using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KingdomBouns.CityMap.EditorTools
{
    /// <summary>
    /// Редактор гексової карти міста (KingdomBouns → Hex Map Editor).
    /// Палітра (префаби + розумні тайли) і малювання в Scene view: рейкаст у сітку, підсвітка клітинки, дії з Undo.
    ///
    /// ЛКМ — поточна дія (можна вести мишею, щоб малювати/стирати).
    /// Shift+ЛКМ — видалити (у режимі «Повернути» — повернути на −60°).
    /// [ / ] — поворот тайла для встановлення.  Esc — вийти з режиму редагування.
    /// Alt+миша — звичайна навігація камери.
    /// </summary>
    public class HexMapEditorWindow : EditorWindow
    {
        private const string PalettePrefKey = "KingdomBouns.HexMap.PaletteGuid";
        private const string FolderPrefKey = "KingdomBouns.HexMap.PaletteFolder";
        private const string DefaultTilesFolder = "Assets/Prefabs/Tiles/CityMap";
        private const string DefaultDataFolder = "Assets/Data/CityMap";

        [SerializeField] private HexGrid grid;
        [SerializeField] private HexTilePalette palette;
        [SerializeField] private string paletteFolder;
        [SerializeField] private int selected;
        [SerializeField] private HexAction action = HexAction.Place;
        [SerializeField] private int turn;
        [SerializeField] private bool editing;
        [SerializeField] private float thumbSize = 72f;

        private Vector2 scroll;
        private bool hasHover;
        private HexHit hover;
        private Vector2Int lastAppliedCell;
        private bool lastAppliedValid;
        private int undoGroup;
        private readonly Vector3[] corners = new Vector3[6];
        private readonly Vector3[] outline = new Vector3[7];

        private static readonly string[] ActionLabels = { "Поставити", "Видалити", "Повернути" };

        [MenuItem("KingdomBouns/Hex Map Editor")]
        public static HexMapEditorWindow Open()
        {
            var w = GetWindow<HexMapEditorWindow>("Hex Map");
            w.minSize = new Vector2(260, 300);
            return w;
        }

        /// <summary>Відкрити редактор одразу з потрібною сіткою (з інспектора HexGrid).</summary>
        public static void OpenFor(HexGrid target)
        {
            var w = Open();
            w.grid = target;
            w.Repaint();
        }

        // ------------------------------------------------------------ життєвий цикл

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;

            if (palette == null)
            {
                var guid = EditorPrefs.GetString(PalettePrefKey, "");
                if (!string.IsNullOrEmpty(guid))
                    palette = AssetDatabase.LoadAssetAtPath<HexTilePalette>(AssetDatabase.GUIDToAssetPath(guid));
            }
            if (string.IsNullOrEmpty(paletteFolder))
                paletteFolder = EditorPrefs.GetString(FolderPrefKey, DefaultTilesFolder);
            if (grid == null) grid = FindAnyObjectByType<HexGrid>();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }

        private void OnHierarchyChange()
        {
            if (grid == null) grid = FindAnyObjectByType<HexGrid>();
            Repaint();
        }

        private void OnProjectChange() => Repaint();

        private void OnUndoRedo()
        {
            if (grid != null) grid.InvalidateCache();
            Repaint();
            SceneView.RepaintAll();
        }

        private void OnPlayModeChanged(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.ExitingEditMode) editing = false;
            Repaint();
        }

        // ------------------------------------------------------------ вікно

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            DrawGridSection();
            if (grid != null && grid.Data != null)
            {
                EditorGUILayout.Space(6);
                DrawToolSection();
                EditorGUILayout.Space(6);
                DrawPaletteSection();
                EditorGUILayout.Space(6);
                DrawBulkSection();
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox(
                "ЛКМ — дія (тягни мишею, щоб малювати)\n" +
                "Shift+ЛКМ — видалити (в режимі «Повернути» — назад на 60°)\n" +
                "[ / ] — поворот тайла перед встановленням\n" +
                "Alt+миша — камера, Esc — вийти з редагування\n" +
                "ПКМ по тайлу в палітрі — меню",
                MessageType.None);

            EditorGUILayout.EndScrollView();
        }

        private void DrawGridSection()
        {
            EditorGUILayout.LabelField("Сітка", EditorStyles.boldLabel);
            grid = (HexGrid)EditorGUILayout.ObjectField("HexGrid", grid, typeof(HexGrid), true);

            if (grid == null)
            {
                if (GUILayout.Button("Створити HexGrid у сцені")) CreateGrid();
                return;
            }

            var so = new SerializedObject(grid);
            EditorGUILayout.PropertyField(so.FindProperty("data"), new GUIContent("CityMapData"));
            so.ApplyModifiedProperties();

            if (grid.Data == null)
            {
                if (GUILayout.Button("Створити CityMapData")) CreateDataFor(grid);
                return;
            }

            var dso = new SerializedObject(grid.Data);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(dso.FindProperty("radius"), new GUIContent("Радіус"));
            EditorGUILayout.PropertyField(dso.FindProperty("hexSize"), new GUIContent("Розмір гекса"));
            bool changed = EditorGUI.EndChangeCheck();
            dso.ApplyModifiedProperties();
            if (changed) SceneView.RepaintAll();

            EditorGUILayout.LabelField(" ",
                $"Клітинок: {HexMath.CountInRadius(grid.Radius)}   Тайлів: {grid.Data.Entries.Count}",
                EditorStyles.miniLabel);
        }

        private void DrawToolSection()
        {
            EditorGUILayout.LabelField("Інструмент", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            {
                var prev = GUI.backgroundColor;
                GUI.backgroundColor = editing ? new Color(0.45f, 1f, 0.5f) : prev;
                if (GUILayout.Button(editing ? "Редагування в Scene view: УВІМК" : "Редагування в Scene view: вимк", GUILayout.Height(28)))
                {
                    editing = !editing;
                    if (editing && SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Focus();
                    SceneView.RepaintAll();
                }
                GUI.backgroundColor = prev;
            }
            if (EditorApplication.isPlaying)
                EditorGUILayout.HelpBox("У Play mode користуйся HexGridPlayTester.", MessageType.Info);

            action = (HexAction)GUILayout.Toolbar((int)action, ActionLabels);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Поворот тайла", GUILayout.Width(100));
                if (GUILayout.Button("◀", GUILayout.Width(28))) turn = HexMath.Mod6(turn - 1);
                GUILayout.Label($"{turn * 60}°", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(40));
                if (GUILayout.Button("▶", GUILayout.Width(28))) turn = HexMath.Mod6(turn + 1);
            }
            var st = SelectedItem as SmartTile;
            if (st != null && (st.IsConnected || st.randomRotation))
                EditorGUILayout.LabelField(" ", st.IsConnected ? "поворот визначають сусіди" : "поворот випадковий",
                    EditorStyles.miniLabel);
        }

        private void DrawPaletteSection()
        {
            EditorGUILayout.LabelField("Палітра", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            palette = (HexTilePalette)EditorGUILayout.ObjectField("Палітра", palette, typeof(HexTilePalette), false);
            if (EditorGUI.EndChangeCheck()) SavePalettePref();

            DrawFolderPicker();
            DrawSmartTileCreate();

            if (palette == null || palette.Count == 0)
            {
                EditorGUILayout.HelpBox("Палітра порожня. Вибери папку й натисни «Нова палітра», " +
                                        "або перетягни префаби / SmartTile у поле нижче.", MessageType.Info);
                if (palette != null) DrawPaletteDropZone();
                return;
            }

            thumbSize = EditorGUILayout.Slider("Розмір превʼю", thumbSize, 40f, 128f);
            selected = Mathf.Clamp(selected, 0, palette.Count - 1);

            int columns = Mathf.Max(1, Mathf.FloorToInt((position.width - 24f) / (thumbSize + 6f)));
            bool loading = false;
            var nameStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperCenter, clipping = TextClipping.Clip };
            var badgeStyle = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter };
            badgeStyle.normal.textColor = Color.black;

            for (int i = 0; i < palette.Count; i += columns)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int j = i; j < Mathf.Min(i + columns, palette.Count); j++)
                    {
                        var item = palette.items[j];
                        using (new EditorGUILayout.VerticalScope(GUILayout.Width(thumbSize + 2f)))
                        {
                            var tex = SmartTileTools.Preview(item, out bool l);
                            loading |= l;

                            var prev = GUI.backgroundColor;
                            if (j == selected) GUI.backgroundColor = new Color(0.3f, 0.75f, 1f);
                            var content = new GUIContent(tex, item != null ? item.name : "(порожньо)");
                            var rect = GUILayoutUtility.GetRect(thumbSize, thumbSize, GUILayout.Width(thumbSize), GUILayout.Height(thumbSize));

                            var e = Event.current;
                            if (e.type == EventType.ContextClick && rect.Contains(e.mousePosition))
                            {
                                ShowItemMenu(j);
                                e.Use();
                            }
                            if (GUI.Button(rect, content))
                            {
                                selected = j;
                                if (action != HexAction.Place) action = HexAction.Place;
                            }
                            GUI.backgroundColor = prev;

                            var badgeRect = new Rect(rect.x + 3, rect.y + 3, 38, 14);
                            if (item is SmartTile s)
                            {
                                EditorGUI.DrawRect(badgeRect, s.IsConnected ? new Color(1f, 0.8f, 0.3f) : new Color(0.55f, 0.85f, 1f));
                                GUI.Label(badgeRect, s.IsConnected ? "AUTO" : "RND", badgeStyle);
                            }
                            else if (item is GameObject go && go.GetComponent<HexConnector>() != null)
                            {
                                EditorGUI.DrawRect(badgeRect, new Color(0.6f, 0.95f, 0.6f));
                                GUI.Label(badgeRect, "LINK", badgeStyle);
                            }
                            GUILayout.Label(item != null ? item.name : "—", nameStyle, GUILayout.Width(thumbSize));
                        }
                    }
                }
            }
            if (loading) Repaint();

            DrawPaletteDropZone();
        }

        private void DrawFolderPicker()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var folderAsset = AssetDatabase.IsValidFolder(paletteFolder)
                    ? AssetDatabase.LoadAssetAtPath<DefaultAsset>(paletteFolder)
                    : null;
                EditorGUI.BeginChangeCheck();
                var picked = (DefaultAsset)EditorGUILayout.ObjectField("Папка", folderAsset, typeof(DefaultAsset), false);
                if (EditorGUI.EndChangeCheck())
                {
                    var path = picked != null ? AssetDatabase.GetAssetPath(picked) : "";
                    if (picked == null || AssetDatabase.IsValidFolder(path)) SetFolder(path);
                    else EditorUtility.DisplayDialog("Папка", "Це не папка. Перетягни сюди папку з Project.", "OK");
                }
                if (GUILayout.Button("…", GUILayout.Width(26)))
                {
                    string start = AssetDatabase.IsValidFolder(paletteFolder) ? paletteFolder : "Assets";
                    var abs = EditorUtility.OpenFolderPanel("Папка з префабами", start, "");
                    if (!string.IsNullOrEmpty(abs))
                    {
                        var rel = SmartTileTools.ToProjectPath(abs);
                        if (rel != null) SetFolder(rel);
                        else EditorUtility.DisplayDialog("Папка", "Папка має бути всередині Assets цього проєкту.", "OK");
                    }
                }
            }

            bool valid = AssetDatabase.IsValidFolder(paletteFolder);
            using (new EditorGUI.DisabledScope(!valid))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Нова палітра з папки", "Префаби і SmartTile з папки та підпапок")))
                    CreatePaletteFromFolder(paletteFolder);
                using (new EditorGUI.DisabledScope(palette == null))
                    if (GUILayout.Button(new GUIContent("Додати з папки", "Додати в поточну палітру те, чого в ній ще нема")))
                        AddToPalette(SmartTileTools.FindPlaceables(paletteFolder));
            }
        }

        private void DrawSmartTileCreate()
        {
            var sel = SmartTileTools.SelectedPrefabs();
            using (new EditorGUI.DisabledScope(sel.Count == 0))
            {
                if (GUILayout.Button(new GUIContent($"Розумний тайл з виділених префабів ({sel.Count})",
                        "Дороги/річки Carved (…_straight, _bend, _fork…) → режим «За сусідами», решта → «Випадковий».")))
                    CreateSmartTile(sel);
            }
        }

        private void DrawPaletteDropZone()
        {
            var drop = GUILayoutUtility.GetRect(0, 30, GUILayout.ExpandWidth(true));
            GUI.Box(drop, "Перетягни сюди префаби або SmartTile", EditorStyles.helpBox);
            var e = Event.current;
            if ((e.type == EventType.DragUpdated || e.type == EventType.DragPerform) && drop.Contains(e.mousePosition))
            {
                var items = new List<Object>();
                foreach (var o in DragAndDrop.objectReferences)
                    if (HexTilePalette.IsPlaceable(o) && AssetDatabase.Contains(o)) items.Add(o);
                DragAndDrop.visualMode = items.Count > 0 ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                if (e.type == EventType.DragPerform && items.Count > 0)
                {
                    DragAndDrop.AcceptDrag();
                    AddToPalette(items);
                }
                e.Use();
            }
        }

        private void ShowItemMenu(int index)
        {
            var item = palette.items[index];
            var menu = new GenericMenu();
            if (item != null)
            {
                menu.AddItem(new GUIContent("Показати в Project"), false, () => EditorGUIUtility.PingObject(item));
                menu.AddItem(new GUIContent("Відкрити в інспекторі"), false, () => Selection.activeObject = item);
            }
            if (item is GameObject prefab)
            {
                var conn = prefab.GetComponent<HexConnector>();
                if (conn != null)
                    menu.AddItem(new GUIContent($"Групи з'єднань: {string.Join(", ", conn.groups)}  (редагувати…)"), false,
                        () => Selection.activeObject = prefab);
                else
                    menu.AddItem(new GUIContent("Додати групу з'єднань (HexConnector)"), false, () => AddConnector(prefab));
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Прибрати з палітри"), false, () => RemoveFromPalette(index));
            menu.ShowAsContext();
        }

        /// <summary>Додає HexConnector на корінь префаба-асета (група — як у першого розумного тайла палітри).</summary>
        private void AddConnector(GameObject prefab)
        {
            var type = PrefabUtility.GetPrefabAssetType(prefab);
            if (type != PrefabAssetType.Regular && type != PrefabAssetType.Variant)
            {
                EditorUtility.DisplayDialog("HexConnector",
                    $"{prefab.name} — не префаб, а модель чи інший асет. Зроби з нього префаб і додай у палітру його.", "OK");
                return;
            }

            string group = "road";
            if (palette != null)
                foreach (var o in palette.items)
                    if (o is SmartTile st && st.IsConnected && st.groups.Count > 0) { group = st.groups[0]; break; }

            var path = AssetDatabase.GetAssetPath(prefab);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var conn = root.GetComponent<HexConnector>();
                if (conn == null) conn = root.AddComponent<HexConnector>();
                if (conn.groups.Count == 0) conn.groups.Add(group);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            Selection.activeObject = prefab;
            if (grid != null && grid.Data != null && !EditorApplication.isPlaying)
                RunGrouped("Refresh smart tiles", () => grid.RefreshAllSmart());
            ShowNotification(new GUIContent($"HexConnector «{group}» додано до {prefab.name}"));
        }

        private void DrawBulkSection()
        {
            EditorGUILayout.LabelField("Уся карта", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(SelectedItem == null))
                        if (GUILayout.Button("Заповнити порожні")) RunGrouped("Fill hex map", () => grid.FillEmpty(SelectedItem, turn));
                    if (GUILayout.Button("Прибрати за межею")) RunGrouped("Trim hex map", () => grid.RemoveOutside());
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("Оновити розумні тайли", "Перерахувати форми доріг/річок (після зміни SmartTile)")))
                        RunGrouped("Refresh smart tiles", () => grid.RefreshAllSmart());
                    if (GUILayout.Button("Перебудувати з даних")) RunGrouped("Rebuild hex map", () => grid.RebuildFromData());
                }
                if (GUILayout.Button("Очистити") &&
                    EditorUtility.DisplayDialog("Очистити карту", "Видалити всі тайли з карти? (можна відмінити через Undo)", "Так", "Ні"))
                    RunGrouped("Clear hex map", () => grid.ClearAll());
            }
        }

        private Object SelectedItem => palette != null ? palette.Get(selected) : null;

        // ------------------------------------------------------------ Scene view

        private void OnSceneGUI(SceneView view)
        {
            if (!editing || EditorApplication.isPlaying || grid == null || grid.Data == null) return;

            var e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);

            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.LeftBracket) { turn = HexMath.Mod6(turn - 1); e.Use(); Repaint(); }
                else if (e.keyCode == KeyCode.RightBracket) { turn = HexMath.Mod6(turn + 1); e.Use(); Repaint(); }
                else if (e.keyCode == KeyCode.Escape) { editing = false; e.Use(); Repaint(); view.Repaint(); return; }
            }

            if (e.isMouse || e.type == EventType.Repaint || e.type == EventType.Layout)
            {
                var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                hasHover = grid.Raycast(ray, out hover);
            }

            if (e.button == 0 && !e.alt)
            {
                if (e.type == EventType.MouseDown && HandleUtility.nearestControl == id)
                {
                    GUIUtility.hotControl = id;
                    Undo.IncrementCurrentGroup();
                    undoGroup = Undo.GetCurrentGroup();
                    Undo.SetCurrentGroupName("Hex map edit");
                    lastAppliedValid = false;
                    ApplyAtHover(e);
                    e.Use();
                }
                else if (e.type == EventType.MouseDrag && GUIUtility.hotControl == id)
                {
                    // Поворот — лише по кліку, щоб не крутити тайл кожен кадр.
                    if (CurrentAction(e) != HexAction.Rotate && (!lastAppliedValid || hover.cell != lastAppliedCell))
                        ApplyAtHover(e);
                    e.Use();
                }
                else if (e.type == EventType.MouseUp && GUIUtility.hotControl == id)
                {
                    GUIUtility.hotControl = 0;
                    Undo.CollapseUndoOperations(undoGroup);
                    e.Use();
                }
            }

            if (e.type == EventType.MouseMove) view.Repaint();
            if (e.type == EventType.Repaint) DrawHover(e);
            DrawOverlay(view);
        }

        private HexAction CurrentAction(Event e)
        {
            if (e.shift && action != HexAction.Rotate) return HexAction.Remove;
            return action;
        }

        private void ApplyAtHover(Event e)
        {
            if (!hasHover) return;
            var act = CurrentAction(e);
            int steps = action == HexAction.Rotate && e.shift ? -1 : 1;
            grid.Apply(act, hover.cell, SelectedItem, turn, steps);
            lastAppliedCell = hover.cell;
            lastAppliedValid = true;
            Repaint();
        }

        private void DrawHover(Event e)
        {
            if (!hasHover) return;

            var act = CurrentAction(e);
            var item = SelectedItem;
            Color c;
            if (!hover.inside) c = new Color(1f, 0.3f, 0.3f);
            else if (act == HexAction.Remove) c = hover.HasTile ? new Color(1f, 0.45f, 0.2f) : new Color(0.6f, 0.6f, 0.6f);
            else if (act == HexAction.Rotate) c = hover.HasTile ? new Color(1f, 0.85f, 0.2f) : new Color(0.6f, 0.6f, 0.6f);
            else c = item != null ? new Color(0.3f, 1f, 0.45f) : new Color(0.6f, 0.6f, 0.6f);

            var up = grid.transform.up * 0.02f;
            grid.GetCellCorners(hover.cell, corners, 0.03f);
            for (int i = 0; i < 6; i++) { corners[i] += up; outline[i] = corners[i]; }
            outline[6] = outline[0];

            var prevZ = Handles.zTest;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            Handles.color = new Color(c.r, c.g, c.b, 0.22f);
            Handles.DrawAAConvexPolygon(corners);
            Handles.color = c;
            Handles.DrawAAPolyLine(4f, outline);

            // Стрілка: куди дивиться ребро 0 тайла. Для Place — майбутній поворот (якщо він ручний), для Rotate — поточний.
            int arrowTurn = -1;
            if (act == HexAction.Place && ManualTurn(item)) arrowTurn = turn;
            else if (act == HexAction.Rotate && hover.HasTile) arrowTurn = hover.tile.turn;
            if (arrowTurn >= 0 && hover.inside)
            {
                var center = grid.CellToWorld(hover.cell) + up;
                var tip = grid.transform.TransformPoint(HexMath.CellToLocal(hover.cell, grid.HexSize) +
                                                        HexMath.EdgeMid(arrowTurn, grid.HexSize) * 0.75f) + up;
                Handles.DrawAAPolyLine(5f, center, tip);
                Handles.DrawSolidDisc(tip, grid.transform.up, grid.HexSize * 0.08f);
            }
            Handles.zTest = prevZ;

            string tileName = "порожньо";
            if (hover.HasTile)
            {
                tileName = hover.tile.prefab != null ? hover.tile.prefab.name : hover.tile.name;
                if (hover.tile.smart != null) tileName += $" [{hover.tile.smart.name}]";
            }
            Handles.Label(grid.CellToWorld(hover.cell) + grid.transform.up * 0.4f,
                $"({hover.cell.x}, {hover.cell.y})  {tileName}{(hover.inside ? "" : "  [поза межею]")}",
                EditorStyles.whiteBoldLabel);
        }

        private static bool ManualTurn(Object item)
        {
            if (item is GameObject) return true;
            return item is SmartTile st && !st.IsConnected && !st.randomRotation;
        }

        private void DrawOverlay(SceneView view)
        {
            Handles.BeginGUI();
            var item = SelectedItem;
            string what = item == null ? "нічого не вибрано" : item.name + (item is SmartTile ? " (розумний)" : "");
            string text = $"Hex Map: {ActionLabels[(int)action]}   поворот {turn * 60}°   {what}   (Esc — вийти)";
            var rect = new Rect(10, view.position.height - 58, 560, 22);
            GUI.Box(rect, GUIContent.none);
            GUI.Label(new Rect(rect.x + 6, rect.y + 3, rect.width - 12, rect.height), text, EditorStyles.boldLabel);
            Handles.EndGUI();
        }

        // ------------------------------------------------------------ асети

        private void RunGrouped(string name, System.Action body)
        {
            Undo.IncrementCurrentGroup();
            int g = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(name);
            body();
            Undo.CollapseUndoOperations(g);
            SceneView.RepaintAll();
        }

        private void SetFolder(string path)
        {
            paletteFolder = path;
            EditorPrefs.SetString(FolderPrefKey, path ?? "");
        }

        private void SavePalettePref()
        {
            EditorPrefs.SetString(PalettePrefKey,
                palette != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(palette)) : "");
        }

        private void CreateGrid()
        {
            var go = new GameObject("CityHexGrid");
            Undo.RegisterCreatedObjectUndo(go, "Create HexGrid");
            grid = Undo.AddComponent<HexGrid>(go);
            Selection.activeGameObject = go;
            if (grid.Data == null) CreateDataFor(grid);
        }

        private static void CreateDataFor(HexGrid target)
        {
            SmartTileTools.EnsureFolder(DefaultDataFolder);
            var path = EditorUtility.SaveFilePanelInProject("Нова карта міста", "CityMapData", "asset",
                "Куди зберегти CityMapData", DefaultDataFolder);
            if (string.IsNullOrEmpty(path)) return;

            var data = CreateInstance<CityMapData>();
            AssetDatabase.CreateAsset(data, path);
            AssetDatabase.SaveAssets();

            var so = new SerializedObject(target);
            so.FindProperty("data").objectReferenceValue = data;
            so.ApplyModifiedProperties();
        }

        private void CreatePaletteFromFolder(string folder)
        {
            var items = SmartTileTools.FindPlaceables(folder);
            if (items.Count == 0)
            {
                EditorUtility.DisplayDialog("Палітра", $"У папці {folder} немає префабів чи SmartTile.", "OK");
                return;
            }

            SmartTileTools.EnsureFolder(DefaultDataFolder);
            var name = System.IO.Path.GetFileName(folder) + "Palette";
            var path = EditorUtility.SaveFilePanelInProject("Нова палітра", name, "asset", "Куди зберегти палітру", DefaultDataFolder);
            if (string.IsNullOrEmpty(path)) return;

            var pal = CreateInstance<HexTilePalette>();
            pal.items = items;
            AssetDatabase.CreateAsset(pal, path);
            AssetDatabase.SaveAssets();
            palette = pal;
            selected = 0;
            SavePalettePref();
        }

        private void AddToPalette(List<Object> items)
        {
            if (palette == null) return;
            Undo.RecordObject(palette, "Add to palette");
            int added = 0;
            foreach (var o in items)
                if (o != null && !palette.items.Contains(o)) { palette.items.Add(o); added++; }
            if (added > 0)
            {
                EditorUtility.SetDirty(palette);
                AssetDatabase.SaveAssetIfDirty(palette);
            }
            ShowNotification(new GUIContent(added > 0 ? $"Додано: {added}" : "Усе вже є в палітрі"));
        }

        private void RemoveFromPalette(int index)
        {
            if (palette == null || index < 0 || index >= palette.Count) return;
            Undo.RecordObject(palette, "Remove from palette");
            palette.items.RemoveAt(index);
            EditorUtility.SetDirty(palette);
            AssetDatabase.SaveAssetIfDirty(palette);
            selected = Mathf.Clamp(selected, 0, Mathf.Max(0, palette.Count - 1));
            Repaint();
        }

        private void CreateSmartTile(List<GameObject> prefabs)
        {
            string folder = AssetDatabase.IsValidFolder(paletteFolder) ? paletteFolder : DefaultDataFolder;
            SmartTileTools.EnsureFolder(folder);
            string suggested = SmartTileTools.TryGuessEdges(prefabs[0].name, out _)
                ? SmartTileTools.GuessGroup(prefabs[0].name) + "_smart"
                : SmartTileTools.BaseName(prefabs[0].name) + "_random";
            var path = EditorUtility.SaveFilePanelInProject("Новий розумний тайл", suggested, "asset",
                "Куди зберегти SmartTile", folder);
            if (string.IsNullOrEmpty(path)) return;

            var tile = SmartTileTools.CreateFromPrefabs(prefabs, path);
            if (palette != null)
            {
                AddToPalette(new List<Object> { tile });
                selected = palette.items.IndexOf(tile);
                action = HexAction.Place;
            }
            Selection.activeObject = tile;
        }
    }
}
