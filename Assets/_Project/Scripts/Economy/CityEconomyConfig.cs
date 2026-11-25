using System;
using System.Collections.Generic;
using KingdomBouns.CityMap;
using UnityEngine;

namespace KingdomBouns.Economy
{
    /// <summary>На що діє бонус сусідства.</summary>
    public enum BonusTarget
    {
        Food = 0,
        Production = 1,
        Gold = 2,
        Glory = 3,
        Science = 4,
        Housing = 5,
    }

    /// <summary>
    /// Бонус за сусідів. Сусід рахується, якщо на сусідній клітинці стоїть будівля зі списку
    /// (або будь-яка, якщо anyBuilding), або гекс карти зі списку (Ліс тощо).
    /// Бонус = amount × (кількість сусідів / perNeighbors, вниз); onlyOnce — amount, якщо є хоч один сусід.
    /// </summary>
    [Serializable]
    public class AdjacencyBonus
    {
        public BonusTarget target;
        public float amount = 1f;
        public List<BuildingType> neighborBuildings = new List<BuildingType>();
        public List<HexCellType> neighborHexes = new List<HexCellType>();
        [Tooltip("Рахувати будь-яку збудовану будівлю.")]
        public bool anyBuilding;
        [Min(1)] public int perNeighbors = 1;
        [Tooltip("Дати amount один раз, якщо є хоча б один такий сусід.")]
        public bool onlyOnce;
    }

    /// <summary>
    /// Конверсія: частина однієї галузі додатково йде в іншу.
    /// perWorker — за кожного працівника галузі from (Гільдія: 0,25 гроші за працівника Промисловості);
    /// shareOfOutput — частка виходу галузі from (Лабораторія: 10% промисловості в науку).
    /// Вихід галузі from не зменшується.
    /// </summary>
    [Serializable]
    public class ConversionRule
    {
        public CitySector from;
        public CitySector to;
        public float perWorker;
        [Range(0f, 1f)] public float shareOfOutput;
    }

    /// <summary>Параметри однієї будівлі (усе — для однієї копії).</summary>
    [Serializable]
    public class BuildingDefinition
    {
        public BuildingType type;
        [Tooltip("Як будівля виглядає на сітці міста.")]
        public HexCellType hex;
        [Tooltip("Базова ціна в промисловості (кожна наступна копія дорожча на costStepPerCopy).")]
        [Min(0)] public int cost;
        [Tooltip("Утримання, гроші за хід.")]
        [Min(0)] public int upkeep;
        [Tooltip("Місця для життя.")]
        [Min(0)] public int housing;
        [Tooltip("Робочі місця по галузях.")]
        public SectorValues jobs;
        [Tooltip("Плоский вихід за хід по галузях.")]
        public SectorValues flat;
        [Tooltip("Ефективність галузей: 0,15 = +15% до виходу працівників.")]
        public SectorValues efficiency;
        public List<AdjacencyBonus> adjacency = new List<AdjacencyBonus>();
        public List<ConversionRule> conversions = new List<ConversionRule>();
    }

    /// <summary>
    /// Параметри економіки міст: загальні правила (вихід працівника, їжа, ріст, житло) і таблиця будівель.
    /// Значення за замовчуванням — з GDD v2 «Нові Землі» (розділи «Населення», «Будівлі»).
    /// </summary>
    [CreateAssetMenu(fileName = "CityEconomyConfig", menuName = "KingdomBouns/City Economy Config")]
    public class CityEconomyConfig : ScriptableObject
    {
        [Header("Галузі")]
        [Tooltip("Вихід одного працівника без бонусів.")]
        public SectorValues yieldPerWorker = new SectorValues(6, 3, 3, 3, 3);

        [Header("Їжа і ріст")]
        [Min(0f)] public float foodPerCitizen = 2f;
        [Tooltip("Поріг нового громадянина = growthBase + growthStep × (населення − 1).")]
        [Min(1f)] public float growthBase = 8f;
        [Min(0f)] public float growthStep = 3f;
        [Tooltip("За скільки громадян до ліміту житла ріст сповільнюється.")]
        [Min(0)] public int slowGrowthMargin = 2;
        [Range(0f, 1f)] public float slowGrowthFactor = 0.5f;
        [Tooltip("Після голодної смерті запас їжі відновлюється до цієї частки порогу.")]
        [Range(0f, 1f)] public float starvationRestore = 0.25f;

        [Header("Будівництво")]
        [Tooltip("Кожна копія, що вже стоїть у місті, робить нову дорожчою на цю частку (0,25 = +25%).")]
        [Min(0f)] public float costStepPerCopy = 0.25f;

        [Header("Будівлі")]
        public List<BuildingDefinition> buildings = CreateGddBuildings();

        [NonSerialized] private Dictionary<BuildingType, BuildingDefinition> _lookup;
        [NonSerialized] private int _lookupSourceCount = -1;

        public BuildingDefinition Get(BuildingType type)
        {
            if (_lookup == null || _lookupSourceCount != buildings.Count) RebuildLookup();
            return _lookup.TryGetValue(type, out var d) ? d : null;
        }

        /// <summary>Ціна наступної копії: база × (1 + costStepPerCopy × копій, що вже стоять).</summary>
        public float CostOf(BuildingType type, int alreadyBuilt)
        {
            var d = Get(type);
            return d == null ? 0f : d.cost * (1f + costStepPerCopy * Mathf.Max(0, alreadyBuilt));
        }

        /// <summary>Поріг їжі для наступного громадянина.</summary>
        public float GrowthThreshold(int population) => growthBase + growthStep * Mathf.Max(0, population - 1);

        private void OnValidate() => _lookup = null;

        private void RebuildLookup()
        {
            _lookup = new Dictionary<BuildingType, BuildingDefinition>();
            foreach (var b in buildings)
                if (b != null && b.type != BuildingType.None && !_lookup.ContainsKey(b.type))
                    _lookup.Add(b.type, b);
            _lookupSourceCount = buildings.Count;
        }

        [ContextMenu("Скинути будівлі до значень GDD")]
        private void ResetBuildingsToGdd()
        {
            buildings = CreateGddBuildings();
            _lookup = null;
        }

        // ------------------------------------------------------------ значення з GDD

        private static List<BuildingDefinition> CreateGddBuildings()
        {
            return new List<BuildingDefinition>
            {
                new BuildingDefinition
                {
                    type = BuildingType.TownHall, hex = HexCellType.TownHall, cost = 0, upkeep = 0, housing = 4,
                    jobs = new SectorValues(3, 2, 1, 2, 1),
                    flat = new SectorValues(3, 3, 2, 2, 2),
                },
                new BuildingDefinition
                {
                    type = BuildingType.House, hex = HexCellType.House, cost = 20, upkeep = 0, housing = 3,
                    adjacency = { Bonus(BonusTarget.Housing, 1, once: true, buildings: new[] { BuildingType.Market, BuildingType.Monument }) },
                },
                new BuildingDefinition
                {
                    type = BuildingType.Farm, hex = HexCellType.Farm, cost = 20, upkeep = 0,
                    jobs = new SectorValues(2, 0, 0, 0, 0),
                    flat = new SectorValues(1, 0, 0, 0, 0),
                    adjacency = { Bonus(BonusTarget.Food, 1, buildings: new[] { BuildingType.Farm }) },
                },
                new BuildingDefinition
                {
                    type = BuildingType.Mill, hex = HexCellType.Mill, cost = 45, upkeep = 1,
                    efficiency = new SectorValues(0.15f, 0, 0, 0, 0),
                },
                new BuildingDefinition
                {
                    type = BuildingType.Workshop, hex = HexCellType.Workshop, cost = 25, upkeep = 1,
                    jobs = new SectorValues(0, 2, 0, 0, 0),
                    flat = new SectorValues(0, 1, 0, 0, 0),
                },
                new BuildingDefinition
                {
                    type = BuildingType.Guild, hex = HexCellType.Guild, cost = 60, upkeep = 2,
                    conversions = { new ConversionRule { from = CitySector.Production, to = CitySector.Gold, perWorker = 0.25f } },
                },
                new BuildingDefinition
                {
                    type = BuildingType.Market, hex = HexCellType.Market, cost = 25, upkeep = 0,
                    jobs = new SectorValues(0, 0, 2, 0, 0),
                    flat = new SectorValues(0, 0, 1, 0, 0),
                    adjacency = { Bonus(BonusTarget.Gold, 1, perNeighbors: 2, anyBuilding: true) },
                },
                new BuildingDefinition
                {
                    type = BuildingType.Bank, hex = HexCellType.Bank, cost = 90, upkeep = 0,
                    efficiency = new SectorValues(0, 0, 0.15f, 0, 0),
                    adjacency = { Bonus(BonusTarget.Gold, 2, once: true, buildings: new[] { BuildingType.Market }) },
                },
                new BuildingDefinition
                {
                    type = BuildingType.Monument, hex = HexCellType.Monument, cost = 25, upkeep = 1,
                    jobs = new SectorValues(0, 0, 0, 2, 0),
                    flat = new SectorValues(0, 0, 0, 2, 0),
                },
                new BuildingDefinition
                {
                    type = BuildingType.Arena, hex = HexCellType.Arena, cost = 105, upkeep = 3,
                    efficiency = new SectorValues(0, 0, 0, 0.15f, 0),
                },
                new BuildingDefinition
                {
                    type = BuildingType.Library, hex = HexCellType.Library, cost = 30, upkeep = 1,
                    jobs = new SectorValues(0, 0, 0, 0, 2),
                    flat = new SectorValues(0, 0, 0, 0, 1),
                    adjacency = { Bonus(BonusTarget.Science, 1, hexes: new[] { HexCellType.Forest }) },
                },
                new BuildingDefinition
                {
                    type = BuildingType.University, hex = HexCellType.University, cost = 110, upkeep = 3,
                    efficiency = new SectorValues(0, 0, 0, 0, 0.15f),
                    adjacency = { Bonus(BonusTarget.Science, 2, once: true, buildings: new[] { BuildingType.Library }) },
                    conversions = { new ConversionRule { from = CitySector.Science, to = CitySector.Glory, perWorker = 0.25f } },
                },
                new BuildingDefinition
                {
                    type = BuildingType.Laboratory, hex = HexCellType.Laboratory, cost = 190, upkeep = 4,
                    conversions = { new ConversionRule { from = CitySector.Production, to = CitySector.Science, shareOfOutput = 0.1f } },
                },
            };
        }

        private static AdjacencyBonus Bonus(BonusTarget target, float amount, int perNeighbors = 1, bool once = false,
            bool anyBuilding = false, BuildingType[] buildings = null, HexCellType[] hexes = null)
        {
            var b = new AdjacencyBonus { target = target, amount = amount, perNeighbors = perNeighbors, onlyOnce = once, anyBuilding = anyBuilding };
            if (buildings != null) b.neighborBuildings.AddRange(buildings);
            if (hexes != null) b.neighborHexes.AddRange(hexes);
            return b;
        }
    }
}
