using PVDesigner.InputHandling;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PVDesigner.CameraControl
{
    /// <summary>
    /// CAD style navigation: right drag = orbit, middle drag (or Shift + right) = pan,
    /// wheel = zoom to cursor, WASD / QE = move, F = frame the site.
    /// </summary>
    public class OrbitCamera : MonoBehaviour
    {
        public Camera Camera { get; private set; }
        public Vector3 Target = Vector3.zero;
        public float Distance = 220f;
        public float Yaw = 20f;
        public float Pitch = 45f;
        public float MinDistance = 2f;
        public float MaxDistance = 5000f;
        public bool InputEnabled = true;

        private Vector3 _targetGoal;
        private float _distanceGoal, _yawGoal, _pitchGoal;
        private Vector2 _lastMouse;
        private bool _dragging;

        public void Initialize()
        {
            Camera = gameObject.GetComponent<Camera>();
            if (Camera == null) Camera = gameObject.AddComponent<Camera>();
            gameObject.tag = "MainCamera";
            Camera.nearClipPlane = 0.1f;
            Camera.farClipPlane = 20000f;
            Camera.fieldOfView = 50f;
            Camera.clearFlags = CameraClearFlags.Skybox;
            Camera.stereoTargetEye = StereoTargetEyeMask.None;
            Camera.allowHDR = true;
            Camera.allowMSAA = true;
            _targetGoal = Target;
            _distanceGoal = Distance;
            _yawGoal = Yaw;
            _pitchGoal = Pitch;
            Apply();
        }

        public void Frame(Bounds b, bool topView = false)
        {
            _targetGoal = new Vector3(b.center.x, 0, b.center.z);
            float size = Mathf.Max(b.size.x, b.size.z, 20f);
            float fov = Camera != null ? Camera.fieldOfView : 50f;
            _distanceGoal = Mathf.Clamp(size * 0.6f / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad), MinDistance, MaxDistance);
            if (topView)
            {
                _pitchGoal = 89.5f;
                _yawGoal = 0f;
            }
            else if (_pitchGoal > 80f)
            {
                _pitchGoal = 45f;
                _yawGoal = 20f;
            }
        }

        public void SetView(float yaw, float pitch)
        {
            _yawGoal = yaw;
            _pitchGoal = Mathf.Clamp(pitch, 2f, 89.5f);
        }

        private bool PointerOverUi =>
            EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        private void Update()
        {
            if (Camera == null || !Camera.enabled) return;
            if (InputEnabled) HandleInput();
            float k = 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime);
            Target = Vector3.Lerp(Target, _targetGoal, k);
            Distance = Mathf.Lerp(Distance, _distanceGoal, k);
            Yaw = Mathf.LerpAngle(Yaw, _yawGoal, k);
            Pitch = Mathf.Lerp(Pitch, _pitchGoal, k);
            Apply();
        }

        private void HandleInput()
        {
            Vector2 mouse = InputAdapter.MousePosition;
            Vector2 delta = mouse - _lastMouse;
            _lastMouse = mouse;

            bool rightDown = InputAdapter.MouseDown(1), middleDown = InputAdapter.MouseDown(2);
            if ((rightDown || middleDown) && !PointerOverUi) _dragging = true;
            if (!InputAdapter.MouseHeld(1) && !InputAdapter.MouseHeld(2)) _dragging = false;

            bool shift = InputAdapter.KeyHeld(InputAdapter.Key.Shift);
            if (_dragging)
            {
                bool pan = InputAdapter.MouseHeld(2) || (InputAdapter.MouseHeld(1) && shift);
                if (pan)
                {
                    float scale = _distanceGoal * 0.0018f;
                    var right = transform.right;
                    var fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                    if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(transform.up, Vector3.up).normalized;
                    _targetGoal -= (right * delta.x + fwd * delta.y) * scale;
                }
                else if (InputAdapter.MouseHeld(1))
                {
                    _yawGoal += delta.x * 0.25f;
                    _pitchGoal = Mathf.Clamp(_pitchGoal - delta.y * 0.2f, 2f, 89.5f);
                }
            }

            float scroll = InputAdapter.Scroll;
            if (Mathf.Abs(scroll) > 0.01f && !PointerOverUi)
            {
                float factor = Mathf.Pow(0.88f, Mathf.Clamp(scroll, -5f, 5f));
                // Zoom towards the point under the cursor.
                var ray = Camera.ScreenPointToRay(mouse);
                if (new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float enter) && enter < MaxDistance * 2f)
                {
                    var hit = ray.GetPoint(enter);
                    _targetGoal = Vector3.Lerp(hit, _targetGoal, factor);
                    _targetGoal.y = 0;
                }
                _distanceGoal = Mathf.Clamp(_distanceGoal * factor, MinDistance, MaxDistance);
            }

            if (UnityEngine.EventSystems.EventSystem.current != null
                && UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject != null
                && UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject.GetComponent<UnityEngine.UI.InputField>() != null)
                return; // typing in a text field

            var move = Vector3.zero;
            if (InputAdapter.KeyHeld(InputAdapter.Key.W)) move += Vector3.forward;
            if (InputAdapter.KeyHeld(InputAdapter.Key.S)) move += Vector3.back;
            if (InputAdapter.KeyHeld(InputAdapter.Key.A)) move += Vector3.left;
            if (InputAdapter.KeyHeld(InputAdapter.Key.D)) move += Vector3.right;
            if (InputAdapter.KeyHeld(InputAdapter.Key.Q)) _yawGoal -= 60f * Time.unscaledDeltaTime;
            if (InputAdapter.KeyHeld(InputAdapter.Key.E)) _yawGoal += 60f * Time.unscaledDeltaTime;
            if (move != Vector3.zero)
            {
                var rot = Quaternion.Euler(0, Yaw, 0);
                float speed = _distanceGoal * (shift ? 1.5f : 0.6f);
                _targetGoal += rot * move.normalized * speed * Time.unscaledDeltaTime;
            }
        }

        private void Apply()
        {
            var rot = Quaternion.Euler(Pitch, Yaw, 0);
            transform.position = Target - rot * Vector3.forward * Distance;
            transform.rotation = rot;
            if (Camera != null) Camera.nearClipPlane = Mathf.Clamp(Distance * 0.002f, 0.05f, 2f);
        }

        /// <summary>Ground point (y = 0) under a screen position.</summary>
        public bool GroundPoint(Vector2 screen, out Vector3 point, float height = 0f)
        {
            point = Vector3.zero;
            if (Camera == null) return false;
            var ray = Camera.ScreenPointToRay(screen);
            if (new Plane(Vector3.up, new Vector3(0, height, 0)).Raycast(ray, out float enter))
            {
                point = ray.GetPoint(enter);
                return true;
            }
            return false;
        }
    }
}
