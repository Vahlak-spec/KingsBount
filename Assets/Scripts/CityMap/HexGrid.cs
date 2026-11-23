using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace KingdomBouns.CityMap
{
    public enum HexAction { Place, Remove, Rotate }

    /// <summary>Результат рейкасту в сітку.</summary>
    public struct HexHit
    {
        /// <summary>Клітинка під променем.</summary>
        public Vector2Int cell;
        /// <summary>Точка влучання у світових координатах.</summary>
        public Vector3 point;
        /// <summary>Клітинка в межах радіуса міста.</summary>
        public bool inside;
        /// <summary>Тайл, що стоїть на клітинці (null, якщо порожньо).</summary>
        public HexCell tile;
        /// <summary>true — влучили в колайдер тайла, false — у площину сітки.</summary>
        public bool fromCollider;

        public bool HasTile => tile != null;
    }

    /// <summary>
    /// Кастомна гексова сітка міста. Працює і в Edit mode (через редактор), і в Play mode.
    /// Джерело правди — CityMapData; об'єкти тайлів лежать у дочірньому "Tiles".
    /// У редакторі всі дії йдуть через Undo, у Play mode — звичайні Instantiate/Destroy.
    /// </summary>
    [DisallowMultipleComponent]
    public class HexGrid : MonoBehaviour
    {
        [SerializeField] private CityMapData data;
        [SerializeField] private Transform tilesRoot;

        [Header("Рейкаст")]
        [Tooltip("Спершу шукати влучання в колайдери тайлів, потім у площину сітки.")]
        [SerializeField] private bool usePhysics = true;
        [SerializeField] private LayerMask raycastMask = ~0;
        [SerializeField] private float maxRayDistance = 2000f;

        [Header("Play mode")]
        [Tooltip("У Play mode працювати з копією даних, щоб зміни не записувались в асет.")]
        [SerializeField] private bool cloneDataInPlayMode = true;
        [Tooltip("Перебудувати тайли з даних при старті.")]
        [SerializeField] private bool rebuildOnStart = false;

        [Header("Гізмо")]
        [SerializeField] private bool drawGrid = true;
        [SerializeField] private Color gridColor = new Color(1f, 1f, 1f, 0.35f);

        private readonly Dictionary<Vector2Int, HexCell> instances = new Dictionary<Vector2Int, HexCell>();
        private bool cacheValid;
        private int cachedChildCount;

        // ------------------------------------------------------------ властивості

        public CityMapData Data
        {
            get => data;
            set { data = value; cacheValid = false; }
        }

        public int Radius => data != null ? data.radius : 0;
        public float HexSize => data != null ? data.hexSize : 1f;

        public Transform TilesRoot
        {
            get
            {
                if (tilesRoot != null) return tilesRoot;
                var t = transform.Find("Tiles");
                if (t == null)
                {
                    t = new GameObject("Tiles").transform;
                    t.SetParent(transform, false);
#if UNITY_EDITOR
                    if (!Application.isPlaying) Undo.RegisterCreatedObjectUndo(t.gameObject, "Create Tiles root");
#endif
                }
                tilesRoot = t;
                return tilesRoot;
            }
        }

        // ------------------------------------------------------------ життєвий цикл

        private void Awake()
        {
            if (!Application.isPlaying) return;
            if (cloneDataInPlayMode && data != null)
            {
                data = Instantiate(data);
                data.name += " (runtime)";
            }
            if (rebuildOnStart) RebuildFromData();
        }

        // ------------------------------------------------------------ координати

        public Vector3 CellToWorld(Vector2Int cell) => transform.TransformPoint(HexMath.CellToLocal(cell, HexSize));

        public Vector2Int WorldToCell(Vector3 world) => HexMath.LocalToCell(transform.InverseTransformPoint(world), HexSize);

        public bool IsInside(Vector2Int cell) => data != null && data.IsInside(cell);

        /// <summary>Кути клітинки у світових координатах (6 точок).</summary>
        public void GetCellCorners(Vector2Int cell, Vector3[] result, float inset = 0f)
        {
            var c = HexMath.CellToLocal(cell, HexSize);
            for (int i = 0; i < 6; i++)
                result[i] = transform.TransformPoint(c + HexMath.Corner(i, HexSize * (1f - inset)));
        }

        // ------------------------------------------------------------ рейкаст

        /// <summary>
        /// Шукає клітинку під променем. Спершу колайдери тайлів (точно для високих об'єктів),
        /// якщо промінь не влучив у тайл цієї сітки — площина сітки (працює і на порожніх клітинках).
        /// </summary>
        public bool Raycast(Ray ray, out HexHit hit)
        {
            hit = default;
            if (data == null) return false;

            if (usePhysics)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying) Physics.SyncTransforms();
#endif
                if (Physics.Raycast(ray, out var ph, maxRayDistance, raycastMask, QueryTriggerInteraction.Ignore))
                {
                    var hc = ph.collider.GetComponentInParent<HexCell>();
                    if (hc != null && hc.transform.IsChildOf(TilesRoot))
                    {
                        hit = new HexHit { cell = hc.cell, point = ph.point, inside = IsInside(hc.cell), tile = hc, fromCollider = true };
                        return true;
                    }
                }
            }

            var plane = new Plane(transform.up, transform.position);
            if (!plane.Raycast(ray, out float dist) || dist > maxRayDistance) return false;
            var p = ray.GetPoint(dist);
            var cell = WorldToCell(p);
            hit = new HexHit { cell = cell, point = p, inside = IsInside(cell), tile = GetTile(cell), fromCollider = false };
            return true;
        }

        // ------------------------------------------------------------ доступ до тайлів

        public HexCell GetTile(Vector2Int cell)
        {
            EnsureCache();
            if (instances.TryGetValue(cell, out var hc))
            {
                if (hc != null) return hc;
                RebuildCache();
                return instances.TryGetValue(cell, out hc) ? hc : null;
            }
            return null;
        }

        public void InvalidateCache() => cacheValid = false;

        // Кеш сам себе перевіряє: після Undo/Redo або ручного видалення в ієрархії
        // змінюється кількість дітей або посилання стає null — тоді він перебудовується.
        private void EnsureCache()
        {
            if (!cacheValid || cachedChildCount != TilesRoot.childCount) RebuildCache();
        }

        private void RebuildCache()
        {
            instances.Clear();
            var root = TilesRoot;
            for (int i = 0; i < root.childCount; i++)
            {
                var hc = root.GetChild(i).GetComponent<HexCell>();
                if (hc != null) instances[hc.cell] = hc;
            }
            cachedChildCount = root.childCount;
            cacheValid = true;
        }

        // ------------------------------------------------------------ дії

        /// <summary>
        /// Застосовує дію до клітинки. item — префаб (GameObject) або розумний тайл (SmartTile).
        /// rotateSteps — на скільки кроків по 60° повертати (для Rotate).
        /// </summary>
        public bool Apply(HexAction action, Vector2Int cell, Object item = null, int turn = 0, int rotateSteps = 1)
        {
            switch (action)
            {
                case HexAction.Place: return Place(cell, item, turn);
                case HexAction.Remove: return Remove(cell);
                case HexAction.Rotate: return Rotate(cell, rotateSteps);
                default: return false;
            }
        }

        /// <summary>Ставить префаб або розумний тайл. Повертає false, якщо нічого не змінилося.</summary>
        public bool Place(Vector2Int cell, Object item, int turn)
        {
            if (item is SmartTile smart) return PlaceSmart(cell, smart, turn);
            if (item is GameObject prefab) return PlacePrefab(cell, prefab, turn);
            return false;
        }

        /// <summary>Ставить звичайний префаб (замінює наявний).</summary>
        public bool PlacePrefab(Vector2Int cell, GameObject prefab, int turn)
        {
            if (data == null || prefab == null || !IsInside(cell)) return false;
            turn = HexMath.Mod6(turn);

            var old = GetTile(cell);
            if (old != null && old.smart == null && old.prefab == prefab && old.turn == turn) return false;

            RecordData("Place hex tile");
            if (old != null) Kill(old.gameObject);
            Spawn(cell, prefab, turn, null);
            data.Set(cell, prefab, turn);
            RefreshNeighbours(cell);
            return true;
        }

        /// <summary>
        /// Ставить розумний тайл: Random — випадковий варіант/поворот, Connected — форма за сусідами.
        /// Якщо на клітинці вже стоїть цей самий SmartTile, нічого не робить (щоб протягування мишею не «перекидало кубик»).
        /// </summary>
        public bool PlaceSmart(Vector2Int cell, SmartTile smart, int turn)
        {
            if (data == null || smart == null || !IsInside(cell)) return false;

            var old = GetTile(cell);
            if (old != null && old.smart == smart) return false;

            int mask = smart.IsConnected ? ConnectionMask(cell, smart) : 0;
            if (!smart.TryPick(mask, turn, null, 0, out var pick)) return false;

            RecordData("Place smart tile");
            if (old != null) Kill(old.gameObject);
            Spawn(cell, pick.prefab, pick.turn, smart);
            data.Set(cell, pick.prefab, pick.turn, smart);
            RefreshNeighbours(cell);
            return true;
        }

        public bool Remove(Vector2Int cell)
        {
            if (data == null) return false;
            var old = GetTile(cell);
            bool inData = data.TryGet(cell, out _);
            if (old == null && !inData) return false;

            RecordData("Remove hex tile");
            if (old != null) Kill(old.gameObject);
            instances.Remove(cell);
            data.Remove(cell);
            RefreshNeighbours(cell);
            return true;
        }

        /// <summary>Ручний поворот. Для Connected-тайла він протримається до наступної зміни сусідів.</summary>
        public bool Rotate(Vector2Int cell, int steps)
        {
            if (data == null) return false;
            var tile = GetTile(cell);
            if (tile == null || HexMath.Mod6(steps) == 0) return false;

            RecordData("Rotate hex tile");
            SetTurn(tile, HexMath.Mod6(tile.turn + steps));
            // Префаб із HexConnector після повороту відкриває інші сторони — сусідні дороги перебудуються.
            RefreshNeighbours(cell);
            return true;
        }

        /// <summary>Заповнює всі порожні клітинки в радіусі.</summary>
        public int FillEmpty(Object item, int turn)
        {
            if (data == null || item == null) return 0;
            int n = 0;
            foreach (var cell in HexMath.CellsInRadius(Radius))
                if (GetTile(cell) == null && Place(cell, item, turn)) n++;
            return n;
        }

        /// <summary>Прибирає тайли, що опинились за межею радіуса (наприклад, після його зменшення).</summary>
        public int RemoveOutside()
        {
            if (data == null) return 0;
            EnsureCache();
            var cells = new List<Vector2Int>();
            foreach (var kv in instances) if (!IsInside(kv.Key)) cells.Add(kv.Key);
            foreach (var e in data.Entries) if (!IsInside(e.cell) && !cells.Contains(e.cell)) cells.Add(e.cell);
            foreach (var c in cells) Remove(c);
            return cells.Count;
        }

        public void ClearAll()
        {
            if (data == null) return;
            RecordData("Clear hex map");
            DestroyAllInstances();
            data.Clear();
        }

        /// <summary>Знищує всі об'єкти тайлів і створює їх заново з CityMapData.</summary>
        public void RebuildFromData()
        {
            if (data == null) return;
            DestroyAllInstances();
            foreach (var e in data.Entries)
                if (e.prefab != null) Spawn(e.cell, e.prefab, e.turn, e.smart);
        }

        // ------------------------------------------------------------ розумні тайли

        /// <summary>Маска з'єднаних сусідів: біт e — сусід через ребро e з'єднується з цим тайлом.</summary>
        public int ConnectionMask(Vector2Int cell, SmartTile smart)
        {
            int mask = 0;
            for (int e = 0; e < 6; e++)
            {
                var n = HexMath.Neighbour(cell, e);
                bool connected;
                if (!IsInside(n)) connected = smart.connectOutside;
                else
                {
                    connected = smart.ConnectsTo(GetTile(n), e);
                }
                if (connected) mask |= 1 << e;
            }
            return mask;
        }

        /// <summary>Перераховує форму Connected-тайла на клітинці. Повертає true, якщо щось змінилося.</summary>
        public bool RefreshCell(Vector2Int cell)
        {
            var tile = GetTile(cell);
            if (tile == null || tile.smart == null || !tile.smart.IsConnected) return false;

            int mask = ConnectionMask(cell, tile.smart);
            if (!tile.smart.TryPick(mask, tile.turn, tile.prefab, tile.turn, out var pick)) return false;
            if (pick.prefab == tile.prefab && pick.turn == tile.turn) return false;

            RecordData("Update smart tile");
            if (pick.prefab == tile.prefab)
            {
                SetTurn(tile, pick.turn);
            }
            else
            {
                var smart = tile.smart;
                Kill(tile.gameObject);
                Spawn(cell, pick.prefab, pick.turn, smart);
                data.Set(cell, pick.prefab, pick.turn, smart);
            }
            return true;
        }

        public void RefreshNeighbours(Vector2Int cell)
        {
            for (int e = 0; e < 6; e++)
            {
                var n = HexMath.Neighbour(cell, e);
                if (IsInside(n)) RefreshCell(n);
            }
        }

        /// <summary>Перераховує всі Connected-тайли (наприклад, після зміни форм у SmartTile).</summary>
        public int RefreshAllSmart()
        {
            if (data == null) return 0;
            EnsureCache();
            var cells = new List<Vector2Int>(instances.Keys);
            int n = 0;
            foreach (var c in cells) if (RefreshCell(c)) n++;
            return n;
        }

        private void SetTurn(HexCell tile, int turn)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                Undo.RecordObjects(new Object[] { tile, tile.transform }, "Rotate hex tile");
#endif
            tile.turn = HexMath.Mod6(turn);
            tile.transform.localRotation = HexMath.Turn(tile.turn);
            data.Set(tile.cell, tile.prefab, tile.turn, tile.smart);
        }

        // ------------------------------------------------------------ внутрішнє

        private void DestroyAllInstances()
        {
            var root = TilesRoot;
            for (int i = root.childCount - 1; i >= 0; i--)
                Kill(root.GetChild(i).gameObject);
            instances.Clear();
            cacheValid = true;
        }

        private HexCell Spawn(Vector2Int cell, GameObject prefab, int turn, SmartTile smart)
        {
            GameObject go = null;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                go = PrefabUtility.InstantiatePrefab(prefab, TilesRoot) as GameObject;
#endif
            if (go == null) go = Instantiate(prefab, TilesRoot);

            go.name = $"{prefab.name} ({cell.x},{cell.y})";
            go.transform.localPosition = HexMath.CellToLocal(cell, HexSize);
            go.transform.localRotation = HexMath.Turn(turn);
            go.transform.localScale = Vector3.one * HexSize;

            var hc = go.GetComponent<HexCell>();
            if (hc == null) hc = go.AddComponent<HexCell>();
            hc.cell = cell;
            hc.prefab = prefab;
            hc.turn = HexMath.Mod6(turn);
            hc.smart = smart;

#if UNITY_EDITOR
            if (!Application.isPlaying) Undo.RegisterCreatedObjectUndo(go, "Place hex tile");
#endif
            EnsureCache();
            instances[cell] = hc;
            return hc;
        }

        private void Kill(GameObject go)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Undo.DestroyObjectImmediate(go);
                return;
            }
#endif
            // Destroy відкладений до кінця кадру: від'єднуємо одразу, щоб кеш не бачив «мертвий» тайл.
            go.SetActive(false);
            go.transform.SetParent(null);
            Destroy(go);
        }

        private void RecordData(string undoName)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && data != null) Undo.RecordObject(data, undoName);
#endif
        }

        // ------------------------------------------------------------ гізмо

        private void OnDrawGizmos()
        {
            if (!drawGrid || data == null) return;
            Gizmos.color = gridColor;
            Gizmos.matrix = transform.localToWorldMatrix;
            float s = HexSize;
            foreach (var cell in HexMath.CellsInRadius(Radius))
            {
                var c = HexMath.CellToLocal(cell, s) + Vector3.up * 0.01f;
                for (int i = 0; i < 6; i++)
                    Gizmos.DrawLine(c + HexMath.Corner(i, s), c + HexMath.Corner(i + 1, s));
            }
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
