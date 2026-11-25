using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomBouns.Economy
{
    /// <summary>
    /// Стан одного міста для збереження (SaveService, ключ "cities").
    /// Лише факти: що й де стоїть, що будується, скільки людей і де вони працюють.
    /// Усе, що з цього виводиться (житло, вихід, місця, бонуси), рахує CityEconomy.
    /// Вигляд сітки міста зберігається окремо — HexMapData під ключем "map_{id}".
    /// </summary>
    [Serializable]
    public class CityData
    {
        [Tooltip("Унікальний id міста; з нього утворюється ключ карти map_{id}.")]
        public string id = "";
        [Min(1)] public int population = 3;
        [Tooltip("Запас їжі до наступного громадянина.")]
        public float foodStock;
        [Tooltip("Розподіл громадян по галузях (повзунки).")]
        public SectorWorkers workers;
        public List<CityBuildingData> buildings = new List<CityBuildingData>();
        [Tooltip("Черга будівництва: перший елемент отримує промисловість першим.")]
        public List<ConstructionData> constructionQueue = new List<ConstructionData>();

        public bool HasBuildingAt(Vector2Int cell)
        {
            foreach (var b in buildings) if (b.cell == cell) return true;
            return false;
        }

        public bool HasConstructionAt(Vector2Int cell)
        {
            foreach (var c in constructionQueue) if (c.cell == cell) return true;
            return false;
        }

        public int CountBuildings(BuildingType type)
        {
            int n = 0;
            foreach (var b in buildings) if (b.type == type) n++;
            return n;
        }
    }

    /// <summary>Збудована будівля на клітинці сітки міста.</summary>
    [Serializable]
    public struct CityBuildingData
    {
        public Vector2Int cell;
        public BuildingType type;
    }

    /// <summary>Елемент черги будівництва: де, що, скільки промисловості вже вкладено.</summary>
    [Serializable]
    public class ConstructionData
    {
        public Vector2Int cell;
        public BuildingType type;
        public float progress;
    }

    /// <summary>Усі міста гравця — один запис у SaveService.</summary>
    [Serializable]
    public class CitiesSaveData
    {
        public List<CityData> cities = new List<CityData>();
    }
}
