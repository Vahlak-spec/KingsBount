namespace KingdomBouns.Economy
{
    /// <summary>
    /// Тип будівлі міста. Як будівля виглядає на сітці (HexCellType), скільки коштує й що дає — у CityEconomyConfig.
    ///
    /// ВАЖЛИВО: зберігається в сейвах числом, тому значення задані явно.
    /// Нові будівлі додавай у кінець, наявні числа не міняй і не перевикористовуй.
    /// </summary>
    public enum BuildingType
    {
        None = 0,
        TownHall = 1,
        House = 2,
        Farm = 3,
        Mill = 4,
        Workshop = 5,
        Guild = 6,
        Market = 7,
        Bank = 8,
        Monument = 9,
        Arena = 10,
        Library = 11,
        University = 12,
        Laboratory = 13,
    }
}
