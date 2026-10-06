using UnityEngine;
using UnityEngine.SceneManagement;

namespace GrannyCoop
{
    /// <summary>
    /// The visible body of the OTHER player (the peer) in a co-op match.
    /// Built at runtime from a Poly.pizza model placed under Resources/PolyPizza/.
    /// Falls back to a copy of the local player's third-person model if the
    /// Poly.pizza model is missing.
    /// </summary>
    public class CoopAvatar : MonoBehaviour
    {
        Animator _animator;
        Animation _legacy;
        AnimationClip _idle, _walk, _run;
        string _playing = "";

        float _spd;          // 0..1 locomotion amount
        bool _crouch;
        string _modelPath;
        float _actionUntil;
        bool _dead;

        // network interpolation targets
        Vector3 _tPos;
        float _tYaw;
        bool _hasTarget;

        /// <summary>Fallback player capsule height when the controller is not around yet.</summary>
        const float PlayerHeight = 1.8f;

        static float _groundOffset = -1f;
        static int _groundScene = int.MinValue;

        /// <summary>
        /// How far the feet sit BELOW the player root.
        ///
        /// PlayerController.Controll() does
        ///     characterController.center = Vector3.down * (normalHeight - height) / 2
        /// so while standing (height == normalHeight) the centre is 0, i.e. the
        /// capsule - and therefore the transform we sync - sits at the player's
        /// WAIST. Placing the model's feet on the reported position therefore
        /// lifts the whole character by half its height: it floats, and its head
        /// ends up around 2.7 m, poking through the ceiling.
        ///
        /// Read per scene from the local player, so every level adapts itself.
        /// </summary>
        public static float GroundOffset()
        {
            int scn = SceneManager.GetActiveScene().buildIndex;
            if (_groundOffset < 0f || scn != _groundScene)
            {
                _groundScene = scn;
                _groundOffset = PlayerHeight * 0.5f;
                var p = FindObjectOfType<PlayerController>();
                if (p != null && p.normalHeight > 0.1f) _groundOffset = p.normalHeight * 0.5f;
                NetConfig.Log("avatar: ground offset " + _groundOffset.ToString("F3")
                              + " (scene " + scn + ")");
            }
            return _groundOffset;
        }

        // exposed for the on-screen diagnostics
        public float MeasuredHeight;
        public float AlignOffset;
        public bool  ModelLoaded;
        public string ModelName = "";
        GameObject _model;
        bool _fitRetried;

        /// <summary>
        /// The Poly.pizza FBX has no pinned import scale (no .meta is shipped),
        /// so its native size cannot be trusted - it can come in far taller
        /// than the level. Measure it and scale it to the player height instead
        /// of forcing localScale = 1.
        /// </summary>
        /// <summary>
        /// Obviously-a-placeholder body so the peer is never invisible while the
        /// Poly.pizza assets are being sorted out.
        /// </summary>
        static GameObject BuildPlaceholder()
        {
            var go = new GameObject("Placeholder");

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(go.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);

            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(go.transform, false);
            head.transform.localPosition = new Vector3(0f, 1.62f, 0f);
            head.transform.localScale = Vector3.one * 0.42f;

            // never block movement: the avatar's own capsule handles collision
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);

            var sh = Shader.Find("Standard");
            if (sh == null) sh = Shader.Find("Mobile/Diffuse");
            if (sh != null)
            {
                var mat = new Material(sh);
                mat.color = new Color(0.95f, 0.35f, 0.25f);
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = mat;
            }
            return go;
        }

        /// <summary>
        /// Bounds of the model's geometry, expressed in the model root's own space.
        /// Uses the mesh / skinned bounds instead of Renderer.bounds: right after
        /// Instantiate the world-space bounds can still be degenerate, and a bogus
        /// height becomes an absurd scale factor (which is exactly how one model
        /// ended up taller than the level and "invisible" to the peer).
        /// </summary>
        static bool TryBounds(GameObject model, out Bounds b)
        {
            b = new Bounds();
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0) return false;

            bool any = false;
            foreach (var r in renderers)
            {
                if (r == null) continue;

                Bounds lb;
                var smr = r as SkinnedMeshRenderer;
                if (smr != null) lb = smr.localBounds;
                else
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    lb = mf.sharedMesh.bounds;
                }

                Matrix4x4 toRoot = model.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
                Vector3 c = lb.center, e = lb.extents;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 p = toRoot.MultiplyPoint3x4(c + new Vector3(
                        (i & 1) == 0 ? -e.x : e.x,
                        (i & 2) == 0 ? -e.y : e.y,
                        (i & 4) == 0 ? -e.z : e.z));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            return any;
        }

        /// <summary>
        /// The two Poly.pizza FBX files do not share a pivot: one has its origin
        /// at the feet, the other at mid-body. Left alone, the second one sits
        /// half-buried in the floor (which also makes it invisible to the peer).
        /// So measure the model, scale it to the player height, then drop its
        /// lowest point onto the avatar root.
        /// </summary>
        void FitModel(GameObject model, float targetHeight)
        {
            if (model == null || targetHeight <= 0f) return;

            Bounds b;
            if (!TryBounds(model, out b)) return;

            float h = b.size.y;
            MeasuredHeight = h;

            // Reject implausible measurements instead of turning them into a wild
            // scale factor. Update() retries once the transforms have settled.
            if (h < 0.05f || h > 1000f)
            {
                NetConfig.LogError("avatar: implausible model height " + h.ToString("F4") + ", skipping scale");
                return;
            }

            float k = targetHeight / h;
            if (float.IsNaN(k) || float.IsInfinity(k) || k <= 0f) return;
            model.transform.localScale = Vector3.one * k;

            // b is in the model root's own space, so the lowest local y (times the
            // scale we just applied) is exactly how far the feet sit from the root.
            float minY = b.min.y * k;
            model.transform.localPosition = new Vector3(0f, -minY, 0f);
            AlignOffset = -minY;
            NetConfig.Log("avatar: height " + h.ToString("F3") + " -> " + targetHeight.ToString("F2")
                          + " (x" + k.ToString("F4") + ") feet " + (-minY).ToString("F3"));
        }

        public static CoopAvatar Create(int avatarIndex, Transform fallbackTpsModel)
        {
            var go = new GameObject("CoopAvatar");
            var av = go.AddComponent<CoopAvatar>();
            av.Build(avatarIndex, fallbackTpsModel);
            return av;
        }

        void Build(int avatarIndex, Transform fallback)
        {
            // Host (index 0) uses a freshly downloaded Quaternius model: it is
            // properly skinned and ships Idle / Walk / Run / Jump / Death clips,
            // unlike the old male mesh which had no armature at all.
            string path = (avatarIndex == 1)
                ? "PolyPizza/AnimatedWoman_Female"
                : "PolyPizza/AnimatedMan_Host";
            _modelPath = path;

            GameObject model = null;
            ModelName = path;
            var prefab = Resources.Load<GameObject>(path);
            if (prefab != null)
            {
                model = Instantiate(prefab);
            }
            else if (fallback != null)
            {
                NetConfig.Log("avatar: Poly.pizza model missing, using fallback TPS model");
                model = Instantiate(fallback.gameObject);
                foreach (var mb in model.GetComponentsInChildren<MonoBehaviour>(true)) Destroy(mb);
                foreach (var col in model.GetComponentsInChildren<Collider>(true)) Destroy(col);
            }

            // A model that imported without any renderer (or a missing asset)
            // would leave the peer invisible, so fall back to a primitive body.
            if (model != null && model.GetComponentsInChildren<Renderer>(true).Length == 0)
            {
                NetConfig.LogError("avatar: '" + path + "' has no renderers, using a placeholder body");
                Destroy(model);
                model = null;
            }
            if (model == null)
            {
                NetConfig.LogError("avatar: no usable model, using a placeholder body");
                model = BuildPlaceholder();
                ModelName = "placeholder";
            }

            model.name = "Model";
            model.transform.SetParent(transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            _model = model;
            FitModel(model, PlayerHeight);
            ModelLoaded = true;

            // solid body so the two players physically block each other.
            // On the host the separate CoopClientProxy already carries the body, so skip here.
            if (!CoopSession.IsHost)
            {
                gameObject.layer = 0; // Default: the player collides with it, like walls
                var col = GetComponent<CapsuleCollider>();
                if (col == null) col = gameObject.AddComponent<CapsuleCollider>();
                col.height = 1.8f; col.radius = 0.35f; col.center = new Vector3(0f, 0.9f, 0f);
                var rb = GetComponent<Rigidbody>();
                if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
                rb.isKinematic = true; rb.useGravity = false;
            }

            // try to animate through the FBX animation clips (needs Rig = Legacy in Unity)
            var clips = Resources.LoadAll<AnimationClip>(path);
            if (clips != null && clips.Length > 0)
            {
                _legacy = model.GetComponent<Animation>();
                if (_legacy == null) _legacy = model.AddComponent<Animation>();
                foreach (var c in clips)
                {
                    if (c == null) continue;
                    string n = c.name.ToLowerInvariant();
                    if (_idle == null && n.Contains("idle")) _idle = c;
                    if (_walk == null && n.Contains("walk")) _walk = c;
                    if (_run == null && n.Contains("run")) _run = c;
                    _legacy.AddClip(c, c.name);
                }
                if (_idle == null && clips.Length > 0) _idle = clips[0];
                _legacy.playAutomatically = false;
                PlayClip(_idle);
            }
            else
            {
                _animator = model.GetComponentInChildren<Animator>();
            }
        }

        void PlayClip(AnimationClip clip)
        {
            if (_legacy == null || clip == null) return;
            if (_playing == clip.name) return;
            _playing = clip.name;
            _legacy.CrossFade(clip.name, 0.15f);
        }

        /// <summary>Feed a fresh network state; the avatar interpolates toward it.</summary>
        public void ApplyState(Vector3 pos, float yaw, float spd, bool crouch)
        {
            if (_dead) return;

            // the peer reports its transform, which sits at the capsule centre
            Vector3 grounded = pos - Vector3.up * GroundOffset();

            if (!_hasTarget)
            {
                // The avatar is created before any state arrives, so it starts
                // at the world origin. Snap on the first update instead of
                // sliding across the level.
                transform.position = grounded;
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            }
            _tPos = grounded;
            _tYaw = yaw;
            _spd = Mathf.Clamp01(spd);
            _crouch = crouch;
            _hasTarget = true;
        }

        void Update()
        {
            // A freshly instantiated prefab can report degenerate renderer
            // bounds for one frame; retry the measurement once the transforms
            // have settled instead of leaving the model at its raw size.
            if (!_fitRetried && _model != null && MeasuredHeight <= 0.0001f)
            {
                _fitRetried = true;
                _model.transform.localPosition = Vector3.zero;
                _model.transform.localScale = Vector3.one;
                FitModel(_model, PlayerHeight);
                NetConfig.Log("avatar: retried fit -> height " + MeasuredHeight.ToString("F3"));
            }

            if (_hasTarget)
            {
                transform.position = Vector3.Lerp(transform.position, _tPos, Time.deltaTime * NetConfig.InterpSpeed);
                var rot = Quaternion.Euler(0f, _tYaw, 0f);
                transform.rotation = Quaternion.Slerp(transform.rotation, rot, Time.deltaTime * NetConfig.InterpSpeed);
            }

            if (_legacy != null && !_dead && Time.time >= _actionUntil)
            {
                if (_spd > 0.6f && _run != null) PlayClip(_run);
                else if (_spd > 0.05f && _walk != null) PlayClip(_walk);
                else PlayClip(_idle);
            }
            else if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                _animator.SetFloat("DirectionX", 0f);
                _animator.SetFloat("DirectionY", _spd);
                _animator.SetBool("Crouch", _crouch);
                _animator.SetBool("Run", _spd > 0.6f);
            }
        }

        /// <summary>Play the death clip once and freeze the avatar.</summary>
        public void PlayDeath()
        {
            _dead = true;
            if (_legacy == null) return;
            AnimationClip found = null;
            var clips = Resources.LoadAll<AnimationClip>(_modelPath);
            foreach (var c in clips)
            {
                if (c != null && c.name.ToLowerInvariant().Contains("death")) { found = c; break; }
            }
            if (found == null) { gameObject.SetActive(false); return; }
            _playing = found.name;
            _legacy.Stop();
            found.wrapMode = WrapMode.Once;
            _legacy.Play(found.name);
        }

        /// <summary>Play a one-shot action clip (e.g. "pickup") on this avatar.</summary>
        public void PlayAction(string kind)
        {
            if (_legacy == null) return;
            string[] wants = (kind == "pickup") ? new string[] { "pick", "work" } : new string[] { kind };
            AnimationClip found = null;
            var clips = Resources.LoadAll<AnimationClip>(_modelPath);
            foreach (var w in wants)
            {
                foreach (var c in clips)
                {
                    if (c != null && c.name.ToLowerInvariant().Contains(w)) { found = c; break; }
                }
                if (found != null) break;
            }
            if (found == null) return;
            _playing = found.name;
            _legacy.Stop();
            found.wrapMode = WrapMode.Once;
            _legacy.Play(found.name);
            _actionUntil = Time.time + Mathf.Max(0.3f, found.length);
        }

        public void Show(bool on) { gameObject.SetActive(on); }
    }
}
