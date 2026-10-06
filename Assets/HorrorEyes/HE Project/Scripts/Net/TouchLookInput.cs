using UnityEngine;
using UnityEngine.EventSystems;

namespace GrannyCoop
{
    /// <summary>
    /// Touch swipe -> camera look deltas, in degrees for the current frame.
    ///
    /// The shipped PlayerController.CameraRotation() reads CrossPlatformInputManager
    /// "Mouse X"/"Mouse Y". On device that is the mouse-only path (Input.GetAxis),
    /// which is always 0 on a phone, so the view never turned. CameraRotation()
    /// now folds these deltas in, so the game's own pitch clamping and spine-angle
    /// handling stay in charge.
    ///
    /// Active Input Handling is "Input Manager (Old)", so this uses Input.GetTouch.
    /// </summary>
    public static class TouchLookInput
    {
        public static float Sensitivity = 0.16f; // degrees per pixel
        public static float Smooth = 20f;        // 0 = no smoothing
        public static float DeadZone = 2f;       // pixels per frame
        public static bool RightHalfOnly = false; // the joystick is filtered by the UI test below

        static int _fingerId = -1;
        static Vector2 _lastPos;
        static float _targetYaw, _targetPitch;
        static float _curYaw, _curPitch;
        static float _outYaw, _outPitch;

        /// <summary>Yaw for this frame in degrees (already smoothed).</summary>
        public static float YawDelta { get; private set; }

        /// <summary>Pitch for this frame in degrees (already smoothed).</summary>
        public static float PitchDelta { get; private set; }

        public static void Tick()
        {
            YawDelta = 0f;
            PitchDelta = 0f;

            if (_fingerId >= 0)
            {
                bool still = false;
                for (int i = 0; i < Input.touchCount; i++)
                {
                    Touch t = Input.GetTouch(i);
                    if (t.fingerId != _fingerId) continue;

                    // Ended/Canceled must reset the drag state, otherwise the view
                    // jumps on release.
                    if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                        break;

                    if (t.phase == TouchPhase.Moved)
                    {
                        // per-frame delta, not "start to now" (that would accelerate)
                        Vector2 d = t.position - _lastPos;
                        _lastPos = t.position;
                        if (d.magnitude >= DeadZone)
                        {
                            _targetYaw += d.x * Sensitivity;
                            _targetPitch -= d.y * Sensitivity;
                        }
                    }
                    still = true;
                    break;
                }
                if (!still) _fingerId = -1;
            }
            else
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    Touch t = Input.GetTouch(i);
                    if (t.phase != TouchPhase.Began) continue;
                    // leave the movement stick alone: never steal a touch that
                    // started on UI (joystick, buttons)
                    if (RightHalfOnly && t.position.x < Screen.width * 0.5f) continue;
                    if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(t.fingerId)) continue;
                    _fingerId = t.fingerId;
                    _lastPos = t.position;
                    break;
                }
            }

            // exponential smoothing so finger jitter does not shake the view
            float k = Smooth <= 0f ? 1f : 1f - Mathf.Exp(-Smooth * Time.deltaTime);
            _curYaw = Mathf.Lerp(_curYaw, _targetYaw, k);
            _curPitch = Mathf.Lerp(_curPitch, _targetPitch, k);

            // hand out only what has not been applied yet
            YawDelta = _curYaw - _outYaw;
            PitchDelta = _curPitch - _outPitch;
            _outYaw = _curYaw;
            _outPitch = _curPitch;
        }

        /// <summary>Drop the drag state, e.g. when the player dies or the scene changes.</summary>
        public static void Reset()
        {
            _fingerId = -1;
            _targetYaw = _targetPitch = _curYaw = _curPitch = _outYaw = _outPitch = 0f;
            YawDelta = PitchDelta = 0f;
        }
    }
}
