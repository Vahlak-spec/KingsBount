using UnityEditor;
using UnityEngine;

namespace KingdomBouns.CityMap.EditorTools
{
    [CustomEditor(typeof(HexGrid))]
    public class HexGridEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var grid = (HexGrid)target;

            EditorGUILayout.Space(8);
            if (grid.Data != null)
                EditorGUILayout.LabelField($"Радіус {grid.Radius}: клітинок {HexMath.CountInRadius(grid.Radius)}, тайлів {grid.Data.Entries.Count}",
                    EditorStyles.miniLabel);

            if (GUILayout.Button("Відкрити Hex Map Editor", GUILayout.Height(26)))
                HexMapEditorWindow.OpenFor(grid);

            using (new EditorGUI.DisabledScope(grid.Data == null || EditorApplication.isPlaying))
            {
                if (GUILayout.Button("Перебудувати з даних"))
                {
                    Undo.IncrementCurrentGroup();
                    int g = Undo.GetCurrentGroup();
                    Undo.SetCurrentGroupName("Rebuild hex map");
                    grid.RebuildFromData();
                    Undo.CollapseUndoOperations(g);
                }
            }
        }
    }
}
