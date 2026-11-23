using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KingdomBouns.CityMap.EditorTools
{
    /// <summary>
    /// Редактор розумного тайла: режим, групи з'єднань, варіанти з вагами й превʼю,
    /// форми з клікабельним гексом ребер, автозаповнення за назвами Carved і перевірка покриття форм.
    /// </summary>
    [CustomEditor(typeof(SmartTile))]
    public class SmartTileEditor : Editor
    {
        private static readonly string[] ModeLabels = { "Випадковий", "За сусідами" };
        private const float Thumb = 44f;

        private SerializedProperty groupsProp;

        private void OnEnable()
        {
            groupsProp = serializedObject.FindProperty("groups");
        }

        public override void OnInspectorGUI()
        {
            var tile = (SmartTile)target;
            serializedObject.Update();
            EditorGUILayout.PropertyField(groupsProp, new GUIContent("Групи з'єднань"), true);
            serializedObject.ApplyModifiedProperties();

            // Решта полів малюється вручну; RecordObject до змін дає звичайний Undo.
            Undo.RecordObject(tile, "Edit SmartTile");
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.Space(4);
            tile.mode = (SmartTile.Mode)GUILayout.Toolbar((int)tile.mode, ModeLabels, GUILayout.Height(24));
            EditorGUILayout.Space(4);

            if (tile.mode == SmartTile.Mode.Random) DrawRandom(tile);
            else DrawConnected(tile);

            if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(tile);
            if (AssetPreview.IsLoadingAssetPreviews()) Repaint();
        }

        // ------------------------------------------------------------ Random

        private void DrawRandom(SmartTile tile)
        {
            EditorGUILayout.HelpBox("При встановленні береться випадковий варіант (з урахуванням ваги)" +
                                    (tile.randomRotation ? " і випадковий поворот." : "; поворот — з редактора."),
                MessageType.None);
            tile.randomRotation = EditorGUILayout.Toggle("Випадковий поворот", tile.randomRotation);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField($"Варіанти ({tile.variants.Count})", EditorStyles.boldLabel);
            DrawVariantList(tile.variants);
            DrawAddButtons(added => SmartTileTools.FillRandom(tile, added), tile);
        }

        // ------------------------------------------------------------ Connected

        private void DrawConnected(SmartTile tile)
        {
            EditorGUILayout.HelpBox(
                "Форма вибирається за сусідами зі спільною групою з'єднань. Кожна форма — це ребра, які вона з'єднує " +
                "без повороту (ребро 0 праворуч = +X, далі проти годинникової стрілки). Клікай по ребрах гекса, щоб їх змінити. " +
                "Якщо точної форми нема, береться найближча.",
                MessageType.None);
            tile.connectOutside = EditorGUILayout.Toggle(new GUIContent("З'єднуватись за межею",
                "Клітинки поза радіусом міста вважаються з'єднаними — дорога «виходить» з міста."), tile.connectOutside);

            DrawCoverage(tile);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField($"Форми ({tile.shapes.Count})", EditorStyles.boldLabel);

            int removeShape = -1;
            for (int i = 0; i < tile.shapes.Count; i++)
            {
                var shape = tile.shapes[i];
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var r = GUILayoutUtility.GetRect(72, 72, GUILayout.Width(72), GUILayout.Height(72));
                        shape.edges = SmartTileTools.EdgeMaskField(r, shape.edges);

                        using (new EditorGUILayout.VerticalScope())
                        {
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                shape.name = EditorGUILayout.TextField(shape.name);
                                if (GUILayout.Button("✕", GUILayout.Width(22))) removeShape = i;
                            }
                            EditorGUILayout.LabelField($"Ребра: {SmartTile.MaskToString(shape.edges)}", EditorStyles.miniLabel);
                            DrawVariantList(shape.variants);
                            DrawAddButtons(added =>
                            {
                                foreach (var p in added)
                                    if (!shape.variants.Exists(v => v.prefab == p))
                                        shape.variants.Add(new SmartTile.Variant { prefab = p, weight = 1f });
                            }, tile, compact: true);
                        }
                    }
                }
            }
            if (removeShape >= 0) { tile.shapes.RemoveAt(removeShape); GUI.changed = true; }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ Форма"))
                {
                    tile.shapes.Add(new SmartTile.Shape { name = "shape", edges = 1 });
                    GUI.changed = true;
                }
                var sel = SmartTileTools.SelectedPrefabs();
                using (new EditorGUI.DisabledScope(sel.Count == 0))
                {
                    if (GUILayout.Button(new GUIContent($"Автозаповнення з виділених ({sel.Count})",
                            "Форма за назвою префаба: _end/_source, _straight, _bend, _sharp, _fork, _cross (як у Carved Kingdoms). " +
                            "Варіанти з однаковою назвою (road_straight_1, _2) потрапляють в одну форму.")))
                    {
                        var unknown = SmartTileTools.FillConnected(tile, sel);
                        if (tile.groups.Count == 0 && sel.Count > 0) tile.groups.Add(SmartTileTools.GuessGroup(sel[0].name));
                        if (unknown.Count > 0)
                            EditorUtility.DisplayDialog("Автозаповнення",
                                "Не вдалося визначити форму для:\n" + string.Join("\n", unknown.ConvertAll(u => u.name)) +
                                "\n\nДодай їх вручну через «+ Форма».", "OK");
                        GUI.changed = true;
                    }
                }
            }
        }

        private static void DrawCoverage(SmartTile tile)
        {
            var missing = new List<int>();
            int covered = tile.ExactCoverage(missing);
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField($"Точних форм: {covered} з 14 конфігурацій сусідів",
                covered == 14 ? EditorStyles.boldLabel : EditorStyles.label);
            if (missing.Count == 0) return;

            EditorGUILayout.LabelField("Без точної форми (буде найближча):", EditorStyles.miniLabel);
            const float s = 34f;
            int perRow = Mathf.Max(1, Mathf.FloorToInt((EditorGUIUtility.currentViewWidth - 40f) / (s + 4f)));
            for (int i = 0; i < missing.Count; i += perRow)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int j = i; j < Mathf.Min(i + perRow, missing.Count); j++)
                    {
                        var r = GUILayoutUtility.GetRect(s, s, GUILayout.Width(s), GUILayout.Height(s));
                        SmartTileTools.EdgeMaskField(r, missing[j], interactive: false, showNumbers: false);
                        GUI.Label(r, new GUIContent("", SmartTile.MaskToString(missing[j])));
                    }
                }
            }
        }

        // ------------------------------------------------------------ варіанти

        private static void DrawVariantList(List<SmartTile.Variant> list)
        {
            int remove = -1;
            for (int i = 0; i < list.Count; i++)
            {
                var v = list[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    var tex = v.prefab != null ? SmartTileTools.Preview(v.prefab, out _) : null;
                    var r = GUILayoutUtility.GetRect(Thumb, Thumb, GUILayout.Width(Thumb), GUILayout.Height(Thumb));
                    if (tex != null) GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit);
                    else GUI.Box(r, GUIContent.none);

                    using (new EditorGUILayout.VerticalScope())
                    {
                        v.prefab = (GameObject)EditorGUILayout.ObjectField(v.prefab, typeof(GameObject), false);
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField("Вага", GUILayout.Width(36));
                            v.weight = Mathf.Max(0f, EditorGUILayout.FloatField(v.weight, GUILayout.Width(50)));
                            GUILayout.FlexibleSpace();
                            if (GUILayout.Button("✕", GUILayout.Width(22))) remove = i;
                        }
                    }
                }
            }
            if (remove >= 0) { list.RemoveAt(remove); GUI.changed = true; }
        }

        /// <summary>Кнопка «додати виділені» + зона перетягування префабів.</summary>
        private static void DrawAddButtons(System.Action<List<GameObject>> add, SmartTile tile, bool compact = false)
        {
            var sel = SmartTileTools.SelectedPrefabs();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(sel.Count == 0))
                    if (GUILayout.Button($"+ Виділені префаби ({sel.Count})", compact ? EditorStyles.miniButton : GUI.skin.button))
                    {
                        add(sel);
                        GUI.changed = true;
                    }
            }

            var drop = GUILayoutUtility.GetRect(0, compact ? 20 : 34, GUILayout.ExpandWidth(true));
            GUI.Box(drop, "Перетягни префаби сюди", EditorStyles.helpBox);
            var e = Event.current;
            if ((e.type == EventType.DragUpdated || e.type == EventType.DragPerform) && drop.Contains(e.mousePosition))
            {
                var dragged = new List<GameObject>();
                foreach (var o in DragAndDrop.objectReferences)
                    if (o is GameObject go && AssetDatabase.Contains(go)) dragged.Add(go);
                DragAndDrop.visualMode = dragged.Count > 0 ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                if (e.type == EventType.DragPerform && dragged.Count > 0)
                {
                    DragAndDrop.AcceptDrag();
                    add(dragged);
                    GUI.changed = true;
                }
                e.Use();
            }
        }
    }
}
