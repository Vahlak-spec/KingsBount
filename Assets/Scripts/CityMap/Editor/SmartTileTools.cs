using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace KingdomBouns.CityMap.EditorTools
{
    /// <summary>Спільні утиліти редактора: автозаповнення розумних тайлів, превʼю, пошук асетів, віджет ребер.</summary>
    public static class SmartTileTools
    {
        // Форми Carved Kingdoms (див. HexBoard.cs): ребра без повороту.
        private static readonly KeyValuePair<string, int>[] Patterns =
        {
            new KeyValuePair<string, int>("cross", (1 << 0) | (1 << 1) | (1 << 3) | (1 << 4)),
            new KeyValuePair<string, int>("fork", (1 << 0) | (1 << 2) | (1 << 4)),
            new KeyValuePair<string, int>("sharp", (1 << 0) | (1 << 1)),
            new KeyValuePair<string, int>("bend", (1 << 0) | (1 << 2)),
            new KeyValuePair<string, int>("straight", (1 << 0) | (1 << 3)),
            new KeyValuePair<string, int>("end", 1 << 0),
            new KeyValuePair<string, int>("source", 1 << 0),
        };

        private static readonly Regex TrailingNumber = new Regex(@"[\s_\-]*\(?\d+\)?$");

        /// <summary>Назва без номера варіанта: road_straight_2 → road_straight.</summary>
        public static string BaseName(string name) => TrailingNumber.Replace(name, "");

        /// <summary>Визначає ребра форми за назвою префаба (road_bend_1 → 0-2).</summary>
        public static bool TryGuessEdges(string name, out int mask)
        {
            mask = 0;
            var tokens = name.ToLowerInvariant().Split('_', ' ', '-');
            foreach (var p in Patterns)
                foreach (var t in tokens)
                    if (t == p.Key) { mask = p.Value; return true; }
            return false;
        }

        /// <summary>Група з'єднань за назвою: road_bend_1 → road.</summary>
        public static string GuessGroup(string name)
        {
            var tokens = name.ToLowerInvariant().Split('_', ' ', '-');
            return tokens.Length > 0 ? tokens[0] : name;
        }

        /// <summary>Додає префаби у форми Connected-тайла (варіанти з однаковою назвою — в одну форму).</summary>
        /// <returns>Префаби, для яких форму не вдалося визначити.</returns>
        public static List<GameObject> FillConnected(SmartTile tile, IEnumerable<GameObject> prefabs)
        {
            var unknown = new List<GameObject>();
            foreach (var p in prefabs)
            {
                if (p == null) continue;
                if (!TryGuessEdges(p.name, out int mask)) { unknown.Add(p); continue; }
                string shapeName = BaseName(p.name);
                var shape = tile.shapes.Find(s => s.name == shapeName);
                if (shape == null)
                {
                    shape = new SmartTile.Shape { name = shapeName, edges = mask };
                    tile.shapes.Add(shape);
                }
                if (!shape.variants.Exists(v => v.prefab == p))
                    shape.variants.Add(new SmartTile.Variant { prefab = p, weight = 1f });
            }
            // Від простих форм до складних — так легше читати.
            tile.shapes.Sort((a, b) => SmartTile.BitCount(a.edges).CompareTo(SmartTile.BitCount(b.edges)));
            return unknown;
        }

        public static void FillRandom(SmartTile tile, IEnumerable<GameObject> prefabs)
        {
            foreach (var p in prefabs)
                if (p != null && !tile.variants.Exists(v => v.prefab == p))
                    tile.variants.Add(new SmartTile.Variant { prefab = p, weight = 1f });
        }

        /// <summary>
        /// Створює SmartTile з префабів. Якщо назви схожі на дороги/річки Carved — режим Connected
        /// з групою за префіксом назви, інакше — Random.
        /// </summary>
        public static SmartTile CreateFromPrefabs(List<GameObject> prefabs, string path)
        {
            var tile = ScriptableObject.CreateInstance<SmartTile>();
            int known = 0;
            foreach (var p in prefabs) if (p != null && TryGuessEdges(p.name, out _)) known++;

            if (known > 0 && known * 2 >= prefabs.Count)
            {
                tile.mode = SmartTile.Mode.Connected;
                tile.groups.Add(GuessGroup(prefabs[0].name));
                var unknown = FillConnected(tile, prefabs);
                if (unknown.Count > 0)
                    Debug.LogWarning($"[SmartTile] Не вдалося визначити форму для: {string.Join(", ", unknown.ConvertAll(u => u.name))}");
            }
            else
            {
                tile.mode = SmartTile.Mode.Random;
                FillRandom(tile, prefabs);
            }

            AssetDatabase.CreateAsset(tile, path);
            AssetDatabase.SaveAssets();
            return tile;
        }

        /// <summary>Префаби-асети, виділені в Project.</summary>
        public static List<GameObject> SelectedPrefabs()
        {
            var list = new List<GameObject>();
            foreach (var o in Selection.objects)
                if (o is GameObject go && AssetDatabase.Contains(go)) list.Add(go);
            list.Sort((a, b) => EditorUtility.NaturalCompare(a.name, b.name));
            return list;
        }

        /// <summary>Префаби й розумні тайли з папки (з підпапками).</summary>
        public static List<Object> FindPlaceables(string folder)
        {
            var list = new List<Object>();
            foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
                if (go != null) list.Add(go);
            }
            foreach (var g in AssetDatabase.FindAssets("t:" + nameof(SmartTile), new[] { folder }))
            {
                var st = AssetDatabase.LoadAssetAtPath<SmartTile>(AssetDatabase.GUIDToAssetPath(g));
                if (st != null) list.Add(st);
            }
            list.Sort((a, b) =>
            {
                // Розумні тайли — першими.
                int ka = a is SmartTile ? 0 : 1, kb = b is SmartTile ? 0 : 1;
                return ka != kb ? ka.CompareTo(kb) : EditorUtility.NaturalCompare(a.name, b.name);
            });
            return list;
        }

        /// <summary>Абсолютний шлях → шлях проєкту (Assets/...). null, якщо папка поза Assets.</summary>
        public static string ToProjectPath(string absolute)
        {
            if (string.IsNullOrEmpty(absolute)) return null;
            absolute = absolute.Replace('\\', '/');
            var dataPath = Application.dataPath.Replace('\\', '/');
            if (absolute == dataPath) return "Assets";
            if (absolute.StartsWith(dataPath + "/")) return "Assets" + absolute.Substring(dataPath.Length);
            return null;
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        /// <summary>Превʼю для префаба або розумного тайла. loading = превʼю ще генерується.</summary>
        public static Texture Preview(Object o, out bool loading)
        {
            loading = false;
            var prefab = HexTilePalette.PreviewOf(o);
            if (prefab == null) return o != null ? AssetPreview.GetMiniThumbnail(o) : null;
            Texture tex = AssetPreview.GetAssetPreview(prefab);
            if (tex == null)
            {
                loading = AssetPreview.IsLoadingAssetPreviews();
                tex = AssetPreview.GetMiniThumbnail(prefab);
            }
            return tex;
        }

        // ------------------------------------------------------------ віджет ребер

        private static Vector3 HexPoint(Vector2 c, float radius, float deg)
        {
            float a = deg * Mathf.Deg2Rad;
            // У GUI вісь Y дивиться вниз, тому -sin: гекс виглядає як у Scene view зверху (+Z угорі).
            return new Vector3(c.x + Mathf.Cos(a) * radius, c.y - Mathf.Sin(a) * radius, 0f);
        }

        /// <summary>
        /// Гекс із 6 ребрами: увімкнені ребра підсвічені й з'єднані з центром «дорогою».
        /// Якщо interactive — клік по ребру перемикає його. Ребро 0 дивиться праворуч (+X).
        /// </summary>
        public static int EdgeMaskField(Rect rect, int mask, bool interactive = true, bool showNumbers = true)
        {
            var e = Event.current;
            var c = rect.center;
            float radius = Mathf.Min(rect.width, rect.height) * 0.42f;

            if (interactive && e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                var v = e.mousePosition - c;
                float ang = Mathf.Atan2(-v.y, v.x) * Mathf.Rad2Deg;
                int edge = HexMath.Mod6(Mathf.RoundToInt(ang / 60f));
                mask ^= 1 << edge;
                GUI.changed = true;
                e.Use();
            }

            if (e.type != EventType.Repaint) return mask;

            var corners = new Vector3[6];
            for (int i = 0; i < 6; i++) corners[i] = HexPoint(c, radius, 30f + 60f * i);

            Handles.color = new Color(0.16f, 0.16f, 0.16f, 1f);
            Handles.DrawAAConvexPolygon(corners);

            for (int edge = 0; edge < 6; edge++)
            {
                bool on = (mask & (1 << edge)) != 0;
                var a = corners[(edge + 5) % 6];
                var b = corners[edge];
                Handles.color = on ? new Color(1f, 0.8f, 0.3f) : new Color(0.45f, 0.45f, 0.45f);
                Handles.DrawAAPolyLine(on ? 4f : 2f, a, b);
                if (on)
                {
                    var mid = HexPoint(c, radius * 0.866f, 60f * edge);
                    Handles.DrawAAPolyLine(Mathf.Max(3f, radius * 0.22f), new Vector3(c.x, c.y, 0f), mid);
                }
                if (showNumbers && radius >= 22f)
                {
                    var p = HexPoint(c, radius * 0.6f, 60f * edge);
                    var style = EditorStyles.centeredGreyMiniLabel;
                    GUI.Label(new Rect(p.x - 8, p.y - 8, 16, 16), edge.ToString(), style);
                }
            }
            if (mask != 0)
            {
                Handles.color = new Color(1f, 0.8f, 0.3f);
                Handles.DrawSolidDisc(new Vector3(c.x, c.y, 0f), Vector3.forward, Mathf.Max(2f, radius * 0.11f));
            }
            return mask;
        }
    }
}
