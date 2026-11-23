using UnityEngine;

namespace KingdomBouns.CityMap
{
    /// <summary>
    /// Мітка на кожному поставленому тайлі: яка клітинка, з якого префаба, який поворот.
    /// Через неї рейкаст по колайдеру знаходить клітинку, а сітка — свої об'єкти.
    /// </summary>
    [DisallowMultipleComponent]
    public class HexCell : MonoBehaviour
    {
        public Vector2Int cell;
        public GameObject prefab;
        [Range(0, 5)] public int turn;
        [Tooltip("Розумний тайл, з якого вибрано префаб (null — звичайний префаб).")]
        public SmartTile smart;
    }
}
