using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KingdomBouns.CityMap.EditorTools
{
    /// <summary>
    /// Інспектор палітри: рядок під кожен HexCellType з полем для тайла (префаб або SmartTile) і превʼю.
    /// Нові значення enum з'являються тут автоматично.
    /// </summary>
    [CustomEditor(typeof(HexTilePalette))]
    public class HexTilePaletteEditor : Editor
    {
        private const float Thumb = 40f;
        private string folder = "Assets/Prefabs/Tiles/CityMap";

        private void OnEnable()
        {
            var pal = (HexTilePalette)target;
            if (pal.EnsureAllTypes()) EditorUtility.SetDirty(pal);
        }

        public override void OnInspectorGUI()
        {
            var pal = (HexTilePalette)target;
            serializedObject.Update();
            var entries = serializedObject.FindProperty("entries");

            EditorGUILayout.HelpBox("Для кожного типу клітинки — тайл: префаб або розумний тайл (SmartTile). " +
                                    "Карта зберігає лише типи, а як вони виглядають, вирішує палітра.", MessageType.None);

            bool loading = false;
            int assigned = 0;
            for (int i = 0; i < entries.arraySize; i++)
            {
                var e = entries.GetArrayElementAtIndex(i);
                var typeProp = e.FindPropertyRelative("type");
                var tileProp = e.FindPropertyRelative("tile");
                var type = (HexCellType)typeProp.intValue;
                if (type == HexCellType.None) continue;
                if (tileProp.objectReferenceValue != null) assigned++;

                using (new EditorGUILayout.HorizontalScope())
                {
                    var r = GUILayoutUtility.GetRect(Thumb, Thumb, GUILayout.Width(Thumb), GUILayout.Height(Thumb));
                    var tex = SmartTileTools.Preview(tileProp.objectReferenceValue, out bool l);
                    loading |= l;
                    if (tex != null) GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit);
                    else GUI.Box(r, GUIContent.none);

                    using (new EditorGUILayout.VerticalScope())
                    {
                        string label = type.ToString();
                        if (tileProp.objectReferenceValue is SmartTile st) label += st.IsConnected ? "   [AUTO]" : "   [RND]";
                        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);

                        EditorGUI.BeginChangeCheck();
                        var picked = EditorGUILayout.ObjectField(tileProp.objectReferenceValue, typeof(Object), false);
                        if (EditorGUI.EndChangeCheck())
                        {
                            if (picked == null || HexTilePalette.IsPlaceable(picked)) tileProp.objectReferenceValue = picked;
                            else Debug.LogWarning($"[Palette] {picked.name}: потрібен префаб (GameObject) або SmartTile.");
                        }
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.LabelField($"Призначено: {assigned} з {System.Enum.GetValues(typeof(HexCellType)).Length - 1}",
                EditorStyles.miniLabel);

            EditorGUILayout.Space(6);
            DrawAutoFill(pal);
            DrawLegacy(pal);

            if (loading) Repaint();
        }

        private void DrawAutoFill(HexTilePalette pal)
        {
            EditorGUILayout.LabelField("Автозаповнення за назвами", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                var folderAsset = AssetDatabase.IsValidFolder(folder) ? AssetDatabase.LoadAssetAtPath<DefaultAsset>(folder) : null;
                var picked = (DefaultAsset)EditorGUILayout.ObjectField("Папка", folderAsset, typeof(DefaultAsset), false);
                if (picked != null)
                {
                    var path = AssetDatabase.GetAssetPath(picked);
                    if (AssetDatabase.IsValidFolder(path)) folder = path;
                }
            }
            using (new EditorGUI.DisabledScope(!AssetDatabase.IsValidFolder(folder)))
            {
                if (GUILayout.Button(new GUIContent("Заповнити порожні слоти з папки",
                        "Тип вгадується за назвою: tile_meadow → Meadow, road_smart → Road, TownHole → TownHall. " +
                        "Розумні тайли мають перевагу над префабами.")))
                {
                    Undo.RecordObject(pal, "Fill palette by name");
                    int n = SmartTileTools.AssignByName(pal, SmartTileTools.FindPlaceables(folder));
                    EditorUtility.SetDirty(pal);
                    Debug.Log($"[Palette] Призначено слотів: {n}");
                }
            }
        }

        private void DrawLegacy(HexTilePalette pal)
        {
            var legacy = pal.LegacyItems;
            if (legacy == null || legacy.Count == 0) return;

            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox($"Старий список палітри без типів: {legacy.Count} елементів. " +
                                    "Розклади їх по слотах (перетягни з полів нижче або натисни «Розкласти за назвами») і очисти.",
                MessageType.Info);
            using (new EditorGUI.DisabledScope(true))
                foreach (var o in legacy)
                    EditorGUILayout.ObjectField(o, typeof(Object), false);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Розкласти за назвами"))
                {
                    Undo.RecordObject(pal, "Assign legacy palette items");
                    int n = SmartTileTools.AssignByName(pal, new List<Object>(legacy));
                    EditorUtility.SetDirty(pal);
                    Debug.Log($"[Palette] Призначено слотів зі старого списку: {n}");
                }
                if (GUILayout.Button("Очистити старий список"))
                {
                    Undo.RecordObject(pal, "Clear legacy palette items");
                    legacy.Clear();
                    EditorUtility.SetDirty(pal);
                }
            }
        }
    }
}
