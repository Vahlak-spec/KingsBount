using UnityEditor;
using UnityEngine;

namespace KingdomBouns.CityMap.EditorTools
{
    [CustomEditor(typeof(CityCameraController))]
    public class CityCameraControllerEditor : Editor
    {
        private float margin = 1f;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var c = (CityCameraController)target;

            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox(
                "Помаранчевий прямокутник у Scene view — межі (кути можна тягнути), блакитний — що бачить камера зараз.\n" +
                "Обертання камери скрипт не змінює: нахил і поворот задаються в Transform.",
                MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                var grid = FindAnyObjectByType<HexGrid>();
                using (new EditorGUI.DisabledScope(grid == null || grid.Data == null))
                {
                    if (GUILayout.Button(new GUIContent("Межі за HexGrid", "Прямокутник навколо гекса міста з відступом")))
                    {
                        Undo.RecordObject(c, "Fit camera bounds");
                        c.FitBoundsToGrid(grid, margin);
                        EditorUtility.SetDirty(c);
                        SceneView.RepaintAll();
                    }
                }
                margin = EditorGUILayout.FloatField(margin, GUILayout.Width(50));
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Нахил 45°", "Rotation X = 45, Y і Z без змін")))
                {
                    Undo.RecordObject(c.transform, "Camera pitch 45");
                    var e = c.transform.eulerAngles;
                    c.transform.rotation = Quaternion.Euler(45f, e.y, e.z);
                }
                if (GUILayout.Button(new GUIContent("Вписати камеру в межі", "Посунути камеру й обмежити розмір так, як це буде в грі")))
                {
                    var cam = c.GetComponent<Camera>();
                    Undo.RecordObjects(new Object[] { c.transform, cam }, "Clamp camera");
                    cam.orthographicSize = Mathf.Clamp(cam.orthographicSize, c.minSize, MaxFit(c));
                    c.Clamp();
                }
            }
        }

        private static float MaxFit(CityCameraController c)
        {
            // EffectiveMaxSize читає Cam, який у Edit mode ще не ініціалізований — Clamp() його підхопить.
            c.Clamp();
            return c.EffectiveMaxSize();
        }

        private void OnSceneGUI()
        {
            var c = (CityCameraController)target;
            float y = c.groundY;
            var min = new Vector3(c.minX, y, c.minZ);
            var max = new Vector3(c.maxX, y, c.maxZ);

            Handles.color = new Color(1f, 0.6f, 0.1f);
            float size = HandleUtility.GetHandleSize(min) * 0.08f;

            EditorGUI.BeginChangeCheck();
            var newMin = Handles.FreeMoveHandle(min, size, Vector3.zero, Handles.DotHandleCap);
            var newMax = Handles.FreeMoveHandle(max, HandleUtility.GetHandleSize(max) * 0.08f, Vector3.zero, Handles.DotHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(c, "Move camera bounds");
                // FreeMoveHandle рухає в площині екрана — переносимо на площину землі.
                if (newMin != min) newMin = ProjectToGround(newMin, y); else newMin = min;
                if (newMax != max) newMax = ProjectToGround(newMax, y); else newMax = max;
                c.minX = Mathf.Min(newMin.x, c.maxX); c.minZ = Mathf.Min(newMin.z, c.maxZ);
                c.maxX = Mathf.Max(newMax.x, c.minX); c.maxZ = Mathf.Max(newMax.z, c.minZ);
                EditorUtility.SetDirty(c);
            }
            Handles.Label(min, $"  min ({c.minX:0.#}, {c.minZ:0.#})");
            Handles.Label(max, $"  max ({c.maxX:0.#}, {c.maxZ:0.#})");
        }

        private static Vector3 ProjectToGround(Vector3 handlePos, float y)
        {
            var ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
            var plane = new Plane(Vector3.up, new Vector3(0f, y, 0f));
            return plane.Raycast(ray, out float d) ? ray.GetPoint(d) : new Vector3(handlePos.x, y, handlePos.z);
        }
    }
}
