using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace KingdomBouns.CityMap
{
    /// <summary>
    /// Перевірка рейкасту і дій сітки в Play mode (Game view).
    /// Підсвічує клітинку під курсором, показує інфо в кутку й застосовує дії по кліку.
    ///
    /// ЛКМ / тап — поточна дія, Shift+ЛКМ — видалити.
    /// Якщо в сцені є CityCameraController, дія виконується по його кліку (натиснув і відпустив без драгу),
    /// тож перетягування камери не ставить тайли.
    /// 1 / 2 / 3 — Поставити / Видалити / Повернути.  Q / E — поворот тайла для встановлення.
    /// Z / X — попередній / наступний тип клітинки (з тих, що мають тайл у палітрі).
    /// </summary>
    public class HexGridPlayTester : MonoBehaviour
    {
        public HexGrid grid;
        public Camera cam;
        [Tooltip("Який тип ставити. Вигляд — з палітри сітки.")]
        public HexCellType type = HexCellType.Meadow;
        public HexAction action = HexAction.Place;
        [Range(0, 5)] public int turn;
        [Tooltip("Камера з драгом. Порожньо — знайде сама; якщо нема, клік спрацьовує одразу при натисканні.")]
        public CityCameraController cameraController;

        [Header("Підсвітка")]
        public Material lineMaterial;
        public float lineWidth = 0.06f;
        public Color validColor = new Color(0.3f, 1f, 0.4f);
        public Color invalidColor = new Color(1f, 0.3f, 0.3f);
        public bool showInfo = true;
        public bool logClicks = true;

        private LineRenderer line;
        private readonly Vector3[] corners = new Vector3[6];
        private bool hasHit;
        private HexHit hit;

        private void Start()
        {
            if (grid == null) grid = FindAnyObjectByType<HexGrid>();
            if (cam == null) cam = Camera.main;
            if (cameraController == null) cameraController = FindAnyObjectByType<CityCameraController>();
            if (cameraController != null) cameraController.Tapped += OnTap;
            CreateLine();
        }

        private void OnDestroy()
        {
            if (cameraController != null) cameraController.Tapped -= OnTap;
        }

        private void OnTap(Vector2 screenPos)
        {
            if (grid != null && cam != null && grid.Raycast(cam.ScreenPointToRay(screenPos), out var tapHit))
                ApplyAt(tapHit);
        }

        private void ApplyAt(HexHit target)
        {
            var act = ShiftHeld() ? HexAction.Remove : action;
            bool changed = grid.Apply(act, target.cell, type, turn);
            if (logClicks)
                Debug.Log($"[HexGrid] {act} {target.cell} inside={target.inside} tile={(target.HasTile ? target.tile.name : "-")} " +
                          $"collider={target.fromCollider} → {(changed ? "змінено" : "без змін")}");
        }

        private void Update()
        {
            if (grid == null || cam == null) return;
            ReadKeys();

            hasHit = TryGetPointer(out var screenPos) && grid.Raycast(cam.ScreenPointToRay(screenPos), out hit);
            UpdateLine();

            if (cameraController == null && hasHit && ClickedThisFrame()) ApplyAt(hit);
        }

        // ------------------------------------------------------------ ввід

#if ENABLE_INPUT_SYSTEM
        private static bool TryGetPointer(out Vector2 pos)
        {
            // Pointer — миша, тач або перо, що використовувались останніми.
            var p = Pointer.current;
            pos = p != null ? p.position.ReadValue() : Vector2.zero;
            return p != null;
        }

        private static bool ClickedThisFrame() => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;

        private static bool ShiftHeld() => Keyboard.current != null && Keyboard.current.shiftKey.isPressed;

        private void ReadKeys()
        {
            var k = Keyboard.current;
            if (k == null) return;
            if (k.digit1Key.wasPressedThisFrame) action = HexAction.Place;
            if (k.digit2Key.wasPressedThisFrame) action = HexAction.Remove;
            if (k.digit3Key.wasPressedThisFrame) action = HexAction.Rotate;
            if (k.qKey.wasPressedThisFrame) turn = HexMath.Mod6(turn - 1);
            if (k.eKey.wasPressedThisFrame) turn = HexMath.Mod6(turn + 1);
            if (k.zKey.wasPressedThisFrame) CycleType(-1);
            if (k.xKey.wasPressedThisFrame) CycleType(+1);
        }
#else
        private static bool TryGetPointer(out Vector2 pos) { pos = Input.mousePosition; return true; }
        private static bool ClickedThisFrame() => Input.GetMouseButtonDown(0);
        private static bool ShiftHeld() => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        private void ReadKeys()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) action = HexAction.Place;
            if (Input.GetKeyDown(KeyCode.Alpha2)) action = HexAction.Remove;
            if (Input.GetKeyDown(KeyCode.Alpha3)) action = HexAction.Rotate;
            if (Input.GetKeyDown(KeyCode.Q)) turn = HexMath.Mod6(turn - 1);
            if (Input.GetKeyDown(KeyCode.E)) turn = HexMath.Mod6(turn + 1);
            if (Input.GetKeyDown(KeyCode.Z)) CycleType(-1);
            if (Input.GetKeyDown(KeyCode.X)) CycleType(+1);
        }
#endif

        /// <summary>Наступний/попередній тип, для якого в палітрі сітки є тайл.</summary>
        private void CycleType(int d)
        {
            var pal = grid != null ? grid.Palette : null;
            if (pal == null) return;
            var types = new System.Collections.Generic.List<HexCellType>();
            foreach (var e in pal.Entries) if (e.type != HexCellType.None && e.tile != null) types.Add(e.type);
            if (types.Count == 0) return;
            int i = types.IndexOf(type);
            int n = types.Count;
            type = types[((i < 0 ? 0 : i + d) % n + n) % n];
        }

        // ------------------------------------------------------------ підсвітка

        private void CreateLine()
        {
            var go = new GameObject("HexHover");
            go.transform.SetParent(transform, false);
            line = go.AddComponent<LineRenderer>();
            line.loop = true;
            line.positionCount = 6;
            line.useWorldSpace = true;
            line.widthMultiplier = lineWidth;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            if (lineMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                if (shader != null) lineMaterial = new Material(shader);
            }
            line.sharedMaterial = lineMaterial;
            line.enabled = false;
        }

        private void UpdateLine()
        {
            if (line == null) return;
            line.enabled = hasHit;
            if (!hasHit) return;
            grid.GetCellCorners(hit.cell, corners, 0.04f);
            var up = grid.transform.up * 0.03f;
            for (int i = 0; i < 6; i++) line.SetPosition(i, corners[i] + up);
            var c = hit.inside ? validColor : invalidColor;
            line.startColor = line.endColor = c;
            if (line.sharedMaterial != null) line.sharedMaterial.color = c;
        }

        private void OnGUI()
        {
            if (!showInfo) return;
            string cellInfo = hasHit
                ? $"Клітинка: ({hit.cell.x}, {hit.cell.y})  {(hit.inside ? "в межах" : "ПОЗА МЕЖЕЮ")}\n" +
                  $"Стоїть: {(hit.HasTile ? hit.tile.type + " поворот " + (hit.tile.turn * 60) + "° варіант " + hit.tile.variant : "порожньо")}\n" +
                  $"Влучання: {(hit.fromCollider ? "колайдер" : "площина")}"
                : "Курсор не над сіткою";
            GUI.Box(new Rect(10, 10, 330, 120),
                $"{cellInfo}\n\nДія: {action} [1/2/3]   Поворот: {turn * 60}° [Q/E]\n" +
                $"Тип: {type} [Z/X]\nShift+ЛКМ — видалити");
        }
    }
}
