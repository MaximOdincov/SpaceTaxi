using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using SpaceTaxi.Audio;
using SpaceTaxi.Core;
using SpaceTaxi.Economy;

namespace SpaceTaxi.UI
{
    // Экран увольнения (проигрыш): долг превысил порог — МТО расторгает
    // договор. Показ по событию DebtService.Dismissal, мир на паузе, панель
    // поверх всего (200). Кнопка «В МЕНЮ» сбрасывает забег (сейв удаляется).
    // Появляется сам на каждой сцене и ждёт сессию
    public class DismissalScreenController : ModalScreenControllerBase
    {
        private const int SortingOrder = 200; // поверх всех канвасов сцены

        private bool bound;
        private TMP_Text reasonText;
        private DebtService debt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            ModalBootstrap<DismissalScreenController>.Ensure();
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
            // сейв мог сохранить долг выше порога — события на загрузке не
            // будет, проверяем при бинде
            if (debt.IsOverDismissalThreshold)
                ShowDismissal();
        }

        private void ShowDismissal()
        {
            if (opened) // терминальный экран — открылся раз и навсегда
                return;
            Sound.Play(SoundId.DismissalTone); // тон увольнения

            EnsureScreen();
            // причина: долг свыше порога
            if (reasonText != null)
                reasonText.text = debt.IsOverDismissalThreshold
                    ? $"МТО расторгает договор.\nДолг {UiFormat.Currency} {UiFormat.FormatCredits(debt.Balance)} превысил допустимые {UiFormat.Currency} {UiFormat.FormatCredits(debt.DismissalDebtThreshold)}."
                    : "МТО расторгает договор.\nДолги не возвращены.";
            ActivateModal(); // timeScale = 0 — в базе

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
            // увольнение сбрасывает забег: сейв удаляется; автосейв его не
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

        // каркас строится кодом при первом показе
        protected override GameObject BuildScreen()
        {
            var root = new GameObject("DismissalScreen", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            // масштаб как у канваса сцены
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
                "МТО расторгает договор.\nДолги не возвращены.",
                26f, OnboardComputerFactory.BrightColor, "Reason");
            reason.alignment = TextAlignmentOptions.Center;
            reasonText = reason; // текст причины подменяется при показе
            var stats = OnboardComputerFactory.Text(content,
                $"Непогашенный долг: {UiFormat.Currency} {UiFormat.FormatCredits(debt.Balance)}",
                20f, OnboardComputerFactory.LabelColor, "Stats");
            stats.alignment = TextAlignmentOptions.Center;

            // выраженно красная кнопка: полная заливка DangerColor
            var menu = OnboardComputerFactory.Button(content.transform, "В МЕНЮ",
                OnboardComputerFactory.DangerColor,
                OnboardComputerFactory.BrightColor, 26f, "ButtonMenu");
            var menuLayout = menu.AddComponent<LayoutElement>();
            menuLayout.minWidth = 260f;
            menuLayout.preferredWidth = 260f;
            menuLayout.minHeight = 56f;
            menuLayout.preferredHeight = 56f;
            // hover через ColorTint: компоненты >1 дают более светлый красный
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
