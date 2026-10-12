using UnityEngine;
#if ENABLE_INPUT_SYSTEM && PVD_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PVDesigner.InputHandling
{
    /// <summary>
    /// Desktop mouse / keyboard access that works with the legacy Input Manager,
    /// the Input System package, or both (Player Settings &gt; Active Input Handling).
    /// </summary>
    public static class InputAdapter
    {
        public enum Key
        {
            W, A, S, D, Q, E, F, R, Delete, Backspace, Escape, Enter, Shift, Ctrl, Z, Y, Space, T
        }

#if ENABLE_LEGACY_INPUT_MANAGER
        public static Vector2 MousePosition => Input.mousePosition;
        public static float Scroll => Input.mouseScrollDelta.y;
        public static bool MouseHeld(int button) => Input.GetMouseButton(button);
        public static bool MouseDown(int button) => Input.GetMouseButtonDown(button);
        public static bool MouseUp(int button) => Input.GetMouseButtonUp(button);
        public static bool KeyHeld(Key k) => Map(k, Input.GetKey);
        public static bool KeyDown(Key k) => Map(k, Input.GetKeyDown);

        private static bool Map(Key k, System.Func<KeyCode, bool> f)
        {
            switch (k)
            {
                case Key.Shift: return f(KeyCode.LeftShift) || f(KeyCode.RightShift);
                case Key.Ctrl: return f(KeyCode.LeftControl) || f(KeyCode.RightControl) || f(KeyCode.LeftCommand) || f(KeyCode.RightCommand);
                case Key.Enter: return f(KeyCode.Return) || f(KeyCode.KeypadEnter);
                case Key.Delete: return f(KeyCode.Delete);
                case Key.Backspace: return f(KeyCode.Backspace);
                case Key.Escape: return f(KeyCode.Escape);
                case Key.Space: return f(KeyCode.Space);
                default: return f((KeyCode)System.Enum.Parse(typeof(KeyCode), k.ToString()));
            }
        }
#elif ENABLE_INPUT_SYSTEM && PVD_INPUT_SYSTEM
        public static Vector2 MousePosition => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        public static float Scroll
        {
            get
            {
                if (Mouse.current == null) return 0f;
                // Input System 1.8+ reports [-1, 1] per notch by default; older settings report ~120 per notch.
                float y = Mouse.current.scroll.ReadValue().y;
                return Mathf.Abs(y) > 3f ? y / 120f : y;
            }
        }

        private static UnityEngine.InputSystem.Controls.ButtonControl Button(int b)
        {
            var m = Mouse.current;
            if (m == null) return null;
            return b == 0 ? m.leftButton : b == 1 ? m.rightButton : m.middleButton;
        }

        public static bool MouseHeld(int button) => Button(button)?.isPressed ?? false;
        public static bool MouseDown(int button) => Button(button)?.wasPressedThisFrame ?? false;
        public static bool MouseUp(int button) => Button(button)?.wasReleasedThisFrame ?? false;

        private static UnityEngine.InputSystem.Controls.KeyControl[] Controls(Key k)
        {
            var kb = Keyboard.current;
            if (kb == null) return new UnityEngine.InputSystem.Controls.KeyControl[0];
            switch (k)
            {
                case Key.Shift: return new[] { kb.leftShiftKey, kb.rightShiftKey };
                case Key.Ctrl: return new[] { kb.leftCtrlKey, kb.rightCtrlKey, kb.leftCommandKey, kb.rightCommandKey };
                case Key.Enter: return new[] { kb.enterKey, kb.numpadEnterKey };
                case Key.Delete: return new[] { kb.deleteKey };
                case Key.Backspace: return new[] { kb.backspaceKey };
                case Key.Escape: return new[] { kb.escapeKey };
                case Key.Space: return new[] { kb.spaceKey };
                case Key.W: return new[] { kb.wKey };
                case Key.A: return new[] { kb.aKey };
                case Key.S: return new[] { kb.sKey };
                case Key.D: return new[] { kb.dKey };
                case Key.Q: return new[] { kb.qKey };
                case Key.E: return new[] { kb.eKey };
                case Key.F: return new[] { kb.fKey };
                case Key.R: return new[] { kb.rKey };
                case Key.Z: return new[] { kb.zKey };
                case Key.Y: return new[] { kb.yKey };
                case Key.T: return new[] { kb.tKey };
                default: return new UnityEngine.InputSystem.Controls.KeyControl[0];
            }
        }

        public static bool KeyHeld(Key k)
        {
            foreach (var c in Controls(k)) if (c != null && c.isPressed) return true;
            return false;
        }

        public static bool KeyDown(Key k)
        {
            foreach (var c in Controls(k)) if (c != null && c.wasPressedThisFrame) return true;
            return false;
        }
#else
        public static Vector2 MousePosition => Vector2.zero;
        public static float Scroll => 0f;
        public static bool MouseHeld(int button) => false;
        public static bool MouseDown(int button) => false;
        public static bool MouseUp(int button) => false;
        public static bool KeyHeld(Key k) => false;
        public static bool KeyDown(Key k) => false;
#endif
    }
}
