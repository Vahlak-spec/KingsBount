using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomBouns.CityMap
{
    /// <summary>
    /// СТАРИЙ ФОРМАТ (до HexMapData + HexCellType). Лишився тільки для імпорту в Hex Map Editor.
    /// Дані карти міста: розмір сітки і що стоїть на кожній клітинці (префаб + поворот).
    /// Сцена будується з цих даних (HexGrid.RebuildFromData), тож карту можна завантажити і в рантаймі.
    /// </summary>
    [CreateAssetMenu(fileName = "CityMapData", menuName = "KingdomBouns/City Map Data")]
    public class CityMapData : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public Vector2Int cell;
            public GameObject prefab;
            [Range(0, 5)] public int turn;
            [Tooltip("Розумний тайл, з якого вибрано префаб (null — звичайний префаб).")]
            public SmartTile smart;
        }

        [Tooltip("Радіус гекса міста. R=4 → 61 клітинка.")]
        [Min(0)] public int radius = 4;

        [Tooltip("Відстань від центру клітинки до кута. Тайли Carved Kingdoms = 1.")]
        [Min(0.01f)] public float hexSize = 1f;

        [SerializeField] private List<Entry> entries = new List<Entry>();

        public IReadOnlyList<Entry> Entries => entries;

        public bool IsInside(Vector2Int cell) => HexMath.Length(cell) <= radius;

        public bool TryGet(Vector2Int cell, out Entry entry)
        {
            int i = IndexOf(cell);
            entry = i >= 0 ? entries[i] : default;
            return i >= 0;
        }

        public void Set(Vector2Int cell, GameObject prefab, int turn, SmartTile smart = null)
        {
            var e = new Entry { cell = cell, prefab = prefab, turn = HexMath.Mod6(turn), smart = smart };
            int i = IndexOf(cell);
            if (i >= 0) entries[i] = e;
            else entries.Add(e);
        }

        public bool Remove(Vector2Int cell)
        {
            int i = IndexOf(cell);
            if (i < 0) return false;
            entries.RemoveAt(i);
            return true;
        }

        public void Clear() => entries.Clear();

        private int IndexOf(Vector2Int cell)
        {
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].cell == cell) return i;
            return -1;
        }
    }
}
