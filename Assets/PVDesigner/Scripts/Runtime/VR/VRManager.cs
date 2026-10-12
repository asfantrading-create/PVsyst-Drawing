using System.Collections;
using System.Collections.Generic;
using PVDesigner.App;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR;
#if PVD_XR_MANAGEMENT
using UnityEngine.XR.Management;
#endif

namespace PVDesigner.VR
{
    /// <summary>
    /// Virtual reality mode (OpenXR). Starts / stops the XR loader at runtime so the same build
    /// works on a desktop screen and in a headset, and provides:
    /// laser pointer UI, teleport, snap turn, grab / scale of the model (table-top mode)
    /// and sun time control from the left stick.
    /// </summary>
    public class VRManager : MonoBehaviour
    {
        public enum ViewMode
        {
            /// <summary>1:1 scale, walk through the plant.</summary>
            Walk,
            /// <summary>Scaled model on a virtual table in front of the user.</summary>
            TableTop
        }

        public bool IsActive { get; private set; }
        public ViewMode Mode { get; private set; } = ViewMode.TableTop;
        public string StatusText { get; private set; } = "Not running";

        private PVDesignerApp _app;
        private Transform _rig;
        private Camera _head;
        private Transform _left, _right;
        private Camera _pointerCamera;
        private LineRenderer _laser;
        private Transform _reticle;
        private Transform _teleportMarker;
        private VRUIInputModule _uiModule;
        private BaseInputModule _desktopModule;
        private readonly XRControllerState _leftState = new XRControllerState(XRNode.LeftHand);
        private readonly XRControllerState _rightState = new XRControllerState(XRNode.RightHand);
        private bool _starting;
        private bool _weStartedXr;
        private bool _snapReady = true;
        private bool _uiVisible = true;
        private float _activeSince;
        private bool _placePanelPending;
        private float _dayAccumulator;
        private bool _triggerStartedOnUi;

        // Grab state
        private bool _grabbing;
        private bool _twoHand;
        private Vector3 _grabWorldPosInHand;
        private Quaternion _grabWorldRotInHand;
        private float _twoHandStartDistance;
        private float _twoHandStartScale;
        private float _twoHandStartYaw;
        private float _worldStartYaw;
        private Vector3 _twoHandStartMid;
        private Vector3 _worldStartPos;

        public void Initialize(PVDesignerApp app)
        {
            _app = app;
            StartCoroutine(DetectRunningXr());
        }

        /// <summary>Standalone headsets (Quest APK) start with XR already running: switch to VR automatically.</summary>
        private IEnumerator DetectRunningXr()
        {
            yield return null;
            yield return null;
            if (XRSettings.isDeviceActive && !IsActive)
            {
                _weStartedXr = false;
                EnterVrMode();
            }
        }

        public void Toggle()
        {
            if (IsActive && Application.platform == RuntimePlatform.Android)
            {
                _app.Status("The standalone headset version always runs in VR.");
                return;
            }
            if (IsActive) StopVR();
            else StartVR();
        }

        public void StartVR()
        {
            if (IsActive || _starting) return;
            StartCoroutine(StartRoutine());
        }

        private IEnumerator StartRoutine()
        {
            _starting = true;
            StatusText = "Starting OpenXR ...";
            _app.Status(StatusText);
#if PVD_XR_MANAGEMENT
            var settings = XRGeneralSettings.Instance;
            var manager = settings != null ? settings.Manager : null;
            if (manager == null)
            {
                Fail("XR Plug-in Management is not configured. Run 'PV Designer > Setup > Configure project' in the Unity editor.");
                yield break;
            }
            if (manager.activeLoader == null)
            {
                yield return manager.InitializeLoader();
                if (manager.activeLoader == null)
                {
                    Fail("No VR headset found. Connect the headset, start its OpenXR runtime (Meta Quest Link, SteamVR, WMR ...) and try again.");
                    yield break;
                }
                _weStartedXr = true;
            }
            manager.StartSubsystems();
            yield return null;
            EnterVrMode();
#else
            Fail("The XR Plug-in Management package is missing from this build.");
            yield break;
#endif
        }

        private void Fail(string message)
        {
            _starting = false;
            StatusText = message;
            _app.Status(message, true);
            _app.Window.Dialog.Show("Virtual reality", message);
        }

        public void StopVR()
        {
            if (!IsActive) return;
            ExitVrMode();
#if PVD_XR_MANAGEMENT
            var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            if (manager != null && manager.activeLoader != null && _weStartedXr)
            {
                manager.StopSubsystems();
                manager.DeinitializeLoader();
            }
#endif
            _weStartedXr = false;
            StatusText = "Not running";
            _app.Status("VR stopped.");
        }

        private void OnApplicationQuit()
        {
            if (IsActive) StopVR();
        }

        // ------------------------------------------------------------------ mode switching

        private void EnterVrMode()
        {
            _starting = false;
            _activeSince = Time.unscaledTime;
            BuildRig();
            SetTrackingOriginFloor();

            _app.OrbitCamera.Camera.enabled = false;
            _app.OrbitCamera.InputEnabled = false;
            _rig.gameObject.SetActive(true);
            _head.enabled = true;

            // UI: world space panel driven by the laser.
            var es = EventSystem.current;
            _desktopModule = null;
            foreach (var m in es.GetComponents<BaseInputModule>())
            {
                if (!(m is VRUIInputModule) && _desktopModule == null) _desktopModule = m;
                m.enabled = false;
            }
            _uiModule = es.gameObject.GetOrAdd<VRUIInputModule>();
            _uiModule.enabled = true;
            _uiModule.PointerCamera = _pointerCamera;
            _uiModule.IsPressed = () => _rightState.TriggerPressed;
            _uiModule.ScrollDelta = () => _uiModule.IsOverUi ? new Vector2(0, _rightState.Stick.y * 4f) : Vector2.zero;

            _app.Window.SetWorldSpace(true, _pointerCamera);
            _app.Window.Root.SetParent(_rig, false);
            _uiVisible = true;
            IsActive = true;
            SetMode(Mode);
            // The head pose is not known yet: the panel is placed on the first tracked frame.
            _placePanelPending = true;
            _dayAccumulator = _app.Sun.DayOfYear;
            StatusText = "Running (" + XRSettings.loadedDeviceName + ")";
            _app.Window.SetVrState(true);
            _app.Status("VR active. A: walk / table-top, grip: grab the model, trigger: click / teleport, Y or menu: panel.");
        }

        private void ExitVrMode()
        {
            IsActive = false;
            var es = EventSystem.current;
            if (_uiModule != null) _uiModule.enabled = false;
            if (es != null)
            {
                if (_desktopModule != null) _desktopModule.enabled = true;
                else PVDesignerApp.AddDesktopInputModule(es.gameObject).enabled = true;
            }
            _app.Window.SetWorldSpace(false, null);
            _app.Window.Root.gameObject.SetActive(true);
            _app.Scene.World.localPosition = Vector3.zero;
            _app.Scene.World.localRotation = Quaternion.identity;
            _app.Scene.World.localScale = Vector3.one;
            _app.Scene.SetLineScale(1f);
            QualitySettings.shadowDistance = 600f;
            QualitySettings.shadowCascades = 4;
            if (_rig != null) _rig.gameObject.SetActive(false);
            _app.OrbitCamera.Camera.enabled = true;
            _app.OrbitCamera.InputEnabled = true;
            _app.Window.SetVrState(false);
        }

        private static void SetTrackingOriginFloor()
        {
            var subsystems = new List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(subsystems);
            foreach (var s in subsystems) s.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
        }

        public void SetMode(ViewMode mode)
        {
            Mode = mode;
            if (!IsActive) return;
            var world = _app.Scene.World;
            var bounds = _app.Scene.SiteBounds();
            if (mode == ViewMode.TableTop)
            {
                float size = Mathf.Max(bounds.size.x, bounds.size.z, 10f);
                float scale = 1.4f / size;
                world.localScale = Vector3.one * scale;
                world.localRotation = Quaternion.identity;
                // Centre the model 0.9 m above the floor, 1.2 m in front of the rig origin.
                var headForward = HorizontalForward();
                var anchor = _rig.position + headForward * 1.2f + Vector3.up * 0.9f;
                world.position = anchor - world.rotation * (new Vector3(bounds.center.x, 0, bounds.center.z) * scale);
            }
            else
            {
                world.localScale = Vector3.one;
                world.localRotation = Quaternion.identity;
                world.localPosition = Vector3.zero;
                // Stand at the southern edge of the site looking north (or the opposite in the southern hemisphere).
                bool north = _app.Project.Location.Latitude >= 0;
                var start = new Vector3(bounds.center.x, 0, north ? bounds.min.z - 8f : bounds.max.z + 8f);
                _rig.position = start;
                _rig.rotation = Quaternion.Euler(0, north ? 0 : 180, 0);
            }
            _app.Scene.SetLineScale(world.localScale.x);
            _app.Sun.Recompute(); // resizes the sun path for the new scale
            // Shadow maps must cover the model at its current scale.
            QualitySettings.shadowDistance = mode == ViewMode.TableTop ? 12f : 250f;
            QualitySettings.shadowCascades = Application.isMobilePlatform ? 2 : 4;
            _app.Status(mode == ViewMode.TableTop ? "Table-top model. Grip to move, both grips to scale and rotate." : "Walk mode (1:1). Point at the ground and pull the trigger to teleport.");
        }

        private Vector3 HorizontalForward()
        {
            var f = _head != null ? _head.transform.forward : Vector3.forward;
            f.y = 0;
            return f.sqrMagnitude < 1e-4f ? _rig.forward : f.normalized;
        }

        private void PlacePanelInFront()
        {
            var ui = _app.Window.Root;
            var headPos = _head.transform.position;
            var fwd = HorizontalForward();
            var right = Vector3.Cross(Vector3.up, fwd);
            ui.position = headPos + fwd * 1.1f - right * 0.25f + Vector3.down * 0.15f;
            ui.rotation = Quaternion.LookRotation(ui.position - headPos, Vector3.up);
        }

        // ------------------------------------------------------------------ rig

        private void BuildRig()
        {
            if (_rig != null) return;
            _rig = new GameObject("XR Rig").transform;
            DontDestroyOnLoad(_rig.gameObject);

            var headGo = new GameObject("Head");
            headGo.transform.SetParent(_rig, false);
            _head = headGo.AddComponent<Camera>();
            _head.nearClipPlane = 0.02f;
            _head.farClipPlane = 20000f;
            _head.stereoTargetEye = StereoTargetEyeMask.Both;
            _head.clearFlags = CameraClearFlags.Skybox;

            _left = CreateHand("Left Hand");
            _right = CreateHand("Right Hand");

            var pointer = new GameObject("Pointer");
            pointer.transform.SetParent(_right, false);
            _pointerCamera = pointer.AddComponent<Camera>();
            _pointerCamera.enabled = false;
            _pointerCamera.stereoTargetEye = StereoTargetEyeMask.None;
            _pointerCamera.fieldOfView = 1f;
            _pointerCamera.nearClipPlane = 0.01f;
            _pointerCamera.farClipPlane = 100f;

            _laser = _app.Scene.CreateLine("Laser", new Color(0.3f, 0.8f, 1f, 0.9f), 0.004f, _rig);
            _laser.useWorldSpace = true;
            _laser.positionCount = 2;

            _reticle = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;
            Destroy(_reticle.GetComponent<Collider>());
            _reticle.SetParent(_rig, false);
            _reticle.localScale = Vector3.one * 0.015f;
            _reticle.GetComponent<MeshRenderer>().sharedMaterial = _app.Scene.Materials.Overlay;

            _teleportMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder).transform;
            Destroy(_teleportMarker.GetComponent<Collider>());
            _teleportMarker.SetParent(_rig.parent, false);
            _teleportMarker.localScale = new Vector3(0.6f, 0.01f, 0.6f);
            _teleportMarker.GetComponent<MeshRenderer>().sharedMaterial = _app.Scene.Materials.Overlay;
            _teleportMarker.gameObject.SetActive(false);
        }

        private Transform CreateHand(string name)
        {
            var hand = new GameObject(name).transform;
            hand.SetParent(_rig, false);
            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(hand, false);
            body.transform.localRotation = Quaternion.Euler(90, 0, 0);
            body.transform.localScale = new Vector3(0.035f, 0.06f, 0.035f);
            body.transform.localPosition = new Vector3(0, -0.01f, -0.03f);
            body.GetComponent<MeshRenderer>().sharedMaterial = _app.Scene.Materials.Metal;
            var ring = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(ring.GetComponent<Collider>());
            ring.transform.SetParent(hand, false);
            ring.transform.localScale = new Vector3(0.05f, 0.02f, 0.05f);
            ring.transform.localPosition = new Vector3(0, 0.02f, 0.03f);
            ring.GetComponent<MeshRenderer>().sharedMaterial = _app.Scene.Materials.Structure;
            return hand;
        }

        // ------------------------------------------------------------------ per frame

        private void OnEnable() => Application.onBeforeRender += OnBeforeRender;
        private void OnDisable() => Application.onBeforeRender -= OnBeforeRender;

        private void OnBeforeRender()
        {
            if (IsActive) UpdatePoses();
        }

        private void UpdatePoses()
        {
            var headDevice = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
            if (!headDevice.isValid) headDevice = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (headDevice.isValid)
            {
                if (headDevice.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 p) ||
                    headDevice.TryGetFeatureValue(CommonUsages.devicePosition, out p))
                    _head.transform.localPosition = p;
                if (headDevice.TryGetFeatureValue(CommonUsages.centerEyeRotation, out Quaternion r) ||
                    headDevice.TryGetFeatureValue(CommonUsages.deviceRotation, out r))
                    _head.transform.localRotation = r;
            }
            _left.localPosition = _leftState.GripPosition;
            _left.localRotation = _leftState.GripRotation;
            _right.localPosition = _rightState.GripPosition;
            _right.localRotation = _rightState.GripRotation;
            // Pointer camera follows the aim pose (expressed relative to the grip transform).
            _pointerCamera.transform.localPosition = Quaternion.Inverse(_rightState.GripRotation) * (_rightState.PointerPosition - _rightState.GripPosition);
            _pointerCamera.transform.localRotation = Quaternion.Inverse(_rightState.GripRotation) * _rightState.PointerRotation;
        }

        private void Update()
        {
            if (!IsActive) return;
            // The headset may take a moment to report active after the loader starts.
            if (_weStartedXr && !XRSettings.isDeviceActive && Time.unscaledTime - _activeSince > 5f)
            {
                // Runtime lost (headset disconnected).
                StopVR();
                return;
            }
            _leftState.Update();
            _rightState.Update();
            UpdatePoses();

            _left.gameObject.SetActive(_leftState.IsTracked || _leftState.Device.isValid);
            _right.gameObject.SetActive(_rightState.IsTracked || _rightState.Device.isValid);

            if (_placePanelPending && _head.transform.localPosition.y > 0.3f)
            {
                _placePanelPending = false;
                PlacePanelInFront();
                if (Mode == ViewMode.TableTop) SetMode(ViewMode.TableTop);
            }

            HandleButtons();
            HandleSticks();
            HandleGrab();
            HandleLaser();
        }

        private void HandleButtons()
        {
            if (_rightState.PrimaryDown) SetMode(Mode == ViewMode.Walk ? ViewMode.TableTop : ViewMode.Walk);
            if (_leftState.SecondaryDown || _leftState.MenuDown || _rightState.MenuDown)
            {
                _uiVisible = !_uiVisible;
                _app.Window.Root.gameObject.SetActive(_uiVisible);
                if (_uiVisible) PlacePanelInFront();
            }
            if (_rightState.SecondaryDown) PlacePanelInFront();
            if (_leftState.PrimaryDown) _app.Sun.Animate = !_app.Sun.Animate;
        }

        private void HandleSticks()
        {
            // Left stick: X = hour, Y = day of year.
            var ls = _leftState.Stick;
            if (ls.sqrMagnitude > 0.04f)
            {
                float hour = _app.Sun.Hour + ls.x * 2.5f * Time.deltaTime;
                // Accumulate fractional days so the speed does not depend on the frame rate.
                if (Mathf.Abs(_dayAccumulator - _app.Sun.DayOfYear) >= 1f) _dayAccumulator = _app.Sun.DayOfYear;
                if (Mathf.Abs(ls.y) >= 0.3f) _dayAccumulator = Mathf.Clamp(_dayAccumulator + ls.y * 40f * Time.deltaTime, 1f, 365.49f);
                int d = Mathf.Clamp(Mathf.RoundToInt(_dayAccumulator), 1, 365);
                _app.Sun.SetDayAndHour(d, Mathf.Clamp(hour, 0f, 23.99f));
            }

            // Right stick X: snap turn (when not scrolling a panel).
            var rs = _rightState.Stick;
            if (_uiModule != null && _uiModule.IsOverUi) return;
            if (Mathf.Abs(rs.x) > 0.75f && _snapReady)
            {
                _snapReady = false;
                var pivot = _head.transform.position;
                _rig.RotateAround(pivot, Vector3.up, Mathf.Sign(rs.x) * 30f);
            }
            else if (Mathf.Abs(rs.x) < 0.3f)
            {
                _snapReady = true;
            }
        }

        private void HandleGrab()
        {
            var world = _app.Scene.World;
            bool l = _leftState.GripPressed, r = _rightState.GripPressed;
            if (!l && !r)
            {
                _grabbing = false;
                return;
            }

            if (l && r)
            {
                Vector3 a = _left.position, b = _right.position;
                Vector3 mid = (a + b) * 0.5f;
                float dist = Vector3.Distance(a, b);
                var dir = b - a;
                float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                if (!_twoHand || !_grabbing)
                {
                    _twoHand = true;
                    _grabbing = true;
                    _twoHandStartDistance = Mathf.Max(0.01f, dist);
                    _twoHandStartScale = world.localScale.x;
                    _twoHandStartYaw = yaw;
                    _worldStartYaw = world.eulerAngles.y;
                    _twoHandStartMid = mid;
                    _worldStartPos = world.position;
                    _leftState.Haptic(0.3f, 0.05f);
                    _rightState.Haptic(0.3f, 0.05f);
                }
                float scale = _twoHandStartScale;
                if (Mode == ViewMode.TableTop)
                    scale = Mathf.Clamp(_twoHandStartScale * dist / _twoHandStartDistance, 0.0005f, 1f);
                float dyaw = Mathf.DeltaAngle(_twoHandStartYaw, yaw);
                // Rotate and scale about the start midpoint, then follow the midpoint.
                var rel = _worldStartPos - _twoHandStartMid;
                rel = Quaternion.Euler(0, dyaw, 0) * rel * (scale / _twoHandStartScale);
                world.localScale = Vector3.one * scale;
                _app.Scene.SetLineScale(scale);
                world.rotation = Quaternion.Euler(0, _worldStartYaw + dyaw, 0);
                world.position = mid + rel;
                return;
            }

            var hand = l ? _left : _right;
            if (!_grabbing || _twoHand)
            {
                _grabbing = true;
                _twoHand = false;
                _grabWorldPosInHand = Quaternion.Inverse(YawOnly(hand.rotation)) * (world.position - hand.position);
                _grabWorldRotInHand = Quaternion.Inverse(YawOnly(hand.rotation)) * world.rotation;
                (l ? _leftState : _rightState).Haptic(0.25f, 0.04f);
            }
            var yawRot = YawOnly(hand.rotation);
            world.position = hand.position + yawRot * _grabWorldPosInHand;
            if (Mode == ViewMode.TableTop) world.rotation = yawRot * _grabWorldRotInHand;
            else world.rotation = Quaternion.Euler(0, (yawRot * _grabWorldRotInHand).eulerAngles.y, 0);
        }

        private static Quaternion YawOnly(Quaternion q) => Quaternion.Euler(0, q.eulerAngles.y, 0);

        private void HandleLaser()
        {
            var origin = _pointerCamera.transform.position;
            var dir = _pointerCamera.transform.forward;
            Vector3 end = origin + dir * 8f;
            bool showTeleport = false;
            if (_rightState.TriggerDown) _triggerStartedOnUi = _uiModule != null && _uiModule.IsOverUi;

            if (_uiModule != null && _uiModule.IsOverUi)
            {
                end = _uiModule.CurrentRaycast.worldPosition;
            }
            else
            {
                var world = _app.Scene.World;
                var ground = new Plane(world.up, world.position);
                var ray = new Ray(origin, dir);
                if (ground.Raycast(ray, out float t) && t < 300f)
                {
                    end = ray.GetPoint(t);
                    if (Mode == ViewMode.Walk && !_triggerStartedOnUi)
                    {
                        showTeleport = _rightState.TriggerPressed;
                        if (_rightState.TriggerUp) Teleport(end);
                    }
                }
            }

            _laser.SetPosition(0, origin);
            _laser.SetPosition(1, end);
            _reticle.position = end;
            _teleportMarker.gameObject.SetActive(showTeleport);
            if (showTeleport) _teleportMarker.position = end + Vector3.up * 0.01f;
        }

        private void Teleport(Vector3 target)
        {
            var headOffset = _head.transform.position - _rig.position;
            headOffset.y = 0;
            _rig.position = new Vector3(target.x, _app.Scene.World.position.y, target.z) - headOffset;
            _rightState.Haptic(0.2f, 0.03f);
        }
    }
}
