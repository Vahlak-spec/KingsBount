using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomBouns.CityMap
{
    /// <summary>
    /// Розумний тайл: сам вибирає префаб і поворот під час встановлення.
    ///
    /// Random    — випадковий варіант (з вагами) і, за бажанням, випадковий поворот (луки, поля, посіви).
    /// Connected — форма визначається сусідами (дороги, річки): кожна форма має маску ребер, які вона з'єднує,
    ///             у неповерненому стані (ребро 0 = +X). Тайл шукає форму й поворот, що збігаються з сусідами,
    ///             а якщо точної нема — найближчу (мінімум відмінних ребер, далі — мінімум «обірваних» сусідів).
    ///
    /// Сусід рахується з'єднаним, якщо на ньому стоїть розумний тайл зі спільною групою з'єднань
    /// (або той самий SmartTile, якщо груп нема), або префаб із компонентом HexConnector зі спільною групою.
    /// </summary>
    [CreateAssetMenu(fileName = "SmartTile", menuName = "KingdomBouns/Smart Tile")]
    public class SmartTile : ScriptableObject
    {
        public enum Mode { Random, Connected }

        [Serializable]
        public class Variant
        {
            public GameObject prefab;
            [Min(0f)] public float weight = 1f;
        }

        [Serializable]
        public class Shape
        {
            public string name;
            [Tooltip("Біти 0..5 — ребра, які з'єднує форма без повороту.")]
            [Range(0, 63)] public int edges;
            public List<Variant> variants = new List<Variant>();
        }

        public struct Pick
        {
            public GameObject prefab;
            public int turn;
            /// <summary>Форма точно збігається з сусідами.</summary>
            public bool exact;
        }

        public Mode mode = Mode.Random;

        [Tooltip("Групи з'єднань. Тайли зі спільною групою з'єднуються (дорога ↔ міст ↔ ворота).")]
        public List<string> groups = new List<string>();

        [Header("Random")]
        public List<Variant> variants = new List<Variant>();
        [Tooltip("Випадковий поворот при встановленні. Вимкнено — береться поворот з редактора.")]
        public bool randomRotation = true;

        [Header("Connected")]
        public List<Shape> shapes = new List<Shape>();
        [Tooltip("Клітинки за межею міста вважаються з'єднаними (дорога «виходить» з міста).")]
        public bool connectOutside;

        private static readonly System.Random Rng = new System.Random();

        public bool IsConnected => mode == Mode.Connected;

        /// <summary>Префаб для превʼю в палітрі.</summary>
        public GameObject PreviewPrefab
        {
            get
            {
                if (mode == Mode.Random) return FirstPrefab(variants);
                foreach (var s in shapes)
                {
                    var p = FirstPrefab(s.variants);
                    if (p != null) return p;
                }
                return null;
            }
        }

        public bool ConnectsTo(SmartTile other)
        {
            if (other == null) return false;
            if (other == this) return true;
            return HexConnector.GroupsIntersect(groups, other.groups);
        }

        /// <summary>
        /// Чи з'єднується тайл із сусідом, що стоїть за ребром edgeToNeighbour.
        /// Сусід — розумний тайл зі спільною групою (з будь-якого боку)
        /// або префаб із HexConnector зі спільною групою, у якого відкрите ребро в наш бік.
        /// </summary>
        public bool ConnectsTo(HexCell neighbour, int edgeToNeighbour)
        {
            if (neighbour == null) return false;
            if (neighbour.smart != null && ConnectsTo(neighbour.smart)) return true;
            var conn = neighbour.GetComponent<HexConnector>();
            return conn != null
                   && HexConnector.GroupsIntersect(groups, conn.groups)
                   && conn.IsOpen(edgeToNeighbour + 3, neighbour.turn);
        }

        /// <summary>
        /// Вибирає префаб і поворот.
        /// mask — з'єднані сусіди (біт e = сусід через ребро e); для Random не використовується.
        /// manualTurn — поворот з редактора (для Random без випадкового повороту).
        /// currentPrefab/currentTurn — що вже стоїть на клітинці: якщо воно не гірше за найкращий варіант,
        /// лишаємо його, щоб сусідні оновлення не «перекидали кубик».
        /// </summary>
        public bool TryPick(int mask, int manualTurn, GameObject currentPrefab, int currentTurn, out Pick pick)
        {
            pick = default;
            if (mode == Mode.Random)
            {
                var p = PickVariant(variants);
                if (p == null) return false;
                pick = new Pick { prefab = p, turn = randomRotation ? Rng.Next(6) : HexMath.Mod6(manualTurn), exact = true };
                return true;
            }
            return TryPickConnected(mask & 63, currentPrefab, currentTurn, out pick);
        }

        private bool TryPickConnected(int mask, GameObject currentPrefab, int currentTurn, out Pick pick)
        {
            pick = default;
            int bestDist = int.MaxValue, bestMissing = int.MaxValue;
            var cands = new List<Vector2Int>(); // x = shape, y = turn

            for (int s = 0; s < shapes.Count; s++)
            {
                if (FirstPrefab(shapes[s].variants) == null) continue;
                for (int k = 0; k < 6; k++)
                {
                    int r = Rotate(shapes[s].edges, k);
                    int missing = BitCount(mask & ~r & 63);
                    int extra = BitCount(r & ~mask & 63);
                    int dist = missing + extra;
                    if (dist < bestDist || (dist == bestDist && missing < bestMissing))
                    {
                        bestDist = dist;
                        bestMissing = missing;
                        cands.Clear();
                    }
                    if (dist == bestDist && missing == bestMissing) cands.Add(new Vector2Int(s, k));
                }
            }
            if (cands.Count == 0) return false;

            // Те, що вже стоїть, лишаємо, якщо воно серед найкращих.
            if (currentPrefab != null)
            {
                foreach (var c in cands)
                    if (c.y == HexMath.Mod6(currentTurn) && Contains(shapes[c.x].variants, currentPrefab))
                    {
                        pick = new Pick { prefab = currentPrefab, turn = c.y, exact = bestDist == 0 };
                        return true;
                    }
            }

            // Перша за списком форма серед найкращих, випадковий з її рівноцінних поворотів.
            int shape = int.MaxValue;
            foreach (var c in cands) shape = Mathf.Min(shape, c.x);
            var turns = new List<int>();
            foreach (var c in cands) if (c.x == shape) turns.Add(c.y);

            pick = new Pick
            {
                prefab = PickVariant(shapes[shape].variants),
                turn = turns[Rng.Next(turns.Count)],
                exact = bestDist == 0,
            };
            return pick.prefab != null;
        }

        // ------------------------------------------------------------ утиліти масок

        /// <summary>Поворот маски: ребро e переходить у ребро e + k.</summary>
        public static int Rotate(int mask, int k)
        {
            k = HexMath.Mod6(k);
            mask &= 63;
            return ((mask << k) | (mask >> (6 - k))) & 63;
        }

        public static int BitCount(int m)
        {
            int n = 0;
            for (m &= 63; m != 0; m &= m - 1) n++;
            return n;
        }

        /// <summary>Канонічна форма маски (мінімум серед усіх поворотів) — 14 різних конфігурацій сусідів.</summary>
        public static int Canonical(int mask)
        {
            int best = mask & 63;
            for (int k = 1; k < 6; k++) best = Mathf.Min(best, Rotate(mask, k));
            return best;
        }

        public static string MaskToString(int mask)
        {
            if ((mask & 63) == 0) return "—";
            var parts = new List<string>();
            for (int e = 0; e < 6; e++) if ((mask & (1 << e)) != 0) parts.Add(e.ToString());
            return string.Join("-", parts);
        }

        /// <summary>Скільки з 14 конфігурацій сусідів мають точну форму; missing — ті, що не мають.</summary>
        public int ExactCoverage(List<int> missing = null)
        {
            var have = new HashSet<int>();
            foreach (var s in shapes)
                if (FirstPrefab(s.variants) != null) have.Add(Canonical(s.edges));
            var all = new HashSet<int>();
            for (int m = 0; m < 64; m++) all.Add(Canonical(m));
            int n = 0;
            foreach (var c in all)
            {
                if (have.Contains(c)) n++;
                else missing?.Add(c);
            }
            return n;
        }

        public static GameObject PickVariant(List<Variant> list)
        {
            if (list == null) return null;
            float total = 0f;
            foreach (var v in list) if (v != null && v.prefab != null) total += Mathf.Max(0f, v.weight);
            if (total <= 0f) return FirstPrefab(list);
            double roll = Rng.NextDouble() * total;
            foreach (var v in list)
            {
                if (v == null || v.prefab == null) continue;
                roll -= Mathf.Max(0f, v.weight);
                if (roll <= 0) return v.prefab;
            }
            return FirstPrefab(list);
        }

        private static GameObject FirstPrefab(List<Variant> list)
        {
            if (list == null) return null;
            foreach (var v in list) if (v != null && v.prefab != null) return v.prefab;
            return null;
        }

        private static bool Contains(List<Variant> list, GameObject prefab)
        {
            if (list == null) return false;
            foreach (var v in list) if (v != null && v.prefab == prefab) return true;
            return false;
        }
    }
}
