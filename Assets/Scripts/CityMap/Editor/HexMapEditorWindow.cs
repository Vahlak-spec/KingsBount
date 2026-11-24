using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace KingdomBouns.CityMap.EditorTools
{
    /// <summary>
    /// Редактор гексової карти (KingdomBouns → Hex Map Editor).
    /// Малює типи клітинок (HexCellType); вигляд кожного типу береться з палітри сітки.
    /// Карта лежить у HexGrid і зберігається/завантажується як JSON (TextAsset) — це стартові карти для гри.
    ///
    /// ЛКМ — поточна дія (можна вести мишею, щоб малювати/стирати).
    /// Shift+ЛКМ — видалити (у режимі «Повернути» — повернути на −60°).
    /// [ / ] — поворот тайла для встановлення.  Esc — вийти з режиму редагування.
    /// Alt+миша — звичайна навігація камери.
    /// </summary>
    public class HexMapEditorWindow : EditorWindow
    {
        private const string FolderPrefKey = "KingdomBouns.HexMap.PaletteFolder";
        private const string MapPrefKey = "KingdomBouns.HexMap.MapAssetGuid";
        private const string DefaultTilesFolder = "Assets/Prefabs/Tiles/CityMap";
        private const string DefaultDataFolder = "Assets/Data/CityMap";
        private const string DefaultMapsFolder = "Assets/_Project/Data/Maps";

        [SerializeField] private HexGrid grid;
        [SerializeField] private TextAsset mapAsset;
        [SerializeField] private string paletteFolder;
        [SerializeField] private HexCellType selectedType = HexCellType.Meadow;
        [SerializeField] private HexAction action = HexAction.Place;
        [SerializeField] private int turn;
        [SerializeField] private bool editing;
        [SerializeField] private bool dirty;
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

        private HexTilePalette Palette => grid != null ? grid.Palette : null;

        // ------------------------------------------------------------ життєвий цикл

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;

            if (string.IsNullOrEmpty(paletteFolder))
                paletteFolder = EditorPrefs.GetString(FolderPrefKey, DefaultTilesFolder);
            if (mapAsset == null)
            {
                var guid = EditorPrefs.GetString(MapPrefKey, "");
                if (!string.IsNullOrEmpty(guid))
                    mapAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(AssetDatabase.GUIDToAssetPath(guid));
            }
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
            dirty = true;
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
            if (grid != null)
            {
                EditorGUILayout.Space(6);
                DrawMapSection();
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
                "ПКМ по слоту палітри — меню; перетягни тайл на слот, щоб призначити",
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
            EditorGUILayout.PropertyField(so.FindProperty("palette"), new GUIContent("Палітра"));
            so.ApplyModifiedProperties();
        }

        private void DrawMapSection()
        {
            EditorGUILayout.LabelField(dirty ? "Карта  •  не збережено в JSON" : "Карта", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            mapAsset = (TextAsset)EditorGUILayout.ObjectField("JSON карти", mapAsset, typeof(TextAsset), false);
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetString(MapPrefKey, mapAsset != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(mapAsset)) : "");

            var so = new SerializedObject(grid);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(so.FindProperty("map.id"), new GUIContent("Id карти"));
            EditorGUILayout.PropertyField(so.FindProperty("map.radius"), new GUIContent("Радіус"));
            EditorGUILayout.PropertyField(so.FindProperty("map.hexSize"), new GUIContent("Розмір гекса"));
            if (EditorGUI.EndChangeCheck())
            {
                so.ApplyModifiedProperties();
                dirty = true;
                SceneView.RepaintAll();
            }

            EditorGUILayout.LabelField(" ",
                $"Клітинок: {HexMath.CountInRadius(grid.Radius)}   Заповнено: {grid.Map.Count}", EditorStyles.miniLabel);

            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Нова")) NewMap();
                    using (new EditorGUI.DisabledScope(mapAsset == null))
                    {
                        if (GUILayout.Button("Завантажити")) LoadMap();
                        if (GUILayout.Button("Зберегти")) SaveMap(AssetDatabase.GetAssetPath(mapAsset));
                    }
                    if (GUILayout.Button("Зберегти як…")) SaveMapAs();
                }

                if (grid.LegacyData != null)
                {
                    EditorGUILayout.HelpBox($"На сітці є стара карта {grid.LegacyData.name} (формат до enum). " +
                                            "Імпорт переведе префаби в типи за палітрою.", MessageType.Info);
                    using (new EditorGUI.DisabledScope(Palette == null))
                        if (GUILayout.Button("Імпортувати стару CityMapData")) ImportLegacy();
                }
            }
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
                EditorGUILayout.HelpBox("У Play mode карту показує HexMapService; для тестів — HexGridPlayTester.", MessageType.Info);

            action = (HexAction)GUILayout.Toolbar((int)action, ActionLabels);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Поворот тайла", GUILayout.Width(100));
                if (GUILayout.Button("◀", GUILayout.Width(28))) turn = HexMath.Mod6(turn - 1);
                GUILayout.Label($"{turn * 60}°", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(40));
                if (GUILayout.Button("▶", GUILayout.Width(28))) turn = HexMath.Mod6(turn + 1);
            }
            var st = SelectedTile as SmartTile;
            if (st != null && (st.IsConnected || st.randomRotation))
                EditorGUILayout.LabelField(" ", st.IsConnected ? "поворот визначають сусіди" : "поворот випадковий",
                    EditorStyles.miniLabel);
        }

        private void DrawPaletteSection()
        {
            EditorGUILayout.LabelField("Палітра", EditorStyles.boldLabel);
            var pal = Palette;

            DrawFolderPicker();
            DrawSmartTileCreate();

            if (pal == null)
            {
                EditorGUILayout.HelpBox("У сітки нема палітри. Признач її вище або натисни «Нова палітра з папки».", MessageType.Info);
                return;
            }
            if (pal.EnsureAllTypes()) EditorUtility.SetDirty(pal);

            thumbSize = EditorGUILayout.Slider("Розмір превʼю", thumbSize, 40f, 128f);

            var entries = new List<HexTilePalette.Entry>();
            foreach (var e in pal.Entries) if (e.type != HexCellType.None) entries.Add(e);

            int columns = Mathf.Max(1, Mathf.FloorToInt((position.width - 24f) / (thumbSize + 6f)));
            bool loading = false;
            var nameStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperCenter, clipping = TextClipping.Clip };
            var emptyStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { wordWrap = true };
            var badgeStyle = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter };
            badgeStyle.normal.textColor = Color.black;

            for (int i = 0; i < entries.Count; i += columns)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int j = i; j < Mathf.Min(i + columns, entries.Count); j++)
                    {
                        var entry = entries[j];
                        using (new EditorGUILayout.VerticalScope(GUILayout.Width(thumbSize + 2f)))
                        {
                            var rect = GUILayoutUtility.GetRect(thumbSize, thumbSize, GUILayout.Width(thumbSize), GUILayout.Height(thumbSize));
                            var e = Event.current;

                            if (e.type == EventType.ContextClick && rect.Contains(e.mousePosition))
                            {
                                ShowSlotMenu(entry);
                                e.Use();
                            }
                            HandleSlotDrop(rect, entry.type);

                            var prev = GUI.backgroundColor;
                            if (entry.type == selectedType) GUI.backgroundColor = new Color(0.3f, 0.75f, 1f);
                            if (entry.tile != null)
                            {
                                var tex = SmartTileTools.Preview(entry.tile, out bool l);
                                loading |= l;
                                if (GUI.Button(rect, new GUIContent(tex, $"{entry.type}: {entry.tile.name}")))
                                {
                                    selectedType = entry.type;
                                    if (action != HexAction.Place) action = HexAction.Place;
                                }
                            }
                            else
                            {
                                GUI.Box(rect, GUIContent.none);
                                GUI.Label(rect, "нема тайла\n(перетягни сюди)", emptyStyle);
                            }
                            GUI.backgroundColor = prev;

                            var badgeRect = new Rect(rect.x + 3, rect.y + 3, 38, 14);
                            if (entry.tile is SmartTile s)
                            {
                                EditorGUI.DrawRect(badgeRect, s.IsConnected ? new Color(1f, 0.8f, 0.3f) : new Color(0.55f, 0.85f, 1f));
                                GUI.Label(badgeRect, s.IsConnected ? "AUTO" : "RND", badgeStyle);
                            }
                            else if (entry.tile is GameObject go && go.GetComponent<HexConnector>() != null)
                            {
                                EditorGUI.DrawRect(badgeRect, new Color(0.6f, 0.95f, 0.6f));
                                GUI.Label(badgeRect, "LINK", badgeStyle);
                            }
                            GUILayout.Label(entry.type.ToString(), nameStyle, GUILayout.Width(thumbSize));
                        }
                    }
                }
            }
            if (loading) Repaint();
        }

        private void HandleSlotDrop(Rect rect, HexCellType type)
        {
            var e = Event.current;
            if ((e.type != EventType.DragUpdated && e.type != EventType.DragPerform) || !rect.Contains(e.mousePosition)) return;

            Object item = null;
            foreach (var o in DragAndDrop.objectReferences)
                if (HexTilePalette.IsPlaceable(o) && AssetDatabase.Contains(o)) { item = o; break; }

            DragAndDrop.visualMode = item != null ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            if (e.type == EventType.DragPerform && item != null)
            {
                DragAndDrop.AcceptDrag();
                AssignSlot(type, item);
            }
            e.Use();
        }

        private void AssignSlot(HexCellType type, Object tile)
        {
            var pal = Palette;
            if (pal == null) return;
            Undo.RecordObject(pal, "Assign palette slot");
            pal.Set(type, tile);
            EditorUtility.SetDirty(pal);
            AssetDatabase.SaveAssetIfDirty(pal);
            selectedType = type;
            Repaint();
        }

        private void ShowSlotMenu(HexTilePalette.Entry entry)
        {
            var menu = new GenericMenu();
            var tile = entry.tile;
            if (tile != null)
            {
                menu.AddItem(new GUIContent("Показати в Project"), false, () => EditorGUIUtility.PingObject(tile));
                menu.AddItem(new GUIContent("Відкрити в інспекторі"), false, () => Selection.activeObject = tile);
            }
            if (tile is GameObject prefab)
            {
                var conn = prefab.GetComponent<HexConnector>();
                if (conn != null)
                    menu.AddItem(new GUIContent($"Групи з'єднань: {string.Join(", ", conn.groups)}  (редагувати…)"), false,
                        () => Selection.activeObject = prefab);
                else
                    menu.AddItem(new GUIContent("Додати групу з'єднань (HexConnector)"), false, () => AddConnector(prefab));
            }

            var sel = Selection.activeObject;
            if (HexTilePalette.IsPlaceable(sel) && AssetDatabase.Contains(sel))
                menu.AddItem(new GUIContent($"Призначити виділений: {sel.name}"), false, () => AssignSlot(entry.type, sel));
            else
                menu.AddDisabledItem(new GUIContent("Призначити виділений (виділи префаб або SmartTile у Project)"));

            if (tile != null)
            {
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Очистити слот"), false, () => AssignSlot(entry.type, null));
            }
            menu.ShowAsContext();
        }

        private void DrawFolderPicker()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var folderAsset = AssetDatabase.IsValidFolder(paletteFolder)
                    ? AssetDatabase.LoadAssetAtPath<DefaultAsset>(paletteFolder)
                    : null;
                EditorGUI.BeginChangeCheck();
                var picked = (DefaultAsset)EditorGUILayout.ObjectField("Папка тайлів", folderAsset, typeof(DefaultAsset), false);
                if (EditorGUI.EndChangeCheck())
                {
                    var path = picked != null ? AssetDatabase.GetAssetPath(picked) : "";
                    if (picked == null || AssetDatabase.IsValidFolder(path)) SetFolder(path);
                    else EditorUtility.DisplayDialog("Папка", "Це не папка. Перетягни сюди папку з Project.", "OK");
                }
                if (GUILayout.Button("…", GUILayout.Width(26)))
                {
                    string start = AssetDatabase.IsValidFolder(paletteFolder) ? paletteFolder : "Assets";
                    var abs = EditorUtility.OpenFolderPanel("Папка з тайлами", start, "");
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
                if (GUILayout.Button(new GUIContent("Нова палітра з папки", "Слоти заповнюються за назвами тайлів (tile_meadow → Meadow…)")))
                    CreatePaletteFromFolder(paletteFolder);
                using (new EditorGUI.DisabledScope(Palette == null))
                    if (GUILayout.Button(new GUIContent("Заповнити порожні слоти", "З папки, за назвами; зайняті слоти не чіпає")))
                    {
                        Undo.RecordObject(Palette, "Fill palette by name");
                        int n = SmartTileTools.AssignByName(Palette, SmartTileTools.FindPlaceables(paletteFolder));
                        EditorUtility.SetDirty(Palette);
                        AssetDatabase.SaveAssetIfDirty(Palette);
                        ShowNotification(new GUIContent($"Призначено слотів: {n}"));
                    }
            }
        }

        private void DrawSmartTileCreate()
        {
            var sel = SmartTileTools.SelectedPrefabs();
            using (new EditorGUI.DisabledScope(sel.Count == 0))
            {
                if (GUILayout.Button(new GUIContent($"Розумний тайл з виділених префабів ({sel.Count})",
                        "Дороги/річки Carved (…_straight, _bend, _fork…) → «За сусідами», решта → «Випадковий». " +
                        "Якщо слот вибраного типу порожній, тайл одразу туди призначиться.")))
                    CreateSmartTile(sel);
            }
        }

        private void DrawBulkSection()
        {
            EditorGUILayout.LabelField("Уся карта", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(SelectedTile == null))
                        if (GUILayout.Button($"Заповнити порожні: {selectedType}"))
                            RunGrouped("Fill hex map", () => grid.FillEmpty(selectedType, turn));
                    if (GUILayout.Button("Прибрати за межею")) RunGrouped("Trim hex map", () => grid.RemoveOutside());
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("Оновити розумні тайли", "Перерахувати форми доріг/річок (після зміни SmartTile)")))
                        RunGrouped("Refresh smart tiles", () => grid.RefreshAllSmart());
                    if (GUILayout.Button(new GUIContent("Перебудувати з даних", "Наприклад, після зміни палітри")))
                        RunGrouped("Rebuild hex map", () => grid.RebuildFromData());
                }
                if (GUILayout.Button("Очистити") &&
                    EditorUtility.DisplayDialog("Очистити карту", "Видалити всі клітинки з карти? (можна відмінити через Undo)", "Так", "Ні"))
                    RunGrouped("Clear hex map", () => grid.ClearAll());
            }
        }

        private Object SelectedTile => Palette != null ? Palette.Get(selectedType) : null;

        // ------------------------------------------------------------ Scene view

        private void OnSceneGUI(SceneView view)
        {
            if (!editing || EditorApplication.isPlaying || grid == null || grid.Map == null) return;

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
            if (grid.Apply(act, hover.cell, selectedType, turn, steps)) dirty = true;
            lastAppliedCell = hover.cell;
            lastAppliedValid = true;
            Repaint();
        }

        private void DrawHover(Event e)
        {
            if (!hasHover) return;

            var act = CurrentAction(e);
            var tile = SelectedTile;
            Color c;
            if (!hover.inside) c = new Color(1f, 0.3f, 0.3f);
            else if (act == HexAction.Remove) c = hover.HasTile ? new Color(1f, 0.45f, 0.2f) : new Color(0.6f, 0.6f, 0.6f);
            else if (act == HexAction.Rotate) c = hover.HasTile ? new Color(1f, 0.85f, 0.2f) : new Color(0.6f, 0.6f, 0.6f);
            else c = tile != null ? new Color(0.3f, 1f, 0.45f) : new Color(0.6f, 0.6f, 0.6f);

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
            if (act == HexAction.Place && ManualTurn(tile)) arrowTurn = turn;
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

            string info = hover.HasTile ? hover.tile.type.ToString() : "порожньо";
            if (hover.HasTile && hover.tile.prefab != null) info += $" ({hover.tile.prefab.name})";
            Handles.Label(grid.CellToWorld(hover.cell) + grid.transform.up * 0.4f,
                $"({hover.cell.x}, {hover.cell.y})  {info}{(hover.inside ? "" : "  [поза межею]")}",
                EditorStyles.whiteBoldLabel);
        }

        private static bool ManualTurn(Object tile)
        {
            if (tile is GameObject) return true;
            return tile is SmartTile st && !st.IsConnected && !st.randomRotation;
        }

        private void DrawOverlay(SceneView view)
        {
            Handles.BeginGUI();
            string what = SelectedTile != null ? selectedType.ToString() : $"{selectedType} (нема тайла в палітрі)";
            string text = $"Hex Map: {ActionLabels[(int)action]}   поворот {turn * 60}°   {what}   (Esc — вийти)";
            var rect = new Rect(10, view.position.height - 58, 560, 22);
            GUI.Box(rect, GUIContent.none);
            GUI.Label(new Rect(rect.x + 6, rect.y + 3, rect.width - 12, rect.height), text, EditorStyles.boldLabel);
            Handles.EndGUI();
        }

        // ------------------------------------------------------------ карта: JSON

        private void NewMap()
        {
            if (dirty && !EditorUtility.DisplayDialog("Нова карта", "Незбережені зміни карти буде втрачено. Продовжити?", "Так", "Ні"))
                return;
            var map = new HexMapData { radius = grid.Radius, hexSize = grid.HexSize };
            RunGrouped("New hex map", () =>
            {
                Undo.RecordObject(grid, "New hex map");
                grid.SetMap(map);
            });
            mapAsset = null;
            EditorPrefs.SetString(MapPrefKey, "");
            dirty = false;
        }

        private void LoadMap()
        {
            if (mapAsset == null) return;
            if (dirty && !EditorUtility.DisplayDialog("Завантажити карту", "Незбережені зміни карти буде втрачено. Продовжити?", "Так", "Ні"))
                return;
            var map = HexMapData.FromJson(mapAsset.text);
            if (map == null)
            {
                EditorUtility.DisplayDialog("Карта", $"{mapAsset.name}: не вдалося прочитати JSON карти.", "OK");
                return;
            }
            RunGrouped("Load hex map", () =>
            {
                Undo.RecordObject(grid, "Load hex map");
                grid.SetMap(map);
            });
            dirty = false;
        }

        private void SaveMapAs()
        {
            SmartTileTools.EnsureFolder(DefaultMapsFolder);
            string suggested = string.IsNullOrEmpty(grid.Map.id) ? "city_map" : grid.Map.id;
            var path = EditorUtility.SaveFilePanelInProject("Зберегти карту", suggested, "json", "Куди зберегти JSON карти", DefaultMapsFolder);
            if (string.IsNullOrEmpty(path)) return;
            SaveMap(path);
        }

        private void SaveMap(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return;
            if (string.IsNullOrEmpty(grid.Map.id))
            {
                Undo.RecordObject(grid, "Set map id");
                grid.Map.id = Path.GetFileNameWithoutExtension(assetPath);
            }
            grid.Map.version = HexMapData.CURRENT_VERSION;

            var fullPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath);
            File.WriteAllText(fullPath, grid.Map.ToJson(true));
            AssetDatabase.ImportAsset(assetPath);

            mapAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            EditorPrefs.SetString(MapPrefKey, AssetDatabase.AssetPathToGUID(assetPath));
            dirty = false;
            ShowNotification(new GUIContent($"Збережено: {Path.GetFileName(assetPath)}"));
        }

        private void ImportLegacy()
        {
            var legacy = grid.LegacyData;
            var pal = Palette;
            if (legacy == null || pal == null) return;

            var map = new HexMapData { radius = legacy.radius, hexSize = legacy.hexSize, id = legacy.name };
            int skipped = 0;
            foreach (var e in legacy.Entries)
            {
                Object key = e.smart != null ? (Object)e.smart : e.prefab;
                var type = pal.TypeOf(key);
                if (type == HexCellType.None) { skipped++; continue; }
                int variant = e.smart != null ? Mathf.Max(0, e.smart.IndexOfVariant(e.prefab)) : 0;
                map.Set(e.cell, type, e.turn, variant);
            }

            RunGrouped("Import legacy hex map", () =>
            {
                Undo.RecordObject(grid, "Import legacy hex map");
                grid.SetMap(map);
                grid.LegacyData = null;
            });
            dirty = true;
            if (skipped > 0)
                EditorUtility.DisplayDialog("Імпорт", $"Не імпортовано {skipped} клітинок: їхніх префабів/тайлів нема в палітрі. " +
                                                      "Признач їх у палітрі й повтори імпорт (Ctrl+Z, щоб відкотити).", "OK");
            else
                ShowNotification(new GUIContent($"Імпортовано {map.Count} клітинок — не забудь зберегти JSON"));
        }

        // ------------------------------------------------------------ асети

        private void RunGrouped(string name, System.Action body)
        {
            Undo.IncrementCurrentGroup();
            int g = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(name);
            body();
            Undo.CollapseUndoOperations(g);
            dirty = true;
            SceneView.RepaintAll();
        }

        private void SetFolder(string path)
        {
            paletteFolder = path;
            EditorPrefs.SetString(FolderPrefKey, path ?? "");
        }

        private void CreateGrid()
        {
            var go = new GameObject("HexGrid");
            Undo.RegisterCreatedObjectUndo(go, "Create HexGrid");
            grid = Undo.AddComponent<HexGrid>(go);
            Selection.activeGameObject = go;
        }

        private void CreatePaletteFromFolder(string folder)
        {
            var items = SmartTileTools.FindPlaceables(folder);
            SmartTileTools.EnsureFolder(DefaultDataFolder);
            var name = Path.GetFileName(folder) + "Palette";
            var path = EditorUtility.SaveFilePanelInProject("Нова палітра", name, "asset", "Куди зберегти палітру", DefaultDataFolder);
            if (string.IsNullOrEmpty(path)) return;

            var pal = CreateInstance<HexTilePalette>();
            pal.EnsureAllTypes();
            int n = SmartTileTools.AssignByName(pal, items);
            AssetDatabase.CreateAsset(pal, path);
            AssetDatabase.SaveAssets();

            if (grid != null)
            {
                var so = new SerializedObject(grid);
                so.FindProperty("palette").objectReferenceValue = pal;
                so.ApplyModifiedProperties();
            }
            ShowNotification(new GUIContent($"Палітра створена, за назвами призначено слотів: {n}"));
        }

        /// <summary>Додає HexConnector на корінь префаба-асета (група — як у першого розумного тайла палітри).</summary>
        private void AddConnector(GameObject prefab)
        {
            var type = PrefabUtility.GetPrefabAssetType(prefab);
            if (type != PrefabAssetType.Regular && type != PrefabAssetType.Variant)
            {
                EditorUtility.DisplayDialog("HexConnector",
                    $"{prefab.name} — не префаб, а модель чи інший асет. Зроби з нього префаб і признач у палітру його.", "OK");
                return;
            }

            string group = "road";
            if (Palette != null)
                foreach (var e in Palette.Entries)
                    if (e.tile is SmartTile st && st.IsConnected && st.groups.Count > 0) { group = st.groups[0]; break; }

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
            if (grid != null && !EditorApplication.isPlaying)
                RunGrouped("Refresh smart tiles", () => grid.RefreshAllSmart());
            ShowNotification(new GUIContent($"HexConnector «{group}» додано до {prefab.name}"));
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
            var pal = Palette;
            if (pal != null)
            {
                // Слот: вгаданий за назвою, інакше вибраний; призначаємо, лише якщо він порожній.
                var guess = SmartTileTools.GuessType(tile.name);
                var slot = guess != HexCellType.None ? guess : selectedType;
                if (!pal.Has(slot))
                {
                    AssignSlot(slot, tile);
                    ShowNotification(new GUIContent($"{tile.name} → слот {slot}"));
                }
                else ShowNotification(new GUIContent($"Слот {slot} зайнятий — перетягни {tile.name} на потрібний слот"));
            }
            Selection.activeObject = tile;
        }
    }
}
