using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine.Serialization;
using Object = UnityEngine.Object;

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
        /// <summary>Клітинка в межах радіуса карти.</summary>
        public bool inside;
        /// <summary>Тайл, що стоїть на клітинці (null, якщо порожньо).</summary>
        public HexCell tile;
        /// <summary>true — влучили в колайдер тайла, false — у площину сітки.</summary>
        public bool fromCollider;

        public bool HasTile => tile != null;
    }

    /// <summary>
    /// Вигляд гексової карти. Дані — HexMapData (лише типи клітинок, поворот і варіант),
    /// вигляд кожного типу бере з HexTilePalette. Тайли лежать у дочірньому "Tiles".
    ///
    /// У грі карту передає HexMapService (SetMap). У редакторі карта лежить прямо в компоненті
    /// (поле map серіалізується разом зі сценою), а Hex Map Editor завантажує/зберігає її в JSON.
    /// У редакторі всі дії йдуть через Undo, у Play mode — звичайні Instantiate/Destroy.
    /// </summary>
    [DisallowMultipleComponent]
    public class HexGrid : MonoBehaviour
    {
        [SerializeField] private HexTilePalette palette;
        [SerializeField] private HexMapData map = new HexMapData();
        [SerializeField] private Transform tilesRoot;

        [Tooltip("Старий формат карти (до enum). Лише для імпорту в Hex Map Editor.")]
        [FormerlySerializedAs("data")]
        [SerializeField, HideInInspector] private CityMapData legacyData;

        [Header("Рейкаст")]
        [Tooltip("Спершу шукати влучання в колайдери тайлів, потім у площину сітки.")]
        [SerializeField] private bool usePhysics = true;
        [SerializeField] private LayerMask raycastMask = ~0;
        [SerializeField] private float maxRayDistance = 2000f;

        [Header("Гізмо")]
        [SerializeField] private bool drawGrid = true;
        [SerializeField] private Color gridColor = new Color(1f, 1f, 1f, 0.35f);

        /// <summary>Вміст клітинки змінився (поставили, прибрали, повернули, перебудувався сусід).</summary>
        public event Action<Vector2Int> CellChanged;
        /// <summary>Усю карту перебудовано з даних.</summary>
        public event Action MapRebuilt;

        private readonly Dictionary<Vector2Int, HexCell> instances = new Dictionary<Vector2Int, HexCell>();
        private bool cacheValid;
        private int cachedChildCount;

        // ------------------------------------------------------------ властивості

        public HexMapData Map => map;

        /// <summary>Стара CityMapData, якщо сітку створили до переходу на HexMapData (для імпорту).</summary>
        public CityMapData LegacyData
        {
            get => legacyData;
            set => legacyData = value;
        }

        public HexTilePalette Palette
        {
            get => palette;
            set => palette = value;
        }

        public int Radius => map != null ? map.radius : 0;
        public float HexSize => map != null ? map.hexSize : 1f;

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

        /// <summary>
        /// Підміняє карту. Сітка працює з тим самим екземпляром — зміни через Place/Remove/Rotate
        /// одразу потрапляють у переданий HexMapData.
        /// </summary>
        public void SetMap(HexMapData newMap, bool rebuild = true)
        {
            map = newMap ?? new HexMapData();
            InvalidateCache();
            if (rebuild) RebuildFromData();
        }

        // ------------------------------------------------------------ координати

        public Vector3 CellToWorld(Vector2Int cell) => transform.TransformPoint(HexMath.CellToLocal(cell, HexSize));

        public Vector2Int WorldToCell(Vector3 world) => HexMath.LocalToCell(transform.InverseTransformPoint(world), HexSize);

        public bool IsInside(Vector2Int cell) => map != null && map.IsInside(cell);

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
            if (map == null) return false;

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

        /// <summary>Скинути кеші (після Undo/Redo або ручних змін в ієрархії).</summary>
        public void InvalidateCache()
        {
            cacheValid = false;
            map?.InvalidateIndex();
        }

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

        /// <summary>Застосовує дію до клітинки. rotateSteps — на скільки кроків по 60° повертати (для Rotate).</summary>
        public bool Apply(HexAction action, Vector2Int cell, HexCellType type = HexCellType.None, int turn = 0, int rotateSteps = 1)
        {
            switch (action)
            {
                case HexAction.Place: return Place(cell, type, turn);
                case HexAction.Remove: return Remove(cell);
                case HexAction.Rotate: return Rotate(cell, rotateSteps);
                default: return false;
            }
        }

        /// <summary>
        /// Ставить тип на клітинку. Вигляд — з палітри: префаб ставиться з поворотом turn,
        /// розумний тайл сам вибирає варіант і поворот (Random — випадково, Connected — за сусідами).
        /// Якщо на клітинці вже стоїть цей тип із розумним тайлом, нічого не робить (протягування мишею не «перекидає кубик»).
        /// </summary>
        public bool Place(Vector2Int cell, HexCellType type, int turn = 0)
        {
            if (map == null || type == HexCellType.None || !IsInside(cell)) return false;
            var tile = palette != null ? palette.Get(type) : null;
            if (tile == null)
            {
                Debug.LogWarning($"[HexGrid] Для типу {type} у палітрі не призначено тайл.", this);
                return false;
            }

            turn = HexMath.Mod6(turn);
            var old = GetTile(cell);
            GameObject prefab;
            int variant = 0;
            SmartTile smart = tile as SmartTile;

            if (smart != null)
            {
                if (old != null && old.type == type) return false;
                int mask = smart.IsConnected ? ConnectionMask(cell, smart) : 0;
                if (!smart.TryPick(mask, turn, null, 0, out var pick)) return false;
                prefab = pick.prefab;
                turn = pick.turn;
                variant = Mathf.Max(0, smart.IndexOfVariant(prefab));
            }
            else
            {
                prefab = tile as GameObject;
                if (prefab == null) return false;
                if (old != null && old.type == type && old.prefab == prefab && old.turn == turn) return false;
            }

            RecordUndo("Place hex tile");
            if (old != null) Kill(old.gameObject);
            Spawn(cell, type, prefab, turn, variant, smart);
            map.Set(cell, type, turn, variant);
            RefreshNeighbours(cell);
            CellChanged?.Invoke(cell);
            return true;
        }

        public bool Remove(Vector2Int cell)
        {
            if (map == null) return false;
            var old = GetTile(cell);
            bool inData = map.TryGet(cell, out _);
            if (old == null && !inData) return false;

            RecordUndo("Remove hex tile");
            if (old != null) Kill(old.gameObject);
            instances.Remove(cell);
            map.Remove(cell);
            RefreshNeighbours(cell);
            CellChanged?.Invoke(cell);
            return true;
        }

        /// <summary>Ручний поворот. Для Connected-тайла він протримається до наступної зміни сусідів.</summary>
        public bool Rotate(Vector2Int cell, int steps)
        {
            if (map == null) return false;
            var tile = GetTile(cell);
            if (tile == null || HexMath.Mod6(steps) == 0) return false;

            RecordUndo("Rotate hex tile");
            SetTurn(tile, HexMath.Mod6(tile.turn + steps));
            // Префаб із HexConnector після повороту відкриває інші сторони — сусідні дороги перебудуються.
            RefreshNeighbours(cell);
            CellChanged?.Invoke(cell);
            return true;
        }

        /// <summary>Заповнює всі порожні клітинки в радіусі.</summary>
        public int FillEmpty(HexCellType type, int turn = 0)
        {
            if (map == null || type == HexCellType.None) return 0;
            int n = 0;
            foreach (var cell in HexMath.CellsInRadius(Radius))
                if (GetTile(cell) == null && Place(cell, type, turn)) n++;
            return n;
        }

        /// <summary>Прибирає клітинки, що опинились за межею радіуса (наприклад, після його зменшення).</summary>
        public int RemoveOutside()
        {
            if (map == null) return 0;
            EnsureCache();
            var cells = new List<Vector2Int>();
            foreach (var kv in instances) if (!IsInside(kv.Key)) cells.Add(kv.Key);
            foreach (var c in map.cells) if (!IsInside(c.cell) && !cells.Contains(c.cell)) cells.Add(c.cell);
            foreach (var c in cells) Remove(c);
            return cells.Count;
        }

        /// <summary>Очищає карту (дані й тайли).</summary>
        public void ClearAll()
        {
            if (map == null) return;
            RecordUndo("Clear hex map");
            DestroyAllInstances();
            map.Clear();
            MapRebuilt?.Invoke();
        }

        /// <summary>Прибирає лише об'єкти тайлів; дані карти не змінюються.</summary>
        public void ClearVisuals() => DestroyAllInstances();

        /// <summary>
        /// Перебудовує одну клітинку з HexMapData (після того, як дані змінив хтось інший — наприклад, економіка міста).
        /// Сусідні розумні тайли (дороги) перераховуються. Варіант −1 у даних — вибрати заново.
        /// </summary>
        public bool RebuildCell(Vector2Int cell)
        {
            if (map == null) return false;

            RecordUndo("Rebuild hex cell");
            var old = GetTile(cell);
            if (old != null)
            {
                Kill(old.gameObject);
                instances.Remove(cell);
            }

            bool spawned = map.TryGet(cell, out var data) && SpawnFromData(data);
            RefreshCell(cell);
            RefreshNeighbours(cell);
            CellChanged?.Invoke(cell);
            return spawned;
        }

        /// <summary>Знищує всі об'єкти тайлів і будує їх заново з HexMapData через палітру.</summary>
        public void RebuildFromData()
        {
            DestroyAllInstances();
            if (map == null) return;

            int missing = 0;
            // Копія: SpawnFromData може оновити варіант у map.cells, якщо тайл у палітрі змінився.
            var cells = new List<HexCellData>(map.cells);
            foreach (var c in cells)
                if (!SpawnFromData(c)) missing++;

            // Connected-тайли перевіряють сусідів: якщо збережена форма ще підходить, вона лишається.
            RefreshAllSmart();

            if (missing > 0)
                Debug.LogWarning($"[HexGrid] {missing} клітинок не показано: для їхніх типів у палітрі нема тайла.", this);
            MapRebuilt?.Invoke();
        }

        // ------------------------------------------------------------ розумні тайли

        /// <summary>Маска з'єднаних сусідів: біт e — сусід через ребро e з'єднується з цим тайлом.</summary>
        public int ConnectionMask(Vector2Int cell, SmartTile smart)
        {
            int mask = 0;
            for (int e = 0; e < 6; e++)
            {
                var n = HexMath.Neighbour(cell, e);
                bool connected = !IsInside(n) ? smart.connectOutside : smart.ConnectsTo(GetTile(n), e);
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

            RecordUndo("Update smart tile");
            if (pick.prefab == tile.prefab)
            {
                SetTurn(tile, pick.turn);
            }
            else
            {
                var smart = tile.smart;
                var type = tile.type;
                int variant = Mathf.Max(0, smart.IndexOfVariant(pick.prefab));
                Kill(tile.gameObject);
                Spawn(cell, type, pick.prefab, pick.turn, variant, smart);
                map.Set(cell, type, pick.turn, variant);
            }
            CellChanged?.Invoke(cell);
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
            if (map == null) return 0;
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
            map.Set(tile.cell, tile.type, tile.turn, tile.variant);
        }

        // ------------------------------------------------------------ внутрішнє

        /// <summary>Ставить тайл зі збережених даних (без перевибору, якщо варіант ще існує).</summary>
        private bool SpawnFromData(HexCellData c)
        {
            var tile = palette != null ? palette.Get(c.type) : null;
            if (tile == null) return false;

            if (tile is GameObject go)
            {
                Spawn(c.cell, c.type, go, c.turn, 0, null);
                return true;
            }
            if (tile is SmartTile smart)
            {
                var prefab = smart.VariantAt(c.variant);
                int turn = c.turn;
                if (prefab == null)
                {
                    // Тайл у палітрі змінили — збереженого варіанта вже нема, вибираємо заново.
                    if (!smart.TryPick(0, c.turn, null, 0, out var pick)) return false;
                    prefab = pick.prefab;
                    turn = pick.turn;
                    map.Set(c.cell, c.type, turn, Mathf.Max(0, smart.IndexOfVariant(prefab)));
                }
                Spawn(c.cell, c.type, prefab, turn, Mathf.Max(0, smart.IndexOfVariant(prefab)), smart);
                return true;
            }
            return false;
        }

        private void DestroyAllInstances()
        {
            var root = TilesRoot;
            for (int i = root.childCount - 1; i >= 0; i--)
                Kill(root.GetChild(i).gameObject);
            instances.Clear();
            cachedChildCount = root.childCount;
            cacheValid = true;
        }

        private HexCell Spawn(Vector2Int cell, HexCellType type, GameObject prefab, int turn, int variant, SmartTile smart)
        {
            GameObject go = null;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                go = PrefabUtility.InstantiatePrefab(prefab, TilesRoot) as GameObject;
#endif
            if (go == null) go = Instantiate(prefab, TilesRoot);

            go.name = $"{type} ({cell.x},{cell.y})";
            go.transform.localPosition = HexMath.CellToLocal(cell, HexSize);
            go.transform.localRotation = HexMath.Turn(turn);
            go.transform.localScale = Vector3.one * HexSize;

            var hc = go.GetComponent<HexCell>();
            if (hc == null) hc = go.AddComponent<HexCell>();
            hc.cell = cell;
            hc.type = type;
            hc.prefab = prefab;
            hc.turn = HexMath.Mod6(turn);
            hc.variant = variant;
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

        private void RecordUndo(string undoName)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) Undo.RecordObject(this, undoName);
#endif
        }

        // ------------------------------------------------------------ гізмо

        private void OnDrawGizmos()
        {
            if (!drawGrid || map == null) return;
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
