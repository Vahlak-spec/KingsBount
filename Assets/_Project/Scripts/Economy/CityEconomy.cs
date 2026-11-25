using System.Collections.Generic;
using KingdomBouns.CityMap;
using UnityEngine;

namespace KingdomBouns.Economy
{
    /// <summary>
    /// Розрахунки одного міста. Створюється, коли стан міста (CityData) завантажується в CityEconomyService.
    ///
    /// Recalculate() переглядає збудовані будівлі й сумує за CityEconomyConfig: житло, робочі місця, плоский вихід,
    /// бонуси сусідства, ефективність, конверсії, утримання; потім застосовує розподіл громадян (повзунки)
    /// і рахує вихід кожної галузі: працівники × вихід працівника × (1 + ефективність) + плоскі бонуси.
    ///
    /// Сусідство враховує збудовані будівлі (не майданчики) і гекси карти міста (Ліс тощо), якщо карта є.
    /// </summary>
    public sealed class CityEconomy
    {
        public enum GrowthResult { None, Grew, Starved }

        public CityData Data { get; }
        public CityEconomyConfig Config { get; }
        /// <summary>Вигляд сітки міста (може бути null — тоді сусідство з гексами карти не рахується).</summary>
        public HexMapData Map { get; private set; }

        public string Id => Data.id;
        public int Population => Data.population;
        /// <summary>Рівень міста = 1 + населення / 3 (вниз).</summary>
        public int Level => 1 + Data.population / 3;

        // ---- результати Recalculate
        /// <summary>Місця для життя.</summary>
        public int Housing { get; private set; }
        /// <summary>Робочі місця по галузях.</summary>
        public SectorValues Jobs { get; private set; }
        /// <summary>Плоский вихід (будівлі + сусідство).</summary>
        public SectorValues Flat { get; private set; }
        /// <summary>Сумарна ефективність по галузях (0,3 = +30%).</summary>
        public SectorValues Efficiency { get; private set; }
        /// <summary>Фактичні працівники: повзунки, обрізані місцями й населенням.</summary>
        public SectorWorkers Workers { get; private set; }
        /// <summary>Вихід за хід по галузях (з конверсіями).</summary>
        public SectorValues Output { get; private set; }
        /// <summary>Утримання будівель, гроші за хід.</summary>
        public float Upkeep { get; private set; }

        public int Unemployed => Mathf.Max(0, Data.population - Workers.Total);
        public float FoodConsumption => Data.population * Config.foodPerCitizen;
        public float FoodBalance => Output.food - FoodConsumption;
        /// <summary>Гроші за хід мінус утримання (іде в скарбницю імперії).</summary>
        public float GoldNet => Output.gold - Upkeep;
        public float GrowthThreshold => Config.GrowthThreshold(Data.population);

        private readonly Dictionary<Vector2Int, BuildingType> _builtAt = new Dictionary<Vector2Int, BuildingType>();
        private readonly List<ConversionRule> _conversions = new List<ConversionRule>();

        public CityEconomy(CityData data, CityEconomyConfig config, HexMapData map)
        {
            Data = data;
            Config = config;
            Map = map;
            Recalculate();
        }

        public void SetMap(HexMapData map)
        {
            Map = map;
            Recalculate();
        }

        // ------------------------------------------------------------ перерахунок

        public void Recalculate()
        {
            _builtAt.Clear();
            foreach (var b in Data.buildings) _builtAt[b.cell] = b.type;

            float housing = 0f, upkeep = 0f;
            var jobs = new SectorValues();
            var flat = new SectorValues();
            var eff = new SectorValues();
            _conversions.Clear();

            foreach (var b in Data.buildings)
            {
                var def = Config.Get(b.type);
                if (def == null)
                {
                    Debug.LogWarning($"[CityEconomy] {Data.id}: нема параметрів будівлі {b.type} у CityEconomyConfig.");
                    continue;
                }

                housing += def.housing;
                upkeep += def.upkeep;
                jobs += def.jobs;
                flat += def.flat;
                eff += def.efficiency;
                _conversions.AddRange(def.conversions);

                foreach (var adj in def.adjacency)
                {
                    float bonus = AdjacencyBonusAt(b.cell, adj);
                    if (bonus == 0f) continue;
                    if (adj.target == BonusTarget.Housing) housing += bonus;
                    else flat[(CitySector)adj.target] += bonus;
                }
            }

            Housing = Mathf.FloorToInt(housing);
            Upkeep = upkeep;
            Jobs = jobs;
            Flat = flat;
            Efficiency = eff;
            Workers = ApplyWorkers(Data.workers, jobs, Data.population);

            var output = new SectorValues();
            for (int i = 0; i < SectorValues.COUNT; i++)
            {
                var s = (CitySector)i;
                output[s] = Workers[s] * Config.yieldPerWorker[s] * (1f + eff[s]) + flat[s];
            }

            // Конверсії рахуються від виходу до конверсій, тож не ланцюжаться між собою.
            var baseOutput = output;
            foreach (var c in _conversions)
                output[c.to] += Workers[c.from] * c.perWorker + baseOutput[c.from] * c.shareOfOutput;

            Output = output;
        }

        /// <summary>
        /// Повзунки, обрізані місцями в галузі; якщо разом працює більше, ніж живе людей,
        /// зайвих знімаємо з галузей у зворотному порядку (з Науки до Їжі).
        /// </summary>
        private static SectorWorkers ApplyWorkers(SectorWorkers wanted, SectorValues jobs, int population)
        {
            var w = new SectorWorkers();
            for (int i = 0; i < SectorValues.COUNT; i++)
            {
                var s = (CitySector)i;
                w[s] = Mathf.Clamp(wanted[s], 0, Mathf.FloorToInt(jobs[s]));
            }
            for (int i = SectorValues.COUNT - 1; i >= 0 && w.Total > population; i--)
            {
                var s = (CitySector)i;
                w[s] = Mathf.Max(0, w[s] - (w.Total - population));
            }
            return w;
        }

        private float AdjacencyBonusAt(Vector2Int cell, AdjacencyBonus adj)
        {
            int count = 0;
            for (int e = 0; e < 6; e++)
            {
                var n = HexMath.Neighbour(cell, e);
                bool match = false;
                if (_builtAt.TryGetValue(n, out var nb))
                    match = adj.anyBuilding || adj.neighborBuildings.Contains(nb);
                if (!match && Map != null && adj.neighborHexes.Count > 0)
                    match = adj.neighborHexes.Contains(Map.GetCellType(n));
                if (match) count++;
            }

            if (count == 0) return 0f;
            if (adj.onlyOnce) return adj.amount;
            return adj.amount * (count / Mathf.Max(1, adj.perNeighbors));
        }

        // ------------------------------------------------------------ хід

        /// <summary>
        /// Промисловість ходу йде в перший елемент черги, надлишок — у наступний (GDD «Черга будівництва»).
        /// Залишок після кінця черги згорає. Завершені елементи переносяться в buildings.
        /// Ціна — з урахуванням копій, що вже стоять у місті на момент завершення.
        /// </summary>
        public List<ConstructionData> ProcessConstruction()
        {
            var completed = new List<ConstructionData>();
            float production = Output.production;
            var queue = Data.constructionQueue;

            while (queue.Count > 0 && production > 0f)
            {
                var item = queue[0];
                float cost = Config.CostOf(item.type, Data.CountBuildings(item.type));
                float need = Mathf.Max(0f, cost - item.progress);

                if (production >= need)
                {
                    production -= need;
                    item.progress = cost;
                    queue.RemoveAt(0);
                    Data.buildings.Add(new CityBuildingData { cell = item.cell, type = item.type });
                    completed.Add(item);
                }
                else
                {
                    item.progress += production;
                    production = 0f;
                }
            }
            return completed;
        }

        /// <summary>
        /// Їжа і ріст (GDD «Їжа, ріст і голод», «Житло»): надлишок іде в запас (біля ліміту житла — повільніше,
        /// на ліміті — ні), на порозі +1 громадянин; якщо запас пішов у мінус — −1 громадянин,
        /// запас відновлюється до частки порогу. Перед працівниками втрачаються безробітні.
        /// Ріст і голод беруть баланс їжі з поточного розрахунку (до змін цього ходу).
        /// </summary>
        public GrowthResult ProcessGrowth()
        {
            float net = FoodBalance;
            if (net > 0f)
            {
                float mult = 1f;
                if (Data.population >= Housing) mult = 0f;
                else if (Data.population >= Housing - Config.slowGrowthMargin) mult = Config.slowGrowthFactor;
                Data.foodStock += net * mult;
            }
            else
            {
                Data.foodStock += net;
            }

            if (Data.foodStock >= GrowthThreshold && Data.population < Housing)
            {
                Data.population++;
                Data.foodStock = 0f;
                return GrowthResult.Grew;
            }

            // GDD: баланс від'ємний і запас дійшов до 0 — наприкінці ходу помирає 1 громадянин.
            if (net < 0f && Data.foodStock <= 0f)
            {
                if (Data.population > 1)
                {
                    Data.population--;
                    RemoveExcessWorkers();
                }
                Data.foodStock = Config.starvationRestore * Config.GrowthThreshold(Data.population);
                return GrowthResult.Starved;
            }
            return GrowthResult.None;
        }

        /// <summary>Якщо повзунки тепер перевищують населення — знімаємо з найбільшої галузі.</summary>
        private void RemoveExcessWorkers()
        {
            var w = Data.workers;
            while (w.Total > Data.population)
            {
                var max = CitySector.Food;
                for (int i = 1; i < SectorValues.COUNT; i++)
                    if (w[(CitySector)i] > w[max]) max = (CitySector)i;
                w[max]--;
            }
            Data.workers = w;
        }
    }
}
