using System.Linq;
using KingdomBouns.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KingdomBouns.EditorTools
{
    /// <summary>
    /// Меню KingdomBouns → Core:
    ///  • «Створити сцену головного меню» — сцена з Canvas, кнопками, вікном налаштувань і MainMenuSceneBinder (усе вже прив'язано).
    ///  • «Додати Bootstrap гри в поточну сцену» — [Bootstrap] з Ticker + GameSceneBinder (камера, мікшер, пули).
    ///  • «Додати поточну сцену в Build Settings».
    /// </summary>
    public static class CoreSceneBuilder
    {
        private const string MENU = "KingdomBouns/Core/";
        private const string MIXER_PATH = "Assets/_Project/Resources/Sounds/SoundMixer.mixer";
        private const string SCENES_PARENT = "Assets/_Project";
        private const string SCENES_FOLDER = SCENES_PARENT + "/Scenes";
        private const string MAIN_MENU_SCENE = SCENES_FOLDER + "/MainMenuScene.unity";

        private static DefaultControls.Resources _uiResources;
        private static Font _font;

        // ------------------------------------------------------------------ меню

        [MenuItem(MENU + "Створити сцену головного меню")]
        public static void CreateMainMenuScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MAIN_MENU_SCENE) != null
                && !EditorUtility.DisplayDialog("Головне меню", $"{MAIN_MENU_SCENE} уже існує. Перезаписати?", "Перезаписати", "Скасувати"))
                return;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            Camera camera = Camera.main;
            if (camera != null)
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.13f, 0.15f, 0.2f);
            }

            EnsureEventSystem();
            Transform canvas = CreateCanvas("MainMenuCanvas");

            CreateText(canvas, "Title", "KingdomBouns", 110, new Vector2(0f, 330f), new Vector2(1400f, 180f), FontStyle.Bold);

            var buttons = new GameObject("Buttons", typeof(RectTransform), typeof(VerticalLayoutGroup)).GetComponent<RectTransform>();
            buttons.SetParent(canvas, false);
            buttons.sizeDelta = new Vector2(560f, 4 * 100f + 3 * 24f);
            buttons.anchoredPosition = new Vector2(0f, -80f);
            var layout = buttons.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 24f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            Button continueButton = CreateButton(buttons, "ContinueButton", "Продовжити");
            Button newGameButton = CreateButton(buttons, "NewGameButton", "Нова гра");
            Button settingsButton = CreateButton(buttons, "SettingsButton", "Налаштування");
            Button quitButton = CreateButton(buttons, "QuitButton", "Вихід");

            SettingsPanelView settings = CreateSettingsPanel(canvas);

            MainMenuView view = canvas.gameObject.AddComponent<MainMenuView>();
            SetRef(view, Backing(nameof(MainMenuView.ContinueButton)), continueButton);
            SetRef(view, Backing(nameof(MainMenuView.NewGameButton)), newGameButton);
            SetRef(view, Backing(nameof(MainMenuView.SettingsButton)), settingsButton);
            SetRef(view, Backing(nameof(MainMenuView.QuitButton)), quitButton);
            SetRef(view, Backing(nameof(MainMenuView.SettingsPanel)), settings);

            var bootstrap = new GameObject("[Bootstrap]");
            Ticker ticker = bootstrap.AddComponent<Ticker>();
            MainMenuSceneBinder binder = bootstrap.AddComponent<MainMenuSceneBinder>();
            SetRef(binder, "_ticker", ticker);
            SetRef(binder, "_view", view);
            AssignAudio(binder);

            if (!AssetDatabase.IsValidFolder(SCENES_FOLDER))
                AssetDatabase.CreateFolder(SCENES_PARENT, "Scenes");

            EditorSceneManager.SaveScene(scene, MAIN_MENU_SCENE);
            AddSceneToBuild(MAIN_MENU_SCENE, asFirst: true);
            Debug.Log($"[Core] Створено {MAIN_MENU_SCENE}. Назву ігрової сцени задай у [Bootstrap] → Game Scene Name.");
        }

        [MenuItem(MENU + "Додати Bootstrap гри в поточну сцену")]
        public static void AddGameBootstrap()
        {
            CitySceneBinder existing = Object.FindAnyObjectByType<CitySceneBinder>();
            if (existing != null)
            {
                Selection.activeObject = existing.gameObject;
                EditorGUIUtility.PingObject(existing);
                Debug.Log("[Core] GameSceneBinder уже є на сцені.");
                return;
            }

            var bootstrap = new GameObject("[Bootstrap]");
            Undo.RegisterCreatedObjectUndo(bootstrap, "Add Game Bootstrap");
            Ticker ticker = bootstrap.AddComponent<Ticker>();
            CitySceneBinder binder = bootstrap.AddComponent<CitySceneBinder>();

            var pools = new GameObject("[Pools]").transform;
            pools.SetParent(bootstrap.transform, false);

            SetRef(binder, "_ticker", ticker);
            SetRef(binder, "_camera", Camera.main);
            SetRef(binder, "_poolContainer", pools);
            AssignAudio(binder);
            EnsureEventSystem();

            if (Camera.main != null && Camera.main.GetComponent("CityCameraController") != null)
            {
                SetBool(binder, "_useStrategyCamera", false);
                Debug.Log("[Core] На камері вже є CityCameraController — StrategyCameraService вимкнено (Use Strategy Camera).");
            }

            EditorSceneManager.MarkSceneDirty(bootstrap.scene);
            Selection.activeObject = bootstrap;
            if (Camera.main == null)
                Debug.LogWarning("[Core] На сцені немає камери з тегом MainCamera — признач Camera в GameSceneBinder вручну.");
        }

        [MenuItem(MENU + "Додати поточну сцену в Build Settings")]
        public static void AddCurrentSceneToBuild()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
            {
                Debug.LogWarning("[Core] Спершу збережи сцену.");
                return;
            }

            AddSceneToBuild(scene.path, asFirst: false);
            Debug.Log($"[Core] '{scene.name}' у Build Settings.");
        }

        // ------------------------------------------------------------------ UI

        private static SettingsPanelView CreateSettingsPanel(Transform canvas)
        {
            GameObject overlay = DefaultControls.CreatePanel(UiResources());
            overlay.name = "SettingsPanel";
            overlay.transform.SetParent(canvas, false);
            overlay.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);

            GameObject window = DefaultControls.CreatePanel(UiResources());
            window.name = "Window";
            window.transform.SetParent(overlay.transform, false);
            var windowRect = window.GetComponent<RectTransform>();
            windowRect.anchorMin = windowRect.anchorMax = new Vector2(0.5f, 0.5f);
            windowRect.sizeDelta = new Vector2(820f, 600f);
            windowRect.anchoredPosition = Vector2.zero;
            window.GetComponent<Image>().color = new Color(0.2f, 0.23f, 0.3f, 1f);

            Transform w = window.transform;
            CreateText(w, "Title", "Налаштування", 56, new Vector2(0f, 220f), new Vector2(700f, 80f), FontStyle.Bold);
            CreateText(w, "MusicLabel", "Музика", 40, new Vector2(-250f, 90f), new Vector2(240f, 60f));
            Slider music = CreateSlider(w, "MusicSlider", new Vector2(110f, 90f));
            CreateText(w, "EffectsLabel", "Ефекти", 40, new Vector2(-250f, -20f), new Vector2(240f, 60f));
            Slider effects = CreateSlider(w, "EffectsSlider", new Vector2(110f, -20f));

            Button back = CreateButton(w, "BackButton", "Назад");
            var backRect = back.GetComponent<RectTransform>();
            backRect.anchorMin = backRect.anchorMax = new Vector2(0.5f, 0.5f);
            backRect.sizeDelta = new Vector2(360f, 90f);
            backRect.anchoredPosition = new Vector2(0f, -200f);

            SettingsPanelView view = overlay.AddComponent<SettingsPanelView>();
            SetRef(view, Backing(nameof(SettingsPanelView.MusicVolumeSlider)), music);
            SetRef(view, Backing(nameof(SettingsPanelView.EffectsVolumeSlider)), effects);
            SetRef(view, Backing(nameof(SettingsPanelView.CloseButton)), back);
            return view;
        }

        private static Transform CreateCanvas(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return go.transform;
        }

        private static Text CreateText(Transform parent, string name, string value, int size, Vector2 position, Vector2 rectSize,
            FontStyle style = FontStyle.Normal)
        {
            GameObject go = DefaultControls.CreateText(UiResources());
            go.name = name;
            go.transform.SetParent(parent, false);

            var text = go.GetComponent<Text>();
            text.text = value;
            text.font = DefaultFont();
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = rectSize;
            rect.anchoredPosition = position;
            return text;
        }

        private static Button CreateButton(Transform parent, string name, string label)
        {
            GameObject go = DefaultControls.CreateButton(UiResources());
            go.name = name;
            go.transform.SetParent(parent, false);

            foreach (Text text in go.GetComponentsInChildren<Text>(true))
            {
                text.text = label;
                text.font = DefaultFont();
                text.fontSize = 40;
                text.color = new Color(0.15f, 0.15f, 0.18f);
            }

            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = 560f;
            element.preferredHeight = 100f;
            return go.GetComponent<Button>();
        }

        private static Slider CreateSlider(Transform parent, string name, Vector2 position)
        {
            GameObject go = DefaultControls.CreateSlider(UiResources());
            go.name = name;
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(480f, 40f);
            rect.anchoredPosition = position;

            var slider = go.GetComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 1f;
            return slider;
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null)
                return;

            var go = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
            Undo.RegisterCreatedObjectUndo(go, "Add EventSystem");
        }

        private static DefaultControls.Resources UiResources()
        {
            if (_uiResources.standard != null)
                return _uiResources;

            _uiResources = new DefaultControls.Resources
            {
                standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
                background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
                inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd"),
                knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
                checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
                dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
                mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd"),
            };
            return _uiResources;
        }

        private static Font DefaultFont()
        {
            if (_font == null)
            {
#if UNITY_2022_2_OR_NEWER
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
                _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
            }
            return _font;
        }

        // ------------------------------------------------------------------ утиліти

        private static void AssignAudio(SceneBinderBase binder)
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MIXER_PATH);
            if (mixer == null)
            {
                Debug.LogWarning($"[Core] Не знайдено мікшер {MIXER_PATH} — признач AudioMixer і групи вручну.");
                return;
            }

            SetRef(binder, "_audioMixer", mixer);
            SetRef(binder, "_effectsGroup", mixer.FindMatchingGroups("SOUNDS").FirstOrDefault());
            SetRef(binder, "_musicGroup", mixer.FindMatchingGroups("MUSIC").FirstOrDefault());
        }

        private static string Backing(string propertyName) => $"<{propertyName}>k__BackingField";

        private static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"[Core] У {target.GetType().Name} немає серіалізованого поля {field}.");
                return;
            }

            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBool(Object target, string field, bool value)
        {
            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"[Core] У {target.GetType().Name} немає серіалізованого поля {field}.");
                return;
            }

            property.boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddSceneToBuild(string path, bool asFirst)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == path))
                return;

            var entry = new EditorBuildSettingsScene(path, true);
            if (asFirst)
                scenes.Insert(0, entry);
            else
                scenes.Add(entry);
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
