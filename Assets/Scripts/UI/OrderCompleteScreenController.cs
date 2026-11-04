using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using SpaceTaxi.Core;
using SpaceTaxi.Economy;
using SpaceTaxi.Orders;

namespace SpaceTaxi.UI
{
    // Экран завершения заказа (П4.3): модальная разбивка последней выплаты.
    // Данные — PayoutService.LastResult (null — экран не открывать) и дельта
    // рейтинга RatingService.LastDelta. Мир ставится на паузу (timeScale = 0)
    // на время экрана; закрытие ЛЮБЫМ путём (кнопка/ESC/Space/Enter) возвращает
    // 1. ESC уступается паузой по договорённости карты/станции
    // (NotifyOrderCompleteOpened/Closed + EscJustClosed): при открытом экране
    // завершения ESC закрывает его, пауза не открывается. Контроллера в сценах
    // нет — появляется сам (стиль DismissalScreenController), панель строится
    // кодом при первом показе (паттерн StationScreen П3.1).
    public class OrderCompleteScreenController : MonoBehaviour
    {
        private const int SortingOrder = 110; // поверх станции (100), ниже увольнения (200)

        private bool bound;
        private bool opened;
        private GameObject screen;
        private GameSession session;
        private PauseScreenController pauseController;

        private TMP_Text subtitle;
        private TMP_Text priceLine;
        private TMP_Text penaltyLine;
        private TMP_Text commissionLine;
        private TMP_Text tipsLine;
        private TMP_Text debtLine;
        private TMP_Text totalLine;
        private TMP_Text ratingLine;
        private TMP_Text taskLine;

        public bool IsOpened => opened;

        public bool EscJustClosed { get; private set; }

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
            if (FindAnyObjectByType<OrderCompleteScreenController>() != null)
                return;
            new GameObject("OrderCompleteScreenController").AddComponent<OrderCompleteScreenController>();
        }

        private void Update()
        {
            BindSession();
            if (!opened)
                return;
            var kb = Keyboard.current;
            if (kb == null)
                return;
            if (kb.escapeKey.wasPressedThisFrame)
            {
                Close();
                EscJustClosed = true;
            }
            else if (kb.spaceKey.wasPressedThisFrame
                     || kb.enterKey.wasPressedThisFrame
                     || kb.numpadEnterKey.wasPressedThisFrame)
                Close();
        }

        private void LateUpdate()
        {
            EscJustClosed = false;
        }

        private void OnDisable()
        {
            // страховка: экран сорвало со сцены — мир не должен остаться на паузе
            if (opened)
                Time.timeScale = 1f;
        }

        // лениво, пока сессия не соберётся (стиль биндеров)
        private void BindSession()
        {
            if (bound)
                return;
            session = FindAnyObjectByType<GameSession>();
            if (session == null || session.Payout == null)
                return;
            bound = true;
        }

        // открытие: GameSession зовёт после выплаты и тостов; offer даёт имя
        // пассажира для подзаголовка (может быть null)
        public void Show(OrderOffer offer = null)
        {
            if (opened)
                return;
            if (!bound)
                BindSession();
            if (session == null || session.Payout == null || session.Payout.LastResult == null)
                return; // выплаты не было — показывать нечего

            if (screen == null)
                screen = BuildScreen();
            Refresh(offer);
            screen.SetActive(true);
            opened = true;
            Time.timeScale = 0f; // мир ждёт «ПРОДОЛЖИТЬ»

            if (pauseController == null) pauseController = FindAnyObjectByType<PauseScreenController>();
            if (pauseController != null)
                pauseController.NotifyOrderCompleteOpened();
        }

        private void Close()
        {
            if (!opened)
                return;
            opened = false;
            if (screen != null)
                screen.SetActive(false);
            Time.timeScale = 1f; // гарантия: любой путь закрытия возвращает мир

            if (pauseController == null) pauseController = FindAnyObjectByType<PauseScreenController>();
            if (pauseController != null)
                pauseController.NotifyOrderCompleteClosed();
        }

        private void Refresh(OrderOffer offer)
        {
            PayoutResult result = session.Payout.LastResult;

            bool hasPassenger = offer != null && offer.Passenger != null;
            subtitle.gameObject.SetActive(hasPassenger);
            if (hasPassenger)
                subtitle.text = offer.Passenger.DisplayName + " доставлен";

            priceLine.text = $"Цена {result.Price} кр";
            SetRow(penaltyLine, result.Penalty > 0, $"Штраф −{result.Penalty} кр ({result.Collisions} столкн.)");
            SetRow(commissionLine, result.Commission > 0, $"Комиссия аренды −{result.Commission} кр");
            SetRow(tipsLine, result.Tips > 0, $"Чаевые +{result.Tips} кр"); // чаевые появятся в П8.2
            SetRow(debtLine, result.ToDebt > 0, $"В счёт долга −{result.ToDebt} кр");
            totalLine.text = $"ИТОГО +{result.Net} кр";

            float delta = session.Rating != null ? session.Rating.LastDelta : 0f;
            ratingLine.text = "Рейтинг " + delta.ToString("+0.00;-0.00", CultureInfo.InvariantCulture);

            // статус задания (П8.2) читается напрямую из task (UI-слой), в LastResult не пишется
            bool taskDone = hasPassenger && offer.Task != null && offer.Task.Succeeded;
            SetRow(taskLine, taskDone, "Задание пассажира: выполнено");
        }

        // строка разбивки: скрыта при нуле (чтобы экран был короче без инцидентов)
        private static void SetRow(TMP_Text row, bool visible, string text)
        {
            row.gameObject.SetActive(visible);
            if (visible)
                row.text = text;
        }

        // каркас экрана строится кодом при первом показе (стиль OnboardComputerFactory)
        private GameObject BuildScreen()
        {
            var root = new GameObject("OrderCompleteScreen", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            // Масштаб как у сценического канваса (1920x1080, match 0.5) —
            // иначе на не-1080p разрешениях экран расходится с HUD
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(root.transform, false);
            OnboardComputerFactory.Stretch((RectTransform)background.transform);
            background.GetComponent<Image>().color = new Color(0.01f, 0.02f, 0.015f, 0.85f);

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(root.transform, false);
            var panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(520f, 0f); // высоту подберёт fitter
            panel.GetComponent<Image>().color = OnboardComputerFactory.PanelColor;
            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 20, 18);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = panel.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // --- Шапка: точка + заголовок + пассажир ---
            var titleRow = OnboardComputerFactory.H("TitleRow", panel.transform);
            var dot = OnboardComputerFactory.Text(titleRow, "<voffset=0.35em>●</voffset>", 18f, OnboardComputerFactory.TaxiColor, "LinkDot");
            OnboardComputerFactory.FixedWidth(dot.gameObject, 24f);
            var title = OnboardComputerFactory.Text(titleRow, "ЗАКАЗ ВЫПОЛНЕН", 26f, OnboardComputerFactory.BrightColor, "Title");
            OnboardComputerFactory.Flexible(title.gameObject);

            subtitle = BuildRow(panel.transform, "Subtitle", OnboardComputerFactory.LabelColor);

            var line = new GameObject("Line", typeof(RectTransform), typeof(Image));
            line.transform.SetParent(panel.transform, false);
            line.GetComponent<Image>().color = new Color(
                OnboardComputerFactory.LabelColor.r, OnboardComputerFactory.LabelColor.g, OnboardComputerFactory.LabelColor.b, 0.4f);
            OnboardComputerFactory.FixedHeight(line, 2f);

            // --- Разбивка выплаты ---
            priceLine = BuildRow(panel.transform, "PriceLine", OnboardComputerFactory.BrightColor);
            penaltyLine = BuildRow(panel.transform, "PenaltyLine", OnboardComputerFactory.DangerColor);
            commissionLine = BuildRow(panel.transform, "CommissionLine", OnboardComputerFactory.DangerColor);
            tipsLine = BuildRow(panel.transform, "TipsLine", OnboardComputerFactory.BrightColor);
            debtLine = BuildRow(panel.transform, "DebtLine", OnboardComputerFactory.DangerColor);

            totalLine = BuildRow(panel.transform, "TotalLine", OnboardComputerFactory.TaxiColor, 24f, 30f);
            ratingLine = BuildRow(panel.transform, "RatingLine", OnboardComputerFactory.BrightColor);
            taskLine = BuildRow(panel.transform, "TaskLine", OnboardComputerFactory.LabelColor, 16f, 20f);

            // --- Кнопка продолжения ---
            var button = OnboardComputerFactory.Button(panel.transform, "ПРОДОЛЖИТЬ",
                new Color(OnboardComputerFactory.LabelColor.r, OnboardComputerFactory.LabelColor.g,
                    OnboardComputerFactory.LabelColor.b, 0.22f),
                OnboardComputerFactory.BrightColor, 20f, "ContinueButton");
            var buttonLe = button.AddComponent<LayoutElement>();
            buttonLe.minHeight = 46f;
            buttonLe.preferredHeight = 46f;
            button.GetComponent<Button>().onClick.AddListener(Close);

            var footer = OnboardComputerFactory.Text(panel, "ESC / ПРОБЕЛ / ENTER — продолжить", 16f,
                OnboardComputerFactory.LabelColor, "Footer");
            OnboardComputerFactory.FixedHeight(footer.gameObject, 20f);

            return root;
        }

        // строка разбивки — одна текстовая строка фиксированной высоты
        private static TMP_Text BuildRow(Transform parent, string name, Color color, float size = 19f, float height = 24f)
        {
            var text = OnboardComputerFactory.Text(parent.gameObject, "", size, color, name);
            OnboardComputerFactory.FixedHeight(text.gameObject, height);
            return text;
        }
    }
}
