using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace PVDesigner.VR
{
    /// <summary>
    /// Reads an XR controller through UnityEngine.XR.InputDevices (works with every OpenXR interaction
    /// profile enabled in the project) and tracks button edges.
    /// </summary>
    public class XRControllerState
    {
        public readonly XRNode Node;
        public InputDevice Device;
        public bool IsTracked;

        public Vector3 GripPosition;
        public Quaternion GripRotation = Quaternion.identity;
        public Vector3 PointerPosition;
        public Quaternion PointerRotation = Quaternion.identity;

        public float Trigger;
        public bool TriggerPressed, TriggerDown, TriggerUp;
        public bool GripPressed, GripDown, GripUp;
        public bool PrimaryPressed, PrimaryDown;
        public bool SecondaryPressed, SecondaryDown;
        public bool MenuPressed, MenuDown;
        public Vector2 Stick;

        private static readonly InputFeatureUsage<Vector3> PointerPositionUsage = new InputFeatureUsage<Vector3>("PointerPosition");
        private static readonly InputFeatureUsage<Quaternion> PointerRotationUsage = new InputFeatureUsage<Quaternion>("PointerRotation");
        private readonly List<InputFeatureUsage> _usages = new List<InputFeatureUsage>();
        private bool _hasPointerPose;
        private bool _usagesChecked;

        public XRControllerState(XRNode node)
        {
            Node = node;
        }

        public void Update()
        {
            if (!Device.isValid)
            {
                Device = InputDevices.GetDeviceAtXRNode(Node);
                _usagesChecked = false;
            }
            if (!Device.isValid)
            {
                IsTracked = false;
                ResetButtons();
                return;
            }
            if (!_usagesChecked)
            {
                _usages.Clear();
                Device.TryGetFeatureUsages(_usages);
                _hasPointerPose = _usages.Exists(u => u.name == "PointerPosition") && _usages.Exists(u => u.name == "PointerRotation");
                _usagesChecked = true;
            }

            Device.TryGetFeatureValue(CommonUsages.isTracked, out IsTracked);
            if (Device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 p)) GripPosition = p;
            if (Device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion r)) GripRotation = r;
            if (_hasPointerPose
                && Device.TryGetFeatureValue(PointerPositionUsage, out Vector3 pp)
                && Device.TryGetFeatureValue(PointerRotationUsage, out Quaternion pr))
            {
                PointerPosition = pp;
                PointerRotation = pr;
            }
            else
            {
                // Approximate the aim pose: tilt the grip forward axis down by ~35 degrees.
                PointerRotation = GripRotation * Quaternion.Euler(35f, 0, 0);
                PointerPosition = GripPosition + GripRotation * new Vector3(0, -0.01f, 0.05f);
            }

            Device.TryGetFeatureValue(CommonUsages.trigger, out Trigger);
            Device.TryGetFeatureValue(CommonUsages.triggerButton, out bool trig);
            trig |= Trigger > 0.65f;
            Edge(trig, ref TriggerPressed, out TriggerDown, out TriggerUp);

            Device.TryGetFeatureValue(CommonUsages.grip, out float gripValue);
            Device.TryGetFeatureValue(CommonUsages.gripButton, out bool grip);
            grip |= gripValue > 0.6f;
            Edge(grip, ref GripPressed, out GripDown, out GripUp);

            Device.TryGetFeatureValue(CommonUsages.primaryButton, out bool a);
            Edge(a, ref PrimaryPressed, out PrimaryDown, out _);
            Device.TryGetFeatureValue(CommonUsages.secondaryButton, out bool b);
            Edge(b, ref SecondaryPressed, out SecondaryDown, out _);
            Device.TryGetFeatureValue(CommonUsages.menuButton, out bool m);
            Edge(m, ref MenuPressed, out MenuDown, out _);

            Device.TryGetFeatureValue(CommonUsages.primary2DAxis, out Stick);
        }

        private static void Edge(bool now, ref bool state, out bool down, out bool up)
        {
            down = now && !state;
            up = !now && state;
            state = now;
        }

        private void ResetButtons()
        {
            TriggerDown = TriggerUp = GripDown = GripUp = PrimaryDown = SecondaryDown = MenuDown = false;
            TriggerPressed = GripPressed = PrimaryPressed = SecondaryPressed = MenuPressed = false;
            Trigger = 0;
            Stick = Vector2.zero;
        }

        public void Haptic(float amplitude, float duration)
        {
            if (Device.isValid) Device.SendHapticImpulse(0, amplitude, duration);
        }
    }
}
