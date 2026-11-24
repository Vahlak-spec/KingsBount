using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace KingdomBouns.CityMap
{
    /// <summary>
    /// Палітра: для кожного HexCellType — тайл, яким він малюється (префаб або SmartTile).
    /// Карта (HexMapData) зберігає лише типи, тож одну й ту саму карту можна показати різними палітрами
    /// (наприклад, місто людей і місто гномів).
    /// </summary>
    [CreateAssetMenu(fileName = "HexTilePalette", menuName = "KingdomBouns/Hex Tile Palette")]
    public class HexTilePalette : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public HexCellType type;
            [Tooltip("Префаб (GameObject) або розумний тайл (SmartTile).")]
            public UnityEngine.Object tile;
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();

        [Tooltip("Старий список без типів (до переходу на enum). Розклади елементи по типах і очисти.")]
        [FormerlySerializedAs("items")]
        [SerializeField] private List<UnityEngine.Object> legacyItems = new List<UnityEngine.Object>();

        public IReadOnlyList<Entry> Entries => entries;
        public List<UnityEngine.Object> LegacyItems => legacyItems;

        [NonSerialized] private Dictionary<HexCellType, UnityEngine.Object> _lookup;

        /// <summary>Тайл для типу (null, якщо не призначено).</summary>
        public UnityEngine.Object Get(HexCellType type)
        {
            if (_lookup == null || _lookup.Count != entries.Count) RebuildLookup();
            return _lookup.TryGetValue(type, out var t) ? t : null;
        }

        public bool Has(HexCellType type) => Get(type) != null;

        public void Set(HexCellType type, UnityEngine.Object tile)
        {
            if (type == HexCellType.None) return;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].type == type)
                {
                    entries[i] = new Entry { type = type, tile = tile };
                    _lookup = null;
                    return;
                }
            entries.Add(new Entry { type = type, tile = tile });
            _lookup = null;
        }

        /// <summary>Тип, якому призначено цей тайл (None, якщо такого нема).</summary>
        public HexCellType TypeOf(UnityEngine.Object tile)
        {
            if (tile == null) return HexCellType.None;
            foreach (var e in entries) if (e.tile == tile) return e.type;
            return HexCellType.None;
        }

        /// <summary>Додає в entries рядки для всіх типів enum, яких ще нема (щоб у інспекторі було поле під кожен тип).</summary>
        public bool EnsureAllTypes()
        {
            bool changed = false;
            foreach (HexCellType t in Enum.GetValues(typeof(HexCellType)))
            {
                if (t == HexCellType.None) continue;
                bool found = false;
                foreach (var e in entries) if (e.type == t) { found = true; break; }
                if (!found) { entries.Add(new Entry { type = t }); changed = true; }
            }
            if (changed)
            {
                entries.Sort((a, b) => ((int)a.type).CompareTo((int)b.type));
                _lookup = null;
            }
            return changed;
        }

        private void OnValidate() => _lookup = null;

        private void RebuildLookup()
        {
            _lookup = new Dictionary<HexCellType, UnityEngine.Object>();
            foreach (var e in entries)
                if (e.type != HexCellType.None && e.tile != null) _lookup[e.type] = e.tile;
            // Порожні рядки теж рахуються в entries, тож вирівнюємо лічильник, щоб не перебудовувати щоразу.
            foreach (var e in entries)
                if (!_lookup.ContainsKey(e.type)) _lookup[e.type] = null;
        }

        /// <summary>Чи можна призначити об'єкт тайлом.</summary>
        public static bool IsPlaceable(UnityEngine.Object o) => o is GameObject || o is SmartTile;

        /// <summary>Префаб для превʼю (для SmartTile — його перший варіант).</summary>
        public static GameObject PreviewOf(UnityEngine.Object o)
        {
            if (o is GameObject go) return go;
            if (o is SmartTile st) return st.PreviewPrefab;
            return null;
        }
    }
}
