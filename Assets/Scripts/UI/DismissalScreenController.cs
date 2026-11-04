using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
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

            var menu = OnboardComputerFactory.Button(content.transform, "В МЕНЮ",
                new Color(OnboardComputerFactory.DangerColor.r, OnboardComputerFactory.DangerColor.g,
                    OnboardComputerFactory.DangerColor.b, 0.28f),
                OnboardComputerFactory.BrightColor, 26f, "ButtonMenu");
            var menuLayout = menu.AddComponent<LayoutElement>();
            menuLayout.minWidth = 260f;
            menuLayout.preferredWidth = 260f;
            menuLayout.minHeight = 56f;
            menuLayout.preferredHeight = 56f;
            menu.GetComponent<Button>().onClick.AddListener(GoToMenu);
            return root;
        }
    }
}
