using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomBouns.CityMap
{
    /// <summary>
    /// Групи з'єднань для звичайного префаба (міст, ворота, площа...).
    /// Вішається на корінь префаба тайла. Розумні тайли «За сусідами» зі спільною групою
    /// будуть тягнутися до нього, але тільки з тих сторін, що увімкнені в edges.
    /// edges задаються без повороту (ребро 0 = +X) і повертаються разом із тайлом.
    /// </summary>
    [DisallowMultipleComponent]
    public class HexConnector : MonoBehaviour
    {
        [Tooltip("Групи з'єднань (наприклад, road). Регістр не важливий.")]
        public List<string> groups = new List<string>();

        [Tooltip("Ребра, з яких можна з'єднатися (без повороту). 63 — з усіх боків.")]
        [Range(0, 63)] public int edges = 63;

        /// <summary>Чи відкрите ребро тайла, якщо дивитися у світових напрямках з урахуванням повороту.</summary>
        public bool IsOpen(int worldEdge, int turn) => (edges & (1 << HexMath.Mod6(worldEdge - turn))) != 0;

        public static bool GroupsIntersect(IList<string> a, IList<string> b)
        {
            if (a == null || b == null) return false;
            foreach (var x in a)
            {
                if (string.IsNullOrWhiteSpace(x)) continue;
                foreach (var y in b)
                    if (!string.IsNullOrWhiteSpace(y) &&
                        string.Equals(x.Trim(), y.Trim(), StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            return false;
        }
    }
}
