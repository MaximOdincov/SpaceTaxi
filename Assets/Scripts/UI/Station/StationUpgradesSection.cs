using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceTaxi.Audio;
using SpaceTaxi.Core;
using SpaceTaxi.Economy;
using SpaceTaxi.Physics;
using UiKit;

namespace SpaceTaxi.UI
{
    // Хост секций: секции — обычные классы, в сессию/корабль/тосты ходят
    // только через контроллер станции
    public interface IStationSectionHost
    {
        GameSession Session { get; }      // сессия: Fleet/Wallet/Debt/Config
        Ship Ship { get; }                // борт (кэш BindSession) — статы риски
        bool CanOpen();                   // «стоя на планете с услугами»
        void ShowToast(string message, bool emphasized = false, ToastTone tone = ToastTone.Neutral);
    }

    // Улучшения корабля + досрочное гашение долга; Refresh дергает
    // контроллер по событиям Fleet.UpgradesChanged / Debt.DebtChanged
    public sealed class StationUpgradesSection
    {
        // шаблоны описания прибавки одного уровня: проценты — из UpgradeConfig
        // (максимум / 3, шкала линейная), числа не дублируются
        private const string EngineEffectFormat = "+{0}% тяги";
        private const string TankEffectFormat = "+{0}% бака, −{1}% расхода";
        private const string HullEffectFormat = "+{0}% корпуса, −{1}% жара";
        private const string ManeuverEffectFormat = "+{0}% боковой тяги";

        private readonly IStationSectionHost host;

        // блоки: улучшения + долг
        private GameObject upgradesOwned;   // 4 строки линий (свой корабль)
        private TMP_Text upgradesRental;    // строка-пояснение (аренда всегда сток)
        private UpgradeRowRefs[] upgradeRows;
        private GameObject debtSection;     // виден только при долге > 0
        private TMP_Text debtLine;
        private Button repayButton;
        private TMP_Text repayLabel;

        public StationUpgradesSection(IStationSectionHost host)
        {
            this.host = host;
        }

        // строит оба блока в переданном родителе
        public void Build(Transform shop)
        {
            BuildUpgradesBlock(shop);
            BuildDebtBlock(shop);
        }

        // перечитывание блоков по RefreshShop контроллера (деньги/парк/долг)
        public void Refresh()
        {
            RefreshUpgrades();
            RefreshDebt();
        }

        private sealed class UpgradeRowRefs
        {
            public TMP_Text Level;   // «УР 1 → УР 2» / «УР 3 · МАКС»
            public TMP_Text Effect;  // «+8% тяги» … — прибавка следующего уровня
            public Image[] Segments; // полоска уровня: 3 сегмента, заполнение = уровень
            public Button Buy;
            public TMP_Text BuyLabel;
        }

        // блок «Улучшения»: строка на каждую линию — «УР N → УР N+1»,
        // прибавка следующего уровня, цена по классу корабля, кнопка КУПИТЬ
        // и полоска уровня из 3 сегментов
        private void BuildUpgradesBlock(Transform shop)
        {
            var section = StationUI.Section(shop, "// УЛУЧШЕНИЯ", "UpgradesSection");

            // аренда: строка-пояснение без кнопок
            upgradesRental = OnboardComputerFactory.Text(section,
                "Аренда всегда сток — улучшения только на свой корабль", 17f,
                OnboardComputerFactory.LabelColor, "RentalNote");
            OnboardComputerFactory.FixedHeight(upgradesRental.gameObject, 24f);

            // свой корабль: по строке на линию
            upgradesOwned = OnboardComputerFactory.V("OwnedRows", section.transform, 10f);

            var upgrades = host.Session.Fleet.Upgrades; // GameSession в Awake ставит ассет или фолбэк
            string[] names =
            {
                upgrades.EngineName, upgrades.TankName, upgrades.HullName, upgrades.ManeuverName,
            };
            upgradeRows = new UpgradeRowRefs[ShipStats.LineCount];
            for (int i = 0; i < ShipStats.LineCount; i++)
            {
                var line = (UpgradeLine)i;
                // 12 — воздух между кнопкой и полоской уровней под ней
                var rowGo = OnboardComputerFactory.V("Row_" + (i + 1), upgradesOwned.transform, 12f);

                var top = OnboardComputerFactory.H("Top", rowGo.transform);
                OnboardComputerFactory.FixedHeight(top.gameObject, 34f);
                top.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
                var caption = OnboardComputerFactory.Text(top, names[i], 17f,
                    OnboardComputerFactory.BrightColor, "Caption");
                OnboardComputerFactory.FixedWidth(caption.gameObject, 130f);
                OnboardComputerFactory.FixedHeight(caption.gameObject, 22f);
                var refs = new UpgradeRowRefs();
                refs.Level = OnboardComputerFactory.Text(top, "", 17f, OnboardComputerFactory.LabelColor, "Level");
                OnboardComputerFactory.FixedWidth(refs.Level.gameObject, 120f);
                OnboardComputerFactory.FixedHeight(refs.Level.gameObject, 22f);
                var spacer = OnboardComputerFactory.Text(top, "", 15f, OnboardComputerFactory.LabelColor, "Spacer");
                OnboardComputerFactory.Flexible(spacer.gameObject);
                refs.Buy = BuildRowButton(top.transform, () => BuyUpgrade(line), out refs.BuyLabel);

                // полоска прогресса уровней (C3): 3 сегмента, заполнение = уровень
                refs.Segments = StationUI.SegmentRow(rowGo.transform, "LevelSegments", ShipStats.MaxLevel, 10f);
                refs.Effect = OnboardComputerFactory.Text(rowGo, "", 15f, OnboardComputerFactory.LabelColor, "Effect");
                OnboardComputerFactory.FixedHeight(refs.Effect.gameObject, 20f);
                upgradeRows[i] = refs;
            }

            RefreshUpgrades();
        }

        // компактная кнопка «КУПИТЬ · Đ N»
        private static Button BuildRowButton(Transform parent, UnityEngine.Events.UnityAction onClick, out TMP_Text label)
        {
            var go = StationUI.Button(parent, "—", 15f, "BuyButton");
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = 150f;
            le.preferredWidth = 150f;
            le.minHeight = 32f;
            le.preferredHeight = 32f;
            le.flexibleWidth = 0f;
            le.flexibleHeight = 0f;
            var button = go.GetComponent<Button>();
            button.onClick.AddListener(onClick);
            label = go.GetComponentInChildren<TMP_Text>();
            return button;
        }

        // уровни и цены — из конфига; аренда всегда сток, уровень 3 — без кнопки
        private void RefreshUpgrades()
        {
            if (upgradeRows == null || host.Session == null || host.Session.Fleet == null)
                return;
            var fleet = host.Session.Fleet;
            bool owned = fleet.IsOwned(fleet.Current);
            upgradesRental.gameObject.SetActive(!owned);
            upgradesOwned.SetActive(owned);
            if (!owned)
                return;
            var upgrades = fleet.Upgrades;
            if (upgrades == null)
                return;
            for (int i = 0; i < upgradeRows.Length; i++)
            {
                var row = upgradeRows[i];
                int level = fleet.GetLevel(fleet.Current, (UpgradeLine)i);
                bool maxed = level >= ShipStats.MaxLevel;
                row.Level.text = maxed ? $"УР {level} · МАКС" : $"УР {level} → УР {level + 1}";
                row.Effect.text = maxed ? "Максимальный уровень" : NextLevelEffect(upgrades, (UpgradeLine)i);
                StationUI.SetSegments(row.Segments, level);
                // цена — по классу корабля, как в TryUpgrade
                int price = upgrades.GetPrice((int)fleet.Current.ServiceClass);
                row.BuyLabel.text = maxed
                    ? "МАКС."
                    : $"КУПИТЬ · {UiFormat.Currency} {UiFormat.FormatCredits(price)}";
                row.Buy.interactable = !maxed && price > 0 && host.Session.Wallet.Credits >= price;
                // при нехватке кредитов подпись тускнеет вместе с кнопкой
                row.BuyLabel.color = row.Buy.interactable
                    ? OnboardComputerFactory.BrightColor
                    : OnboardComputerFactory.LabelColor;
            }
        }

        // прибавка следующего уровня: максимум из конфига / 3 — шкала линейная
        private static string NextLevelEffect(UpgradeConfig upgrades, UpgradeLine line)
        {
            float third = 1f / ShipStats.MaxLevel;
            switch (line)
            {
                case UpgradeLine.Engine:
                    return string.Format(EngineEffectFormat, Mathf.RoundToInt(upgrades.EngineThrustPct * third));
                case UpgradeLine.Tank:
                    return string.Format(TankEffectFormat,
                        Mathf.RoundToInt(upgrades.TankCapacityPct * third),
                        Mathf.RoundToInt(upgrades.TankDrainRedPct * third));
                case UpgradeLine.Hull:
                    return string.Format(HullEffectFormat,
                        Mathf.RoundToInt(upgrades.HullHpPct * third),
                        Mathf.RoundToInt(upgrades.HullHeatRedPct * third));
                case UpgradeLine.Maneuver:
                    return string.Format(ManeuverEffectFormat, Mathf.RoundToInt(upgrades.ManeuverStrafePct * third));
                default:
                    return "";
            }
        }

        // покупка: только свой корабль; эффект применяет TryUpgrade
        private void BuyUpgrade(UpgradeLine line)
        {
            if (host.Session == null || host.Session.Fleet == null || !host.CanOpen())
                return;
            var fleet = host.Session.Fleet;
            if (!fleet.IsOwned(fleet.Current))
                return; // аренда всегда сток
            if (fleet.TryUpgrade(fleet.Current, line, host.Session.Wallet))
                Sound.Play(SoundId.PurchaseCash); // касса на покупку
        }

        // блок «Долг»
        private void BuildDebtBlock(Transform shop)
        {
            debtSection = StationUI.Section(shop, "// ДОЛГ", "DebtSection");
            debtLine = OnboardComputerFactory.Text(debtSection, "", 18f, OnboardComputerFactory.DangerColor, "DebtLine");
            OnboardComputerFactory.FixedHeight(debtLine.gameObject, 24f);

            var go = OnboardComputerFactory.Button(debtSection.transform, "—",
                Ui.WithAlpha(OnboardComputerFactory.DangerColor, 0.22f),
                OnboardComputerFactory.DangerColor, 20f, "RepayButton");
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 44f;
            le.preferredHeight = 44f;
            repayButton = go.GetComponent<Button>();
            repayButton.onClick.AddListener(RepayDebt);
            repayLabel = go.GetComponentInChildren<TMP_Text>();

            RefreshDebt(); // Balance == 0 → секция скрыта сразу
        }

        // виден только при Balance > 0; кнопка активна при достатке кредитов
        private void RefreshDebt()
        {
            if (debtSection == null || host.Session == null || host.Session.Debt == null)
                return;
            int balance = host.Session.Debt.Balance;
            debtSection.SetActive(balance > 0);
            if (balance <= 0)
                return;
            debtLine.text = $"Долг: {UiFormat.Currency} {UiFormat.FormatCredits(balance)}";
            repayLabel.text = $"ПОГАСИТЬ ДОСРОЧНО ({UiFormat.Currency} {UiFormat.FormatCredits(balance)})";
            repayButton.interactable = host.Session.Wallet.Credits >= balance;
        }

        // досрочное гашение — единовременно полной суммой
        private void RepayDebt()
        {
            if (host.Session == null || host.Session.Debt == null)
                return;
            int balance = host.Session.Debt.Balance;
            if (balance <= 0 || !host.Session.Wallet.TrySpend(balance))
                return;
            host.Session.Debt.Repay(balance); // DebtChanged скроет блок сам
            Sound.Play(SoundId.DebtTone); // тон расчёта по долгу
            host.ShowToast("Долг погашен", emphasized: true, tone: ToastTone.Danger); // списание — красный
        }
    }
}
