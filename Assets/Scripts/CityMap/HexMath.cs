using System.Collections.Generic;
using UnityEngine;

namespace KingdomBouns.CityMap
{
    /// <summary>
    /// Математика гексової сітки. Ті самі домовленості, що й у тайлів Carved Kingdoms:
    /// гекс із гострою вершиною вгору (pointy-top), size = відстань від центру до кута
    /// (тайли Carved = 1, тобто 2 м від кута до кута), верх тайла на y = 0.
    /// Осьові координати (q, r) зберігаються у Vector2Int (x = q, y = r).
    /// Ребро 0 дивиться на +X, ребра нумеруються проти годинникової стрілки, якщо дивитися згори.
    /// </summary>
    public static class HexMath
    {
        public static readonly float Sqrt3 = Mathf.Sqrt(3f);

        /// <summary>Напрямки на сусідів; Dirs[d] — сусід через ребро d.</summary>
        public static readonly Vector2Int[] Dirs =
        {
            new Vector2Int(1, 0), new Vector2Int(1, -1), new Vector2Int(0, -1),
            new Vector2Int(-1, 0), new Vector2Int(-1, 1), new Vector2Int(0, 1),
        };

        public static int Mod6(int k) => ((k % 6) + 6) % 6;

        /// <summary>Центр клітинки в локальному просторі сітки.</summary>
        public static Vector3 CellToLocal(Vector2Int cell, float size)
        {
            return new Vector3(Sqrt3 * size * (cell.x + cell.y * 0.5f), 0f, -1.5f * size * cell.y);
        }

        /// <summary>Клітинка під точкою в локальному просторі сітки (y ігнорується).</summary>
        public static Vector2Int LocalToCell(Vector3 p, float size)
        {
            float r = -p.z / (1.5f * size);
            float q = p.x / (Sqrt3 * size) - r * 0.5f;
            return Round(q, r);
        }

        /// <summary>Округлення дробових осьових координат через кубічні.</summary>
        public static Vector2Int Round(float q, float r)
        {
            float x = q, z = r, y = -x - z;
            float rx = Mathf.Round(x), ry = Mathf.Round(y), rz = Mathf.Round(z);
            float dx = Mathf.Abs(rx - x), dy = Mathf.Abs(ry - y), dz = Mathf.Abs(rz - z);
            if (dx > dy && dx > dz) rx = -ry - rz;
            else if (dy <= dz) rz = -rx - ry;
            return new Vector2Int((int)rx, (int)rz);
        }

        public static Vector2Int Neighbour(Vector2Int cell, int edge) => cell + Dirs[Mod6(edge)];

        public static int Distance(Vector2Int a, Vector2Int b)
        {
            var d = a - b;
            return (Mathf.Abs(d.x) + Mathf.Abs(d.y) + Mathf.Abs(d.x + d.y)) / 2;
        }

        /// <summary>Відстань від центру (0,0).</summary>
        public static int Length(Vector2Int cell) => Distance(cell, Vector2Int.zero);

        /// <summary>Кількість клітинок у гексі радіуса radius: 1 + 3R(R+1).</summary>
        public static int CountInRadius(int radius) => radius < 0 ? 0 : 1 + 3 * radius * (radius + 1);

        /// <summary>Усі клітинки в радіусі від центру (0,0).</summary>
        public static IEnumerable<Vector2Int> CellsInRadius(int radius)
        {
            for (int q = -radius; q <= radius; q++)
            {
                int rMin = Mathf.Max(-radius, -q - radius);
                int rMax = Mathf.Min(radius, -q + radius);
                for (int r = rMin; r <= rMax; r++)
                    yield return new Vector2Int(q, r);
            }
        }

        /// <summary>Поворот на k кроків по 60°: ребро 0 переходить на місце ребра k.</summary>
        public static Quaternion Turn(int k) => Quaternion.AngleAxis(-60f * Mod6(k), Vector3.up);

        /// <summary>Кут i (0..5) гекса в локальному просторі; кут 1 — верхня вершина (+Z).</summary>
        public static Vector3 Corner(int i, float size)
        {
            float a = Mathf.Deg2Rad * (30f + 60f * i);
            return new Vector3(Mathf.Cos(a) * size, 0f, Mathf.Sin(a) * size);
        }

        /// <summary>Середина ребра edge (0..5) у локальному просторі, відносно центру клітинки.</summary>
        public static Vector3 EdgeMid(int edge, float size)
        {
            float a = Mathf.Deg2Rad * (60f * Mod6(edge));
            float apothem = size * Sqrt3 * 0.5f;
            return new Vector3(Mathf.Cos(a) * apothem, 0f, Mathf.Sin(a) * apothem);
        }
    }
}
