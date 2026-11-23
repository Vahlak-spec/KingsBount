using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace KingdomBouns.CityMap
{
    /// <summary>
    /// Камера міста в Play mode: ортографічна, під нахилом (обертання камери скрипт не змінює).
    ///
    /// Рух — драгом ЛКМ або одним пальцем по світовій площині XZ: точка землі під пальцем лишається під пальцем.
    /// Зум — колесом миші або pinch двома пальцями, змінює orthographicSize, зумить до курсора / центру pinch.
    /// Межі — прямокутник [minX..maxX] × [minZ..maxZ] на висоті groundY: усі 4 краї кадру (проєкція
    /// кутів екрана на землю) не виходять за нього. Якщо кадр більший за межі — зум обмежується.
    ///
    /// Клік (натиснув і відпустив, не зсунувши далі dragThreshold) видається подією Tapped —
    /// так HexGridPlayTester відрізняє «поставити тайл» від «посунути камеру».
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CityCameraController : MonoBehaviour
    {
        [Header("Межі (світові X / Z)")]
        public float minX = -12f;
        public float maxX = 12f;
        public float minZ = -12f;
        public float maxZ = 12f;
        [Tooltip("Висота площини землі, по якій рахуються драг, зум і межі (верх тайлів = 0).")]
        public float groundY;

        [Header("Зум (orthographicSize)")]
        [Min(0.1f)] public float minSize = 4f;
        [Min(0.1f)] public float maxSize = 16f;
        [Tooltip("На скільки змінюється розмір за один крок колеса (0.12 = 12%).")]
        [Range(0.01f, 0.5f)] public float wheelZoomStep = 0.12f;
        [Tooltip("Зумити до точки під курсором / центру pinch. Вимкнено — до центру екрана.")]
        public bool zoomToPointer = true;

        [Header("Драг")]
        [Tooltip("На скільки пікселів (на 160 dpi) треба зсунути палець, щоб натискання стало драгом, а не кліком.")]
        [Min(0f)] public float dragThreshold = 10f;
        [Tooltip("Не починати драг/зум, якщо вказівник над UI.")]
        public bool ignoreOverUI = true;

        [Header("Інерція")]
        public bool inertia = true;
        [Tooltip("Швидкість згасання інерції (більше — швидше зупиняється).")]
        [Min(0.1f)] public float inertiaDamping = 6f;

        /// <summary>Клік / тап без драгу (екранна позиція).</summary>
        public event Action<Vector2> Tapped;

        /// <summary>Зараз іде драг або pinch.</summary>
        public bool IsDragging => dragging || pinching;

        public Camera Cam { get; private set; }

        // стан вказівника
        private bool pointerDown;
        private bool dragging;
        private bool pinching;
        private bool overUIAtPress;
        private Vector2 pressPos;
        private Vector2 lastPos;
        private float lastPinchDist;
        private Vector2 lastPinchMid;
        private Vector3 velocity;          // світова швидкість інерції (XZ)
        private Vector3 lastDragDelta;

        private void Awake()
        {
            Cam = GetComponent<Camera>();
            if (!Cam.orthographic)
                Debug.LogWarning("[CityCamera] Камера не ортографічна — зум працює через orthographicSize.", this);
        }

        private void OnEnable() => EnhancedTouchSupport.Enable();
        private void OnDisable()
        {
            EnhancedTouchSupport.Disable();
            pointerDown = dragging = pinching = false;
        }

        private void Start()
        {
            Cam.orthographicSize = Mathf.Clamp(Cam.orthographicSize, minSize, EffectiveMaxSize());
            Clamp();
        }

        private void LateUpdate()
        {
            if (ETouch.activeTouches.Count > 0) HandleTouches();
            else HandleMouse();

            if (!pointerDown && !pinching) ApplyInertia();
            Clamp();
        }

        // ------------------------------------------------------------ ввід

        private void HandleMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            var pos = mouse.position.ReadValue();

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f && !(ignoreOverUI && PointerOverUI(-1)))
                ZoomAt(pos, Mathf.Pow(1f - wheelZoomStep, Mathf.Sign(scroll)));

            if (mouse.leftButton.wasPressedThisFrame) PointerDown(pos, -1);
            else if (mouse.leftButton.isPressed && pointerDown) PointerMove(pos);
            else if (mouse.leftButton.wasReleasedThisFrame && pointerDown) PointerUp(pos);
        }

        private void HandleTouches()
        {
            var touches = ETouch.activeTouches;
            if (touches.Count >= 2)
            {
                var a = touches[0].screenPosition;
                var b = touches[1].screenPosition;
                var mid = (a + b) * 0.5f;
                float dist = Vector2.Distance(a, b);

                if (!pinching)
                {
                    pinching = true;
                    dragging = true;      // pinch — це не клік
                    velocity = Vector3.zero;
                    overUIAtPress = ignoreOverUI && (PointerOverUI(touches[0].touchId) || PointerOverUI(touches[1].touchId));
                }
                else if (!overUIAtPress)
                {
                    Pan(lastPinchMid, mid);
                    if (dist > 1f && lastPinchDist > 1f) ZoomAt(mid, lastPinchDist / dist);
                }
                lastPinchDist = dist;
                lastPinchMid = mid;
                return;
            }

            var t = touches[0];
            var pos = t.screenPosition;
            if (pinching)
            {
                // Один палець лишився після pinch — продовжуємо драг з його позиції без стрибка.
                pinching = false;
                pointerDown = true;
                lastPos = pos;
                return;
            }

            var phase = t.phase;
            if (phase == UnityEngine.InputSystem.TouchPhase.Began) PointerDown(pos, t.touchId);
            else if ((phase == UnityEngine.InputSystem.TouchPhase.Moved || phase == UnityEngine.InputSystem.TouchPhase.Stationary) && pointerDown) PointerMove(pos);
            else if ((phase == UnityEngine.InputSystem.TouchPhase.Ended || phase == UnityEngine.InputSystem.TouchPhase.Canceled) && pointerDown)
                PointerUp(pos, phase == UnityEngine.InputSystem.TouchPhase.Canceled);
        }

        private void PointerDown(Vector2 pos, int pointerId)
        {
            pointerDown = true;
            dragging = false;
            overUIAtPress = ignoreOverUI && PointerOverUI(pointerId);
            pressPos = lastPos = pos;
            velocity = Vector3.zero;
            lastDragDelta = Vector3.zero;
        }

        private void PointerMove(Vector2 pos)
        {
            if (overUIAtPress) return;
            if (!dragging)
            {
                float threshold = dragThreshold * DpiScale();
                if ((pos - pressPos).sqrMagnitude < threshold * threshold) return;
                dragging = true;   // від pressPos — точка, яку «схопили», лишається під пальцем
            }
            lastDragDelta = Pan(lastPos, pos);
            if (Time.unscaledDeltaTime > 0f)
                velocity = Vector3.Lerp(velocity, lastDragDelta / Time.unscaledDeltaTime, 0.5f);
            lastPos = pos;
        }

        private void PointerUp(Vector2 pos, bool cancelled = false)
        {
            bool wasDrag = dragging;
            pointerDown = false;
            dragging = false;
            if (!wasDrag && !cancelled && !overUIAtPress) Tapped?.Invoke(pos);
            if (!wasDrag || !inertia) velocity = Vector3.zero;
        }

        private static float DpiScale() => Screen.dpi > 0f ? Screen.dpi / 160f : 1f;

        private static bool PointerOverUI(int pointerId)
        {
            var es = EventSystem.current;
            if (es == null) return false;
            return pointerId < 0 ? es.IsPointerOverGameObject() : es.IsPointerOverGameObject(pointerId);
        }

        // ------------------------------------------------------------ рух і зум

        /// <summary>Зсуває камеру так, щоб точка землі під from опинилась під to. Повертає світовий зсув.</summary>
        private Vector3 Pan(Vector2 from, Vector2 to)
        {
            if (!ScreenToGround(from, out var a) || !ScreenToGround(to, out var b)) return Vector3.zero;
            var delta = a - b;
            delta.y = 0f;
            transform.position += delta;
            return delta;
        }

        /// <summary>Множить orthographicSize на factor, тримаючи на місці точку під screenPos.</summary>
        public void ZoomAt(Vector2 screenPos, float factor)
        {
            float newSize = Mathf.Clamp(Cam.orthographicSize * factor, minSize, EffectiveMaxSize());
            if (Mathf.Approximately(newSize, Cam.orthographicSize)) return;

            if (!zoomToPointer) screenPos = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            bool had = ScreenToGround(screenPos, out var before);
            Cam.orthographicSize = newSize;
            if (had && ScreenToGround(screenPos, out var after))
            {
                var d = before - after;
                d.y = 0f;
                transform.position += d;
            }
            Clamp();
        }

        private void ApplyInertia()
        {
            if (!inertia || velocity.sqrMagnitude < 1e-4f) { velocity = Vector3.zero; return; }
            float dt = Time.unscaledDeltaTime;
            var before = transform.position;
            transform.position += velocity * dt;
            Clamp();
            // Уперлися в межу — гасимо швидкість по цій осі.
            var moved = transform.position - before;
            if (Mathf.Abs(moved.x) < Mathf.Abs(velocity.x * dt) * 0.5f) velocity.x = 0f;
            if (Mathf.Abs(moved.z) < Mathf.Abs(velocity.z * dt) * 0.5f) velocity.z = 0f;
            velocity *= Mathf.Exp(-inertiaDamping * dt);
        }

        // ------------------------------------------------------------ межі

        /// <summary>Промінь з екранної точки на площину землі.</summary>
        public bool ScreenToGround(Vector2 screenPos, out Vector3 point)
        {
            var ray = Cam.ScreenPointToRay(screenPos);
            var plane = new Plane(Vector3.up, new Vector3(0f, groundY, 0f));
            if (plane.Raycast(ray, out float d)) { point = ray.GetPoint(d); return true; }
            point = default;
            return false;
        }

        /// <summary>
        /// Прямокутник (XZ), який займає кадр на землі, відносно позиції камери: x = minX, y = minZ, width/height — розмір.
        /// Для ортографічної камери він залежить лише від розміру, обертання й пропорцій, але не від позиції.
        /// </summary>
        public bool GetFootprintOffset(out Rect offset)
        {
            offset = default;
            var t = transform;
            var fwd = t.forward;
            if (fwd.y > -0.01f) return false; // камера не дивиться вниз

            float h = Cam.orthographicSize;
            float w = h * Cam.aspect;
            float camToGround = groundY - t.position.y;
            float xMin = float.MaxValue, xMax = float.MinValue, zMin = float.MaxValue, zMax = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                float sx = (i & 1) == 0 ? -w : w;
                float sy = (i & 2) == 0 ? -h : h;
                // Кут кадру: зсув від позиції камери в її площині, потім уздовж forward до землі.
                var o = t.right * sx + t.up * sy;
                float k = (camToGround - o.y) / fwd.y;
                var p = o + fwd * k;
                xMin = Mathf.Min(xMin, p.x); xMax = Mathf.Max(xMax, p.x);
                zMin = Mathf.Min(zMin, p.z); zMax = Mathf.Max(zMax, p.z);
            }
            offset = Rect.MinMaxRect(xMin, zMin, xMax, zMax);
            return true;
        }

        /// <summary>Максимальний розмір, за якого кадр ще вміщається в межі.</summary>
        public float EffectiveMaxSize()
        {
            float max = Mathf.Max(minSize, maxSize);
            if (Cam == null || !GetFootprintOffset(out var f) || f.width <= 0f || f.height <= 0f) return max;
            float fitW = (maxX - minX) / f.width;
            float fitH = (maxZ - minZ) / f.height;
            float fit = Cam.orthographicSize * Mathf.Min(fitW, fitH);
            return Mathf.Max(minSize, Mathf.Min(max, fit));
        }

        /// <summary>Тримає кадр у межах. Якщо кадр ширший за межі по осі — центрує по ній.</summary>
        public void Clamp()
        {
            if (Cam == null) Cam = GetComponent<Camera>();
            if (!GetFootprintOffset(out var f)) return;
            var p = transform.position;

            float loX = minX - f.xMin, hiX = maxX - f.xMax;
            p.x = loX <= hiX ? Mathf.Clamp(p.x, loX, hiX) : (loX + hiX) * 0.5f;
            float loZ = minZ - f.yMin, hiZ = maxZ - f.yMax;
            p.z = loZ <= hiZ ? Mathf.Clamp(p.z, loZ, hiZ) : (loZ + hiZ) * 0.5f;

            transform.position = p;
        }

        /// <summary>Виставляє межі за розміром HexGrid (сітка без повороту) з відступом margin.</summary>
        public void FitBoundsToGrid(HexGrid grid, float margin = 1f)
        {
            if (grid == null) return;
            float s = grid.HexSize;
            int r = grid.Radius;
            var c = grid.transform.position;
            float halfX = HexMath.Sqrt3 * s * (r + 0.5f) + margin;
            float halfZ = 1.5f * s * r + s + margin;
            minX = c.x - halfX; maxX = c.x + halfX;
            minZ = c.z - halfZ; maxZ = c.z + halfZ;
            groundY = c.y;
        }

        private void OnValidate()
        {
            if (maxX < minX) maxX = minX;
            if (maxZ < minZ) maxZ = minZ;
            if (maxSize < minSize) maxSize = minSize;
        }

        // ------------------------------------------------------------ гізмо

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);
            var a = new Vector3(minX, groundY, minZ);
            var b = new Vector3(maxX, groundY, minZ);
            var c = new Vector3(maxX, groundY, maxZ);
            var d = new Vector3(minX, groundY, maxZ);
            Gizmos.DrawLine(a, b); Gizmos.DrawLine(b, c); Gizmos.DrawLine(c, d); Gizmos.DrawLine(d, a);

            var cam = GetComponent<Camera>();
            if (cam == null) return;
            if (Cam == null) Cam = cam;
            if (!GetFootprintOffset(out var f)) return;
            var p = transform.position;
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.9f);
            var fa = new Vector3(p.x + f.xMin, groundY, p.z + f.yMin);
            var fb = new Vector3(p.x + f.xMax, groundY, p.z + f.yMin);
            var fc = new Vector3(p.x + f.xMax, groundY, p.z + f.yMax);
            var fd = new Vector3(p.x + f.xMin, groundY, p.z + f.yMax);
            Gizmos.DrawLine(fa, fb); Gizmos.DrawLine(fb, fc); Gizmos.DrawLine(fc, fd); Gizmos.DrawLine(fd, fa);
        }
    }
}
