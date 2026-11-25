using System;
using KingdomBouns.CityMap;
using KingdomBouns.Core;
using UnityEngine;

namespace KingdomBouns
{
    /// <summary>
    /// Вигляд гексової карти на сцені. Залежність — HexGrid зі сцени (передається через конструктор у біндері).
    ///
    /// Карту передає інший сервіс (завантажувач), який вирішує, яке місто чи велику карту відкрити:
    ///   _hexMap = services.GetService&lt;HexMapService&gt;();
    ///   _hexMap.BindMap(loadedMap);
    ///
    /// Сервіс працює з тим самим екземпляром HexMapData: усі зміни через Place/Remove/Rotate одразу
    /// потрапляють у нього, тож завантажувач може просто зберегти той самий об'єкт.
    /// </summary>
    public sealed class HexMapService : IGameService, IDisposable
    {
        /// <summary>Карту прив'язано й побудовано.</summary>
        public event Action<HexMapData> onMapBound;
        /// <summary>Карту відв'язано (тайли прибрано).</summary>
        public event Action onMapUnbound;
        /// <summary>Вміст клітинки змінився.</summary>
        public event Action<Vector2Int> onCellChanged;

        public HexGrid Grid => _grid;
        public HexMapData Map { get; private set; }
        public bool IsBound => Map != null;

        private readonly HexGrid _grid;
        private readonly bool _clearSceneTilesOnLaunch;

        /// <param name="grid">HexGrid зі сцени.</param>
        /// <param name="clearSceneTilesOnLaunch">
        /// Прибрати на старті тайли, що лишилися в сцені з редактора, — до BindMap на карті нічого не видно.
        /// </param>
        public HexMapService(HexGrid grid, bool clearSceneTilesOnLaunch = true)
        {
            if (grid == null)
                throw new ArgumentNullException(nameof(grid));

            _grid = grid;
            _clearSceneTilesOnLaunch = clearSceneTilesOnLaunch;
        }

        public void Bind(ServicesProvider services) { }

        public void OnLaunchGame()
        {
            _grid.CellChanged += OnGridCellChanged;
            if (_clearSceneTilesOnLaunch && !IsBound)
                _grid.ClearVisuals();
        }

        /// <summary>Показати карту: тайли будуються з даних через палітру сітки.</summary>
        public void BindMap(HexMapData map)
        {
            if (map == null)
                throw new ArgumentNullException(nameof(map));

            Map = map;
            _grid.SetMap(map);
            onMapBound?.Invoke(map);
        }

        /// <summary>Показати карту з JSON (наприклад, стартову карту-TextAsset). Повертає прив'язані дані.</summary>
        public HexMapData BindMapJson(string json)
        {
            var map = HexMapData.FromJson(json);
            if (map == null)
            {
                Debug.LogError("[HexMap] Не вдалося прочитати карту з JSON.");
                return null;
            }
            BindMap(map);
            return map;
        }

        /// <summary>Прибрати карту зі сцени (дані не змінюються).</summary>
        public void UnbindMap()
        {
            if (!IsBound)
                return;

            _grid.ClearVisuals();
            _grid.SetMap(new HexMapData(), rebuild: false);
            Map = null;
            onMapUnbound?.Invoke();
        }

        /// <summary>Змінити палітру (інший набір тайлів для тих самих типів) і перебудувати вигляд.</summary>
        public void SetPalette(HexTilePalette palette)
        {
            _grid.Palette = palette;
            if (IsBound)
                _grid.RebuildFromData();
        }

        // --- зміни карти

        public bool Place(Vector2Int cell, HexCellType type, int turn = 0) => IsBound && _grid.Place(cell, type, turn);

        public bool Remove(Vector2Int cell) => IsBound && _grid.Remove(cell);

        public bool Rotate(Vector2Int cell, int steps = 1) => IsBound && _grid.Rotate(cell, steps);

        /// <summary>
        /// Перебудувати вигляд однієї клітинки з даних карти — коли дані змінив інший сервіс
        /// (наприклад, економіка поставила будівництво). Інші клітинки не перевіряються.
        /// </summary>
        public bool UpdateCell(Vector2Int cell) => IsBound && _grid.RebuildCell(cell);

        // --- запити

        public HexCellType GetCellType(Vector2Int cell) => IsBound ? Map.GetCellType(cell) : HexCellType.None;

        public bool TryGetCell(Vector2Int cell, out HexCellData data)
        {
            data = default;
            return IsBound && Map.TryGet(cell, out data);
        }

        public bool IsInside(Vector2Int cell) => IsBound && Map.IsInside(cell);

        public Vector3 CellToWorld(Vector2Int cell) => _grid.CellToWorld(cell);

        public Vector2Int WorldToCell(Vector3 world) => _grid.WorldToCell(world);

        /// <summary>Клітинка під екранною точкою (наприклад, з InputService.onTap).</summary>
        public bool RaycastScreen(Camera camera, Vector2 screenPos, out HexHit hit)
        {
            hit = default;
            return IsBound && camera != null && _grid.Raycast(camera.ScreenPointToRay(screenPos), out hit);
        }

        public void Dispose()
        {
            if (_grid != null)
                _grid.CellChanged -= OnGridCellChanged;
            onMapBound = null;
            onMapUnbound = null;
            onCellChanged = null;
        }

        private void OnGridCellChanged(Vector2Int cell) => onCellChanged?.Invoke(cell);
    }
}
