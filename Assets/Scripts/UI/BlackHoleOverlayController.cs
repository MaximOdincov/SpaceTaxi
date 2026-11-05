using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using SpaceTaxi.World;

namespace SpaceTaxi.UI
{
    // Оверлей «затянуло» (Н-10): показан, пока корабль в ShipState.Swallowed —
    // управление отключено, выход один: отключить корабль кнопкой или X —
    // дальше стандартный флоу потери (HullSystem.Abandon). По таймлимиту
    // BlackHole.SwallowTimeoutSeconds дыра отключит корабль сама. Игру НЕ
    // паузит — болтание идёт под оверлеем; при открытых карте/борткомпе/
    // станции/паузе гаснет (как канвас стрелок Н-8), болтание продолжается.
    // Контроллера в сценах нет — самоспавн (стиль OrderCompleteScreenController),
    // канвас строится кодом при первом показе. Состояние опрашивается покадрово
    // (бинд ленивый, как у всех биндеров) — оверлей переживает поздний бинд
    // и смену сцены без событий.
    public class BlackHoleOverlayController : MonoBehaviour
    {
        private const int SortingOrder = 90; // ниже станции (100): swallowed — не модал

        private static readonly Color PanelColor = new Color(0.09f, 0.03f, 0.045f, 0.93f);
        private static readonly Color DimLabelColor = new Color(0.357f, 0.788f, 0.561f, 0.75f);

        private GameObject screen;
        private TMP_Text countdown;
        private BlackHole hole;
        private TMP_FontAsset font;
        private MapScreenController map;
        private OnboardComputerPanel onboard;
        private StationScreenController station;
        private PauseScreenController pause;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            // домен редактора может не перезагружаться между запусками — снимаем старую подписку
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Spawn();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Spawn();
        }

        private static void Spawn()
        {
            if (FindAnyObjectByType<BlackHoleOverlayController>() != null)
                return;
            new GameObject("BlackHoleOverlayController").AddComponent<BlackHoleOverlayController>();
        }

        private void Update()
        {
            if (hole == null)
                hole = FindAnyObjectByType<BlackHole>();

            bool show = hole != null && hole.SwallowActive && !AnyPanelOpen();
            if (show && screen == null)
                screen = BuildScreen();
            if (screen != null && screen.activeSelf != show)
                screen.SetActive(show);
            if (!show)
                return;

            // обратный отсчёт до автоотключения — то же игровое время, что и лимит
            float left = Mathf.Max(0f, BlackHole.SwallowTimeoutSeconds - hole.SwallowElapsed);
            countdown.text = $"автоотключение через {Mathf.CeilToInt(left)} с";

            var kb = Keyboard.current;
            if (kb != null && kb.xKey.wasPressedThisFrame)
                hole.RequestAbandon();
        }

        // оверлей уступает место полноэкранным панелям; сами они клавишу X не едят
        private bool AnyPanelOpen()
        {
            if (map == null) map = FindAnyObjectByType<MapScreenController>();
            if (onboard == null) onboard = FindAnyObjectByType<OnboardComputerPanel>();
            if (station == null) station = FindAnyObjectByType<StationScreenController>();
            if (pause == null) pause = FindAnyObjectByType<PauseScreenController>();
            return (map != null && map.IsOpened)
                || (onboard != null && onboard.IsShown)
                || (station != null && station.IsOpened)
                || (pause != null && pause.IsPaused);
        }

        // каркас строится кодом при первом показе (паттерн StationScreen П3.1)
        private GameObject BuildScreen()
        {
            var root = new GameObject("BlackHoleOverlay", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            // масштаб как у сценического канваса (1920x1080, match 0.5) —
            // иначе на не-1080p разрешениях экран расходится с HUD
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            font = Resources.Load<TMP_FontAsset>("Fonts/TMP/Excalifont SDF")
                ?? Resources.Load<TMP_FontAsset>("Excalifont SDF"); // цепочка шрифта карты, иначе дефолт TMP

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(root.transform, false);
            var panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(680f, 0f); // высоту подберёт fitter
            panel.GetComponent<Image>().color = PanelColor;
            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(28, 28, 24, 20);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = panel.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Label(panel, "КОРАБЛЬ ЗАТЯНУЛО", 34f, OnboardComputerFactory.DangerColor);
            Label(panel, "Управление отключено — борт не отвечает. Отключите корабль, чтобы вернуться.",
                19f, OnboardComputerFactory.LabelColor);

            var button = OnboardComputerFactory.Button(panel.transform, "ОТКЛЮЧИТЬ КОРАБЛЬ",
                new Color(OnboardComputerFactory.DangerColor.r, OnboardComputerFactory.DangerColor.g,
                    OnboardComputerFactory.DangerColor.b, 0.24f),
                OnboardComputerFactory.BrightColor, 22f, "AbandonShipButton");
            var element = button.AddComponent<LayoutElement>();
            element.minHeight = 52f;
            element.preferredHeight = 52f;
            button.GetComponent<Button>().onClick.AddListener(() =>
            {
                if (hole != null)
                    hole.RequestAbandon();
            });

            Label(panel, "X — отключить корабль", 16f, OnboardComputerFactory.LabelColor);
            countdown = Label(panel, "", 16f, DimLabelColor);

            return root;
        }

        private TMP_Text Label(GameObject parent, string content, float size, Color color)
        {
            TMP_Text text = OnboardComputerFactory.Text(parent, content, size, color);
            text.alignment = TextAlignmentOptions.Center;
            if (font != null)
                text.font = font;
            return text;
        }
    }
}
