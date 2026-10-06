using UnityEngine;

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

        /// <summary>Player capsule height, used to size the model.</summary>
        const float PlayerHeight = 1.8f;

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
        static bool TryBounds(GameObject model, out Bounds b)
        {
            b = new Bounds();
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0) return false;

            bool any = false;
            foreach (var r in renderers)
            {
                if (r == null) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
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
            if (h > 0.0001f)
            {
                float k = targetHeight / h;
                if (k > 0f && !float.IsNaN(k) && !float.IsInfinity(k))
                {
                    model.transform.localScale = model.transform.localScale * k;
                    NetConfig.Log("avatar: model height " + h.ToString("F3") + " -> " + targetHeight.ToString("F2")
                                  + " (x" + k.ToString("F4") + ")");
                }
            }

            if (!TryBounds(model, out b)) return;

            Transform root = model.transform.parent;
            float rootY = (root != null) ? root.position.y : 0f;
            float delta = b.min.y - rootY;          // >0 floats, <0 sinks
            if (Mathf.Abs(delta) < 0.001f) return;
            model.transform.position -= new Vector3(0f, delta, 0f);
            AlignOffset = -delta;
            NetConfig.Log("avatar: feet aligned by " + (-delta).ToString("F3"));
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
            string path = (avatarIndex == 1)
                ? "PolyPizza/AnimatedWoman_Female"
                : "PolyPizza/AnimatedHuman_Male";
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

            if (model == null)
            {
                NetConfig.LogError("avatar: no model available");
                return;
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
            if (!_hasTarget)
            {
                // The avatar is created before any state arrives, so it starts
                // at the world origin. Snap on the first update instead of
                // sliding across the level.
                transform.position = pos;
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            }
            _tPos = pos;
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
