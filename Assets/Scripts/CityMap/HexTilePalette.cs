using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace KingdomBouns.CityMap
{
    /// <summary>
    /// Набір того, що можна ставити: префаби (GameObject) і розумні тайли (SmartTile) в одному списку.
    /// </summary>
    [CreateAssetMenu(fileName = "HexTilePalette", menuName = "KingdomBouns/Hex Tile Palette")]
    public class HexTilePalette : ScriptableObject
    {
        [FormerlySerializedAs("prefabs")]
        public List<Object> items = new List<Object>();

        public int Count => items != null ? items.Count : 0;

        public Object Get(int index)
        {
            if (Count == 0) return null;
            return items[Mathf.Clamp(index, 0, items.Count - 1)];
        }

        /// <summary>Чи можна покласти об'єкт у палітру.</summary>
        public static bool IsPlaceable(Object o) => o is GameObject || o is SmartTile;

        /// <summary>Префаб для превʼю (для SmartTile — його перший варіант).</summary>
        public static GameObject PreviewOf(Object o)
        {
            if (o is GameObject go) return go;
            if (o is SmartTile st) return st.PreviewPrefab;
            return null;
        }
    }
}
