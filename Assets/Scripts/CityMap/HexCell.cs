using UnityEngine;

namespace KingdomBouns.CityMap
{
    /// <summary>
    /// Мітка на кожному поставленому тайлі: клітинка, тип, з якого префаба/розумного тайла, поворот і варіант.
    /// Через неї рейкаст по колайдеру знаходить клітинку, а сітка — свої об'єкти.
    /// </summary>
    [DisallowMultipleComponent]
    public class HexCell : MonoBehaviour
    {
        public Vector2Int cell;
        public HexCellType type;
        public GameObject prefab;
        [Range(0, 5)] public int turn;
        [Tooltip("Індекс варіанта в розумному тайлі (0 для звичайного префаба).")]
        public int variant;
        [Tooltip("Розумний тайл, з якого вибрано префаб (null — звичайний префаб).")]
        public SmartTile smart;
    }
}
