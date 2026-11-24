using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomBouns.CityMap
{
    /// <summary>Одна клітинка карти.</summary>
    [Serializable]
    public struct HexCellData
    {
        /// <summary>Осьові координати (x = q, y = r).</summary>
        public Vector2Int cell;
        public HexCellType type;
        /// <summary>Поворот 0..5 (кроки по 60°).</summary>
        public int turn;
        /// <summary>
        /// Індекс варіанта префаба в розумному тайлі (для Random — у списку варіантів,
        /// для Connected — наскрізний по всіх формах). Для звичайного префаба — 0.
        /// </summary>
        public int variant;
    }

    /// <summary>
    /// Дані гексової карти (місто або велика карта). Звичайний клас, не ScriptableObject:
    /// серіалізується JsonUtility, тож його можна покласти в TextAsset, прочитати з диска чи зберегти через SaveService.
    /// Префабів тут нема — лише HexCellType; як він виглядає, вирішує палітра (HexTilePalette).
    /// </summary>
    [Serializable]
    public class HexMapData
    {
        public const int CURRENT_VERSION = 1;

        [Tooltip("Ідентифікатор карти, за яким її шукає завантажувач (наприклад, city_capital, world).")]
        public string id = "";
        public int version = CURRENT_VERSION;
        [Min(0)] public int radius = 4;
        [Min(0.01f)] public float hexSize = 1f;
        public List<HexCellData> cells = new List<HexCellData>();

        [NonSerialized] private Dictionary<Vector2Int, int> _index;

        public int Count => cells != null ? cells.Count : 0;

        public bool IsInside(Vector2Int cell) => HexMath.Length(cell) <= radius;

        public bool TryGet(Vector2Int cell, out HexCellData data)
        {
            int i = IndexOf(cell);
            data = i >= 0 ? cells[i] : default;
            return i >= 0;
        }

        public HexCellType GetCellType(Vector2Int cell) => TryGet(cell, out var d) ? d.type : HexCellType.None;

        public void Set(HexCellData data)
        {
            if (data.type == HexCellType.None) { Remove(data.cell); return; }
            data.turn = HexMath.Mod6(data.turn);
            int i = IndexOf(data.cell);
            if (i >= 0) cells[i] = data;
            else
            {
                cells.Add(data);
                if (_index != null) _index[data.cell] = cells.Count - 1;
            }
        }

        public void Set(Vector2Int cell, HexCellType type, int turn = 0, int variant = 0)
            => Set(new HexCellData { cell = cell, type = type, turn = turn, variant = variant });

        public bool Remove(Vector2Int cell)
        {
            int i = IndexOf(cell);
            if (i < 0) return false;
            cells.RemoveAt(i);
            _index = null; // індекси після i зсунулися
            return true;
        }

        public void Clear()
        {
            cells.Clear();
            _index = null;
        }

        /// <summary>Глибока копія — щоб гра змінювала свою копію, а не стартову карту.</summary>
        public HexMapData Clone()
        {
            var copy = (HexMapData)MemberwiseClone();
            copy.cells = new List<HexCellData>(cells);
            copy._index = null;
            return copy;
        }

        /// <summary>Скинути кеш пошуку (після ручної зміни списку cells або Undo в редакторі).</summary>
        public void InvalidateIndex() => _index = null;

        public string ToJson(bool pretty = true) => JsonUtility.ToJson(this, pretty);

        public static HexMapData FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            var map = JsonUtility.FromJson<HexMapData>(json);
            if (map == null) return null;
            if (map.cells == null) map.cells = new List<HexCellData>();
            if (map.id == null) map.id = "";
            return map;
        }

        private int IndexOf(Vector2Int cell)
        {
            if (cells == null) cells = new List<HexCellData>();
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (_index == null || _index.Count != cells.Count || attempt == 1) RebuildIndex();
                if (!_index.TryGetValue(cell, out int idx)) return -1;
                // Кеш міг застаріти (Undo в редакторі, ручна зміна списку) — перевіряємо й за потреби перебудовуємо.
                if (idx < cells.Count && cells[idx].cell == cell) return idx;
            }
            return -1;
        }

        private void RebuildIndex()
        {
            _index = new Dictionary<Vector2Int, int>(cells.Count);
            for (int i = 0; i < cells.Count; i++) _index[cells[i].cell] = i;
        }
    }
}
