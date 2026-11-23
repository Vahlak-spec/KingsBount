using UnityEditor;
using UnityEngine;

namespace KingdomBouns.CityMap.EditorTools
{
    [CustomEditor(typeof(HexConnector))]
    [CanEditMultipleObjects]
    public class HexConnectorEditor : Editor
    {
        private SerializedProperty groupsProp;
        private SerializedProperty edgesProp;

        private void OnEnable()
        {
            groupsProp = serializedObject.FindProperty("groups");
            edgesProp = serializedObject.FindProperty("edges");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox(
                "Розумні тайли «За сусідами» зі спільною групою з'єднуються з цим префабом — " +
                "лише з увімкнених сторін. Сторони задані без повороту (ребро 0 праворуч = +X) і повертаються разом із тайлом.",
                MessageType.None);
            EditorGUILayout.PropertyField(groupsProp, new GUIContent("Групи з'єднань"), true);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Сторони з'єднання", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                var r = GUILayoutUtility.GetRect(84, 84, GUILayout.Width(84), GUILayout.Height(84));
                EditorGUI.showMixedValue = edgesProp.hasMultipleDifferentValues;
                EditorGUI.BeginChangeCheck();
                int mask = SmartTileTools.EdgeMaskField(r, edgesProp.intValue);
                if (EditorGUI.EndChangeCheck()) edgesProp.intValue = mask;
                EditorGUI.showMixedValue = false;

                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField(edgesProp.intValue == 63 ? "З усіх боків" : SmartTile.MaskToString(edgesProp.intValue),
                        EditorStyles.miniLabel);
                    if (GUILayout.Button("Усі")) edgesProp.intValue = 63;
                    if (GUILayout.Button("Наскрізь 0-3")) edgesProp.intValue = (1 << 0) | (1 << 3);
                    if (GUILayout.Button("Жодної")) edgesProp.intValue = 0;
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
