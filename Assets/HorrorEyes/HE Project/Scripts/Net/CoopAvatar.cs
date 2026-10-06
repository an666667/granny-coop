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

        /// <summary>
        /// The Poly.pizza FBX has no pinned import scale (no .meta is shipped),
        /// so its native size cannot be trusted - it can come in far taller
        /// than the level. Measure it and scale it to the player height instead
        /// of forcing localScale = 1.
        /// </summary>
        static void FitToHeight(GameObject model, float targetHeight)
        {
            if (model == null || targetHeight <= 0f) return;

            var renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0) return;

            bool any = false;
            Bounds b = new Bounds();
            foreach (var r in renderers)
            {
                if (r == null) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            if (!any) return;

            float h = b.size.y;
            if (h <= 0.0001f) return;

            float k = targetHeight / h;
            if (k <= 0f || float.IsNaN(k) || float.IsInfinity(k)) return;

            model.transform.localScale = model.transform.localScale * k;
            NetConfig.Log("avatar: model height " + h.ToString("F3") + " -> " + targetHeight.ToString("F2")
                          + " (x" + k.ToString("F4") + ")");
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
            FitToHeight(model, PlayerHeight);

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
            _tPos = pos;
            _tYaw = yaw;
            _spd = Mathf.Clamp01(spd);
            _crouch = crouch;
            _hasTarget = true;
        }

        void Update()
        {
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
