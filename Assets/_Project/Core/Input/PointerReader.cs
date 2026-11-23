using System.Collections.Generic;
using UnityEngine;
#if !ENABLE_LEGACY_INPUT_MANAGER && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace KingdomBouns.Core
{
    internal struct PointerState
    {
        public int Id;
        public Vector2 Position;

        public PointerState(int id, Vector2 position)
        {
            Id = id;
            Position = position;
        }
    }

    /// <summary>
    /// Читання дотиків/миші. Працює і зі старим Input Manager, і з новим Input System
    /// (Project Settings → Player → Active Input Handling). Якщо увімкнено обидва — береться старий.
    /// </summary>
    internal static class PointerReader
    {
        /// <param name="pointers">Активні дотики; миша (ліва кнопка) — лише якщо дотиків немає, з Id = -1.</param>
        /// <param name="scroll">Коліщатко: ≈ +1 за клац від себе.</param>
        public static void Read(List<PointerState> pointers, out float scroll, out Vector2 mousePosition, out bool backPressed)
        {
            pointers.Clear();

#if ENABLE_LEGACY_INPUT_MANAGER
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (touch.phase != TouchPhase.Ended && touch.phase != TouchPhase.Canceled)
                    pointers.Add(new PointerState(touch.fingerId, touch.position));
            }

            bool hasMouse = Input.mousePresent;
            mousePosition = hasMouse ? (Vector2)Input.mousePosition : Vector2.zero;
            scroll = hasMouse ? Input.mouseScrollDelta.y : 0f;

            if (Input.touchCount == 0 && hasMouse && Input.GetMouseButton(0))
                pointers.Add(new PointerState(-1, mousePosition));

            backPressed = Input.GetKeyDown(KeyCode.Escape);   // на Android це кнопка «Назад»
#elif ENABLE_INPUT_SYSTEM
            bool anyTouch = false;
            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen != null)
            {
                foreach (var touch in touchscreen.touches)
                {
                    if (!touch.press.isPressed)
                        continue;
                    pointers.Add(new PointerState(touch.touchId.ReadValue(), touch.position.ReadValue()));
                    anyTouch = true;
                }
            }

            Mouse mouse = Mouse.current;
            mousePosition = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
            scroll = 0f;
            if (mouse != null)
            {
                scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 10f)   // старі версії Input System дають 120 за клац
                    scroll /= 120f;
            }

            if (!anyTouch && mouse != null && mouse.leftButton.isPressed)
                pointers.Add(new PointerState(-1, mousePosition));

            Keyboard keyboard = Keyboard.current;
            backPressed = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
#else
            scroll = 0f;
            mousePosition = Vector2.zero;
            backPressed = false;
#endif
        }
    }
}
