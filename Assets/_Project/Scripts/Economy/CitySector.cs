using System;
using UnityEngine;

namespace KingdomBouns.Economy
{
    /// <summary>П'ять галузей міста (повзунки з GDD).</summary>
    public enum CitySector
    {
        Food = 0,
        Production = 1,
        Gold = 2,
        Glory = 3,
        Science = 4,
    }

    /// <summary>Значення по п'яти галузях (вихід, місця, ефективність, плоскі бонуси).</summary>
    [Serializable]
    public struct SectorValues
    {
        public const int COUNT = 5;

        public float food;
        public float production;
        public float gold;
        public float glory;
        public float science;

        public SectorValues(float food, float production, float gold, float glory, float science)
        {
            this.food = food;
            this.production = production;
            this.gold = gold;
            this.glory = glory;
            this.science = science;
        }

        public float this[CitySector sector]
        {
            get
            {
                switch (sector)
                {
                    case CitySector.Food: return food;
                    case CitySector.Production: return production;
                    case CitySector.Gold: return gold;
                    case CitySector.Glory: return glory;
                    default: return science;
                }
            }
            set
            {
                switch (sector)
                {
                    case CitySector.Food: food = value; break;
                    case CitySector.Production: production = value; break;
                    case CitySector.Gold: gold = value; break;
                    case CitySector.Glory: glory = value; break;
                    default: science = value; break;
                }
            }
        }

        public static SectorValues operator +(SectorValues a, SectorValues b) =>
            new SectorValues(a.food + b.food, a.production + b.production, a.gold + b.gold, a.glory + b.glory, a.science + b.science);

        public override string ToString() =>
            $"їжа {food:0.##}, пром {production:0.##}, гроші {gold:0.##}, слава {glory:0.##}, наука {science:0.##}";
    }

    /// <summary>Скільки громадян працює в кожній галузі (значення повзунків).</summary>
    [Serializable]
    public struct SectorWorkers
    {
        [Min(0)] public int food;
        [Min(0)] public int production;
        [Min(0)] public int gold;
        [Min(0)] public int glory;
        [Min(0)] public int science;

        public int Total => food + production + gold + glory + science;

        public int this[CitySector sector]
        {
            get
            {
                switch (sector)
                {
                    case CitySector.Food: return food;
                    case CitySector.Production: return production;
                    case CitySector.Gold: return gold;
                    case CitySector.Glory: return glory;
                    default: return science;
                }
            }
            set
            {
                switch (sector)
                {
                    case CitySector.Food: food = value; break;
                    case CitySector.Production: production = value; break;
                    case CitySector.Gold: gold = value; break;
                    case CitySector.Glory: glory = value; break;
                    default: science = value; break;
                }
            }
        }

        public override string ToString() =>
            $"їжа {food}, пром {production}, гроші {gold}, слава {glory}, наука {science}";
    }
}
