using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using SpaceTaxi.Audio;
using SpaceTaxi.Core;
using SpaceTaxi.Economy;

namespace SpaceTaxi.UI
{
    // Экран увольнения (проигрыш): 3 аварии с непогашенными долгами.
    // Показ по DebtService.Dismissal: timeScale = 0 (как пауза), панель
    // поверх всего, кнопка «В МЕНЮ» сбрасывает забег (сейвов нет, инвалидацию
    // добавит П10.2). Контроллера в каркасах сцен нет — он появляется сам на
    // каждую сцену; в меню сессии нет и просто ждёт. Панель строится кодом
    // при первом показе и повторно не открывается.
    public class DismissalScreenController : MonoBehaviour
    {
        private const int SortingOrder = 200; // поверх всех канвасов сцены

        private bool bound;
        private bool shown;
        private GameObject screen;
        private DebtService debt;

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
            if (FindAnyObjectByType<DismissalScreenController>() != null)
                return;
            new GameObject("DismissalScreenController").AddComponent<DismissalScreenController>();
        }

        private void Update()
        {
            if (bound)
                return;
            var session = FindAnyObjectByType<GameSession>();
            if (session == null || session.Debt == null)
                return;
            debt = session.Debt;
            debt.Dismissal += ShowDismissal; // подписка одна за жизнь сессии
            bound = true;
        }

        private void ShowDismissal()
        {
            if (shown)
                return;
            shown = true;
            Sound.Play(SoundId.DismissalTone); // П9.3: тон увольнения до глушения экранов

            if (screen == null)
                screen = BuildScreen();
            screen.SetActive(true);
            Time.timeScale = 0f; // как пауза: до выхода в меню мир не живёт

            // терминальное состояние: пауза/карта/борткомп больше не управляются,
            // иначе ESC снял бы паузу под модальным экраном
            var pause = FindAnyObjectByType<PauseScreenController>();
            if (pause != null)
                pause.enabled = false;
            var map = FindAnyObjectByType<MapScreenController>();
            if (map != null)
                map.enabled = false;
            var onboard = FindAnyObjectByType<OnboardComputerPanel>();
            if (onboard != null)
                onboard.enabled = false;
            var station = FindAnyObjectByType<StationScreenController>();
            if (station != null)
                station.enabled = false; // B/Esc не должны работать под модальным экраном
        }

        private void GoToMenu()
        {
            // увольнение сбрасывает забег (§3.9): сейв удаляется, «Продолжить»
            // становится неактивным (П10.2); автосейв выхода в меню его не
            // воскресит — Save пропускает терминальное состояние
            SaveSystem.DeleteSave();
            var flow = FindAnyObjectByType<SceneFlowButtons>();
            if (flow != null)
            {
                flow.LoadMainMenu();
                return;
            }
            // страховка для сцены без флоу-кнопок
            Time.timeScale = 1f;
            SceneManager.LoadScene("MainMenu");
        }

        // каркас экрана строится кодом при первом показе (стиль OnboardComputerFactory)
        private GameObject BuildScreen()
        {
            var root = new GameObject("DismissalScreen", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            // Масштаб как у сценического канваса (1920x1080, match 0.5)
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(root.transform, false);
            OnboardComputerFactory.Stretch((RectTransform)background.transform);
            background.GetComponent<Image>().color = new Color(0.01f, 0.02f, 0.015f, 0.95f);

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(root.transform, false);
            OnboardComputerFactory.Stretch((RectTransform)content.transform);
            var layout = content.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 22f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var title = OnboardComputerFactory.Text(content, "УВОЛЕН", 64f, OnboardComputerFactory.DangerColor, "Title");
            title.alignment = TextAlignmentOptions.Center;
            var reason = OnboardComputerFactory.Text(content,
                "МТО расторгает договор.\nДолги не возвращены, аварий слишком много.",
                26f, OnboardComputerFactory.BrightColor, "Reason");
            reason.alignment = TextAlignmentOptions.Center;
            var stats = OnboardComputerFactory.Text(content,
                $"Непогашенный долг: {debt.Balance} кр · аварий: {debt.UnpaidAccidents}",
                20f, OnboardComputerFactory.LabelColor, "Stats");
            stats.alignment = TextAlignmentOptions.Center;

            // подтверждение увольнения — выраженно красная кнопка: полная заливка
            // DangerColor (была 28% прозрачность), светлый текст борткомпа
            var menu = OnboardComputerFactory.Button(content.transform, "В МЕНЮ",
                OnboardComputerFactory.DangerColor,
                OnboardComputerFactory.BrightColor, 26f, "ButtonMenu");
            var menuLayout = menu.AddComponent<LayoutElement>();
            menuLayout.minWidth = 260f;
            menuLayout.preferredWidth = 260f;
            menuLayout.minHeight = 56f;
            menuLayout.preferredHeight = 56f;
            // hover: встроенный ColorTint (паттерн PlayerButton фабрики) — под
            // курсором красный светлеет; тон умножается на цвет фона, поэтому
            // компоненты >1 дают более светлый красный
            var menuButton = menu.GetComponent<Button>();
            menuButton.transition = Selectable.Transition.ColorTint;
            var menuColors = menuButton.colors;
            menuColors.fadeDuration = 0.1f;
            menuColors.highlightedColor = new Color(1.2f, 1.6f, 1.6f);
            menuColors.pressedColor = new Color(0.9f, 0.9f, 0.9f);
            menuButton.colors = menuColors;
            menuButton.onClick.AddListener(GoToMenu);
            return root;
        }
    }
}
