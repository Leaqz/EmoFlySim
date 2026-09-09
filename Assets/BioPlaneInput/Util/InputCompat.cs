using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace BioPlane.Util
{
    /// Thin wrapper so the project compiles with either the old Input Manager or
    /// the new Input System package. Only used for desktop testing; in VR the
    /// input is the player's body.
    public static class InputCompat
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        static Keyboard K { get { return Keyboard.current; } }
        static Mouse M { get { return Mouse.current; } }

        public static float Vertical()
        {
            if (K == null) return 0f;
            float v = 0f;
            if (K.upArrowKey.isPressed || K.wKey.isPressed) v += 1f;
            if (K.downArrowKey.isPressed || K.sKey.isPressed) v -= 1f;
            return v;
        }

        public static float Horizontal()
        {
            if (K == null) return 0f;
            float h = 0f;
            if (K.rightArrowKey.isPressed || K.dKey.isPressed) h += 1f;
            if (K.leftArrowKey.isPressed || K.aKey.isPressed) h -= 1f;
            return h;
        }

        public static bool KeyDown(string key)
        {
            if (K == null) return false;
            switch (key)
            {
                case "c": return K.cKey.wasPressedThisFrame;
                case "r": return K.rKey.wasPressedThisFrame;
                case "h": return K.hKey.wasPressedThisFrame;
                case "space": return K.spaceKey.wasPressedThisFrame;
            }
            return false;
        }

        public static bool MouseHeld(int button)
        {
            if (M == null) return false;
            if (button == 0) return M.leftButton.isPressed;
            if (button == 1) return M.rightButton.isPressed;
            return M.middleButton.isPressed;
        }

        public static Vector2 MouseDelta() { return M != null ? M.delta.ReadValue() * 0.1f : Vector2.zero; }
        public static float Scroll() { return M != null ? M.scroll.ReadValue().y * 0.01f : 0f; }
#else
        public static float Vertical() { return Input.GetAxisRaw("Vertical"); }
        public static float Horizontal() { return Input.GetAxisRaw("Horizontal"); }

        public static bool KeyDown(string key)
        {
            switch (key)
            {
                case "c": return Input.GetKeyDown(KeyCode.C);
                case "r": return Input.GetKeyDown(KeyCode.R);
                case "h": return Input.GetKeyDown(KeyCode.H);
                case "space": return Input.GetKeyDown(KeyCode.Space);
            }
            return false;
        }

        public static bool MouseHeld(int button) { return Input.GetMouseButton(button); }
        public static Vector2 MouseDelta() { return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")); }
        public static float Scroll() { return Input.GetAxis("Mouse ScrollWheel"); }
#endif
    }
}
