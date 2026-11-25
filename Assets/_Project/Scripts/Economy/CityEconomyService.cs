using System;
using System.Collections.Generic;
using KingdomBouns.CityMap;
using KingdomBouns.Core;
using UnityEngine;

namespace KingdomBouns.Economy
{
    /// <summary>
    /// Економіка всіх міст гравця.
    ///
    /// Стан міст — CitiesSaveData у SaveService (ключ "cities"); вигляд сітки кожного міста — HexMapData
    /// під ключем "map_{id міста}". На старті все завантажується, і для кожного міста створюється CityEconomy,
    /// який рахує житло, місця, вихід, бонуси. Параметри будівель — у CityEconomyConfig (SO).
    ///
    /// NextTurn: гроші (вихід − утримання) і наука міст → EmpireResourcesService, будівництво з переливом,
    /// ріст населення, перерахунок. Завершена будівля міняє гекс Construction на свій.
    /// Якщо карта міста зараз показана через HexMapService, змінений гекс оновлюється одразу.
    /// </summary>
    public sealed class CityEconomyService : IGameService, IDisposable
    {
        public const string SAVE_KEY = "cities";
        public const string MAP_KEY_PREFIX = "map_";

        public static string MapKey(string cityId) => MAP_KEY_PREFIX + cityId;

        /// <summary>Місто перераховано (після ходу або зміни черги).</summary>
        public event Action<CityEconomy> onCityRecalculated;
        /// <summary>Будівлю добудовано.</summary>
        public event Action<CityEconomy, ConstructionData> onConstructionCompleted;
        /// <summary>Населення змінилось (ріст або голод).</summary>
        public event Action<CityEconomy, CityEconomy.GrowthResult> onPopulationChanged;
        /// <summary>Хід усіх міст оброблено.</summary>
        public event Action onTurnEnded;

        public CityEconomyConfig Config => _config;
        public IReadOnlyList<CityEconomy> Cities => _cities;

        private readonly CityEconomyConfig _config;
        private readonly List<CityEconomy> _cities = new List<CityEconomy>();
        private CitiesSaveData _data = new CitiesSaveData();
        private SaveService _save;
        private EmpireResourcesService _empire;
        private HexMapService _hexMap;

        public CityEconomyService(CityEconomyConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            _config = config;
        }

        public void Bind(ServicesProvider services)
        {
            _save = services.GetService<SaveService>();
            _empire = services.GetService<EmpireResourcesService>();
            services.TryGetService(out _hexMap); // необов'язковий: на сцені без карти економіка теж працює
        }

        public void OnLaunchGame()
        {
            Load();
            _save.onSaveRequested += Save;
        }

        // ------------------------------------------------------------ доступ

        public CityEconomy GetCity(string cityId)
        {
            foreach (var c in _cities)
                if (c.Id == cityId) return c;
            return null;
        }

        /// <summary>Вигляд сітки міста — його передає в HexMapService.BindMap завантажувач.</summary>
        public HexMapData GetCityMap(string cityId) => GetCity(cityId)?.Map;

        // ------------------------------------------------------------ черга будівництва

        /// <summary>
        /// Додає будівлю в кінець черги міста й ставить на клітинку гекс будівництва.
        /// Перевіряється лише клітинка: у межах сітки (якщо карта є), не зайнята будівлею чи іншим будівництвом.
        /// </summary>
        public bool AddConstructionToQueue(string cityId, Vector2Int cell, BuildingType type)
        {
            var city = GetCity(cityId);
            if (city == null)
            {
                Debug.LogWarning($"[CityEconomy] Нема міста '{cityId}'.");
                return false;
            }
            if (type == BuildingType.None || _config.Get(type) == null)
            {
                Debug.LogWarning($"[CityEconomy] Нема параметрів будівлі {type} у CityEconomyConfig.");
                return false;
            }
            if (city.Map != null && !city.Map.IsInside(cell))
                return false;
            if (city.Data.HasBuildingAt(cell) || city.Data.HasConstructionAt(cell))
                return false;

            city.Data.constructionQueue.Add(new ConstructionData { cell = cell, type = type });
            SetCityHex(city, cell, HexCellType.Construction);
            RecalculateCity(city);
            return true;
        }

        // ------------------------------------------------------------ хід

        public void NextTurn()
        {
            foreach (var city in _cities)
            {
                // Імперські ресурси — за поточним розрахунком міста.
                _empire.Add(EmpireResource.Gold, city.GoldNet);
                _empire.Add(EmpireResource.Science, city.Output.science);

                foreach (var done in city.ProcessConstruction())
                {
                    var def = _config.Get(done.type);
                    SetCityHex(city, done.cell, def != null ? def.hex : HexCellType.None);
                    onConstructionCompleted?.Invoke(city, done);
                }

                var growth = city.ProcessGrowth();
                if (growth != CityEconomy.GrowthResult.None)
                    onPopulationChanged?.Invoke(city, growth);

                RecalculateCity(city);
            }
            onTurnEnded?.Invoke();
        }

        // ------------------------------------------------------------ збереження

        public void Load()
        {
            _data = _save.LoadOrCreate<CitiesSaveData>(SAVE_KEY);
            if (_data.cities == null) _data.cities = new List<CityData>();

            _cities.Clear();
            foreach (var cityData in _data.cities)
            {
                if (!_save.TryLoad(MapKey(cityData.id), out HexMapData map))
                {
                    Debug.LogWarning($"[CityEconomy] Для міста '{cityData.id}' нема карти '{MapKey(cityData.id)}' — " +
                                     "сусідство з гексами карти не рахується, вигляд не оновлюється.");
                    map = null;
                }
                _cities.Add(new CityEconomy(cityData, _config, map));
            }
        }

        public void Save()
        {
            _save.Save(SAVE_KEY, _data);
            foreach (var city in _cities)
                if (city.Map != null)
                    _save.Save(MapKey(city.Id), city.Map);
        }

        public void Dispose()
        {
            if (_save != null)
                _save.onSaveRequested -= Save;
            onCityRecalculated = null;
            onConstructionCompleted = null;
            onPopulationChanged = null;
            onTurnEnded = null;
        }

        // ------------------------------------------------------------ внутрішнє

        private void RecalculateCity(CityEconomy city)
        {
            city.Recalculate();
            onCityRecalculated?.Invoke(city);
        }

        /// <summary>
        /// Міняє тип гекса в карті міста. Варіант −1 — «вибрати при показі»: сітка сама візьме
        /// випадковий варіант/поворот розумного тайла і запише їх у карту.
        /// </summary>
        private void SetCityHex(CityEconomy city, Vector2Int cell, HexCellType hex)
        {
            var map = city.Map;
            if (map == null || hex == HexCellType.None)
                return;

            map.Set(cell, hex, 0, -1);
            if (_hexMap != null && _hexMap.Map == map)
                _hexMap.UpdateCell(cell);
        }
    }
}
