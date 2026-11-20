using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceTaxi.Core;
using SpaceTaxi.Economy;
using SpaceTaxi.Orders;
using SpaceTaxi.Physics;
using SpaceTaxi.World;
using UiKit;

namespace SpaceTaxi.UI
{
    // Тон тоста: Danger — деньги ушли, Success — пришли/успех, Neutral — просто информация
    public enum ToastTone
    {
        Neutral,
        Danger,
        Success
    }

    // Тосты событий заказов и корабля: на экране один, остальные ждут в
    // очереди (с дедупом одинаковых текстов); аварийные показываются первыми
    public class ToastNotifier : MonoBehaviour
    {
        [Tooltip("Сколько секунд висит обычный тост")]
        public float duration = 3.5f;

        private const int MaxVisible = 1;        // один тост на экране; очередь ждёт
        private const float EmphasizedDuration = 5f;
        private const float Stagger = 0.15f;     // пауза между показами тостов одного кадра
        private const float DedupWindow = 10f;   // одинаковый текст — не чаще раза в 10 с
        private const float EvictAfter = 1.5f;   // старейший вытесняется, прожив столько
        private const float FadeInTime = 0.15f;
        private const float FadeOutTime = 0.2f;
        private const float RowStep = 8f;        // вертикальный зазор между тостами
        private const float SlideSpeed = 900f;   // скорость сдвига стека, ед/с
        private const float EntryDrop = 20f;     // въезд нового тоста сверху
        private const int DedupCacheLimit = 32;

        // рядовой тост — уже, тревожный — шире и крупнее
        private static readonly Vector2 NormalSize = new Vector2(700f, 44f);
        private static readonly Vector2 EmphasizedSize = new Vector2(900f, 58f);

        private readonly struct Item
        {
            public readonly string Message;
            public readonly bool Emphasized;
            public readonly ToastTone Tone;

            // запись очереди: текст + вид + тон
        public Item(string message, bool emphasized, ToastTone tone)
            {
                Message = message;
                Emphasized = emphasized;
                Tone = tone;
            }
        }

        private sealed class ToastView
        {
            public GameObject Go;
            public RectTransform Rect;
            public TMP_Text Label;
            public CanvasGroup Group;
            public float ShownAt;
            public float HideAt;
            public float TargetY; // верх тоста относительно верха контейнера (вниз = минус)
        }

        private readonly List<Item> pending = new List<Item>();          // [0] — следующий
        private readonly List<ToastView> active = new List<ToastView>(); // [0] — новейший, верх стека
        private readonly List<ToastView> fading = new List<ToastView>(); // уходят, слот не занимают
        private readonly List<ToastView> pool = new List<ToastView>();
        private readonly Dictionary<string, float> lastShownAt = new Dictionary<string, float>();
        private float nextShowAt; // гейт стаггера (unscaledTime)

        private bool bound;

        // подписки на события домена (симметричная отписка в OnDestroy)
        private void Awake()
        {
            // своя альфа: HUD при панелях гасится группой, а тост должен
            // читаться поверх
            var rootGroup = gameObject.AddComponent<CanvasGroup>();
            rootGroup.blocksRaycasts = false;
            rootGroup.ignoreParentGroups = true;

            // поверх станции (100) и завершения заказа (110), под увольнением (200)
            var topCanvas = gameObject.AddComponent<Canvas>();
            topCanvas.overrideSorting = true;
            topCanvas.sortingOrder = 120;

            // корень — невидимый контейнер стека (позиция задана сценой: top-center)
            var self = (RectTransform)transform;
            self.sizeDelta = new Vector2(EmphasizedSize.x, MaxVisible * (EmphasizedSize.y + RowStep));

            for (int i = 0; i < MaxVisible; i++)
                pool.Add(CreateView());
        }

        // вид тоста из пула или новый (CanvasGroup для фейда)
        private ToastView CreateView()
        {
            // сборка через UiKit: панель с фоном + текст
            var box = UiBox.Box(transform, "Toast")
                .Size(NormalSize.x, NormalSize.y)
                .Background(UiTheme.Panel); // raycast off — тост клики не ест
            var go = box.Go;

            // отдельные стейтменты, не «??»: fake-null Unity-объекта не
            // триггерит правую часть
            var group = go.GetComponent<CanvasGroup>();
            if (group == null)
                group = go.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;

            var label = box.Label("", 22f, new Color(0.66f, 1f, 0.8f), null, "Text");
            label.alignment = TextAlignmentOptions.Center;
            var labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(18f, 8f);
            labelRect.offsetMax = new Vector2(-18f, -8f);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = NormalSize; // абсолютный размер: тост вне лейаута
            // вью ждёт в пуле невидимым — иначе три плашки висят на экране
            // с первого кадра сцены до первого прогона через цикл фейда
            go.SetActive(false);
            group.alpha = 0f;

            return new ToastView { Go = go, Rect = rect, Label = label, Group = group };
        }

        // дефолт Neutral — вызовы Show без тона остаются валидными
        public void Show(string message, bool emphasized = false, ToastTone tone = ToastTone.Neutral)
        {
            if (string.IsNullOrEmpty(message) || IsDuplicate(message))
                return;
            var item = new Item(message, emphasized, tone);
            if (emphasized)
            {
                // поверх очереди: после уже ждущих аварийных, но впереди обычных
                int index = 0;
                while (index < pending.Count && pending[index].Emphasized)
                    index++;
                pending.Insert(index, item);
            }
            else
            {
                pending.Add(item);
            }
        }

        // дедуп-окно: тот же текст не сыплется дважды подряд
        private bool IsDuplicate(string message)
        {
            float now = Time.unscaledTime;
            if (lastShownAt.TryGetValue(message, out float shown) && now - shown < DedupWindow)
                return true;
            for (int i = 0; i < pending.Count; i++)
            {
                if (pending[i].Message == message)
                    return true; // уже ждёт показ — второй раз не ставим
            }
            if (lastShownAt.Count > DedupCacheLimit)
                PruneDedupCache(now);
            return false;
        }

        // чистка дедуп-кэша от протухших записей
        private void PruneDedupCache(float now)
        {
            List<string> stale = null;
            foreach (var pair in lastShownAt)
            {
                if (now - pair.Value >= DedupWindow)
                {
                    if (stale == null)
                        stale = new List<string>();
                    stale.Add(pair.Key);
                }
            }
            if (stale != null)
                foreach (var key in stale)
                    lastShownAt.Remove(key);
        }

        // тик: очередь → показ, дедуп-кэш, тайминги вида
        private void Update()
        {
            float now = Time.unscaledTime;
            float dt = Time.unscaledDeltaTime;

            ExpireShown(now, dt); // истёкшие — в затухание
            EvictOldestIfNeeded(now); // вытеснение старейшего
            ShowPending(now); // добор из очереди
            SlideStack(dt); // сдвиг стека
            FadeRetired(dt); // затухание ушедших

            if (bound)
                return;
            BindDomainEvents(); // ленивая привязка к событиям сессии и корабля
        }

        // истёкшие — в затухание, слот освобождается сразу
        private void ExpireShown(float now, float dt)
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var view = active[i];
                view.Group.alpha = Mathf.Min(1f, view.Group.alpha + dt / FadeInTime);
                if (now >= view.HideAt)
                    Retire(i);
            }
        }

        // очередь ждёт, слотов нет — вытесняем старейший прожитый тост
        private void EvictOldestIfNeeded(float now)
        {
            if (pending.Count > 0 && active.Count >= MaxVisible
                && now - active[active.Count - 1].ShownAt >= EvictAfter)
                Retire(active.Count - 1);
        }

        // добор из очереди: стаггер между тостами одного кадра, аварийные — без задержки
        private void ShowPending(float now)
        {
            while (pending.Count > 0 && active.Count < MaxVisible
                && (now >= nextShowAt || pending[0].Emphasized))
            {
                Display(pending[0]);
                pending.RemoveAt(0);
            }
        }

        // плавный сдвиг стека при появлении/уходе соседей
        private void SlideStack(float dt)
        {
            for (int i = 0; i < active.Count; i++)
            {
                var view = active[i];
                float y = view.Rect.anchoredPosition.y;
                if (!Mathf.Approximately(y, view.TargetY))
                    view.Rect.anchoredPosition = new Vector2(0f, Mathf.MoveTowards(y, view.TargetY, SlideSpeed * dt));
            }
        }

        // затухание ушедших на месте
        private void FadeRetired(float dt)
        {
            for (int i = fading.Count - 1; i >= 0; i--)
            {
                var view = fading[i];
                view.Group.alpha -= dt / FadeOutTime;
                if (view.Group.alpha <= 0f)
                {
                    view.Go.SetActive(false);
                    fading.RemoveAt(i);
                    pool.Add(view);
                }
            }
        }

        // тосты вешаются на события заказов/корабля, как только сессия и корабль появляются на сцене
        private void BindDomainEvents()
        {
            var session = FindAnyObjectByType<GameSession>();
            var ship = FindAnyObjectByType<Ship>();
            if (session == null || session.Orders == null || ship == null)
                return;

            var orders = session.Orders;
            orders.PassengerBoarded += offer => Show("Пассажир на борту: " + offer.Passenger.DisplayName
                + Prefix(PassengerProfile.PickPhrase(offer.Passenger.Greetings)));
            orders.OrderDelivered += offer => Show("ЗАКАЗ ВЫПОЛНЕН: " + offer.Passenger.DisplayName
                + Prefix(PassengerProfile.PickPhrase(offer.Passenger.Thanks)), true, ToastTone.Success);
            orders.OrderFailed += offer => Show(
                // срыв по таймеру или повреждению называют причину
                string.IsNullOrEmpty(offer.FailReason)
                    ? "Заказ провален" + Prefix(PassengerProfile.PickPhrase(offer.Passenger.Complaints))
                    : "СРЫВ ЗАКАЗА: " + offer.FailReason + Prefix(PassengerProfile.PickPhrase(offer.Passenger.Complaints)),
                true, ToastTone.Danger);

            // отклик в момент свершения/срыва задания пассажира
            orders.TaskMomentSuccess += text => Show(text, false, ToastTone.Success);
            orders.TaskMomentFail += text => Show(text, true, ToastTone.Danger);

            // тост принятия; торопящийся пассажир предупреждает сразу
            orders.OrderAccepted += offer => Show("Заказ принят: " + offer.Passenger.DisplayName
                + (offer.HurrySeconds > 0f
                    ? " · ПАССАЖИР ТОРОПИТСЯ: " + UiFormat.FormatClock(offer.HurrySeconds) + " на доставку"
                    : ""));

            // промежуточные предупреждения задания
            orders.OrderWarning += message => Show(message, false, ToastTone.Danger);

            var landing = ship.GetComponent<LandingSystem>();
            landing.StationServiced += station => Show($"Станция «{station}»: сервис оплачен", false, ToastTone.Danger);
            landing.StationHint += planet => Show("Станция: [B] — " + StationServices(planet));
            // запас и цена топлива сети при посадке; пустая сеть — своё сообщение
            landing.ShipLanded += planet =>
            {
                if (!planet.HasFuel || session.Orders == null)
                    return;
                var pricing = session.Orders.Pricing;
                if (planet.FuelOutage)
                {
                    Show("Топлива на станции нет — сеть пуста");
                    return;
                }
                int available = FuelNetwork.AvailableUnits(ship.Eff.FuelMax, ship.Fuel, planet.FuelTier, pricing);
                if (available <= 0)
                    return;
                Show($"Топлива на станции: {available} ед · цена " +
                     $"{UiFormat.Currency} {FuelNetwork.PricePerUnit(planet.FuelTier, planet.Sector, pricing):0.##}/ед");
            };

            var hull = ship.GetComponent<HullSystem>();
            hull.Respawned += () => Show("Респавн на планете старта");
            hull.ShipLost += reason => Show("КОРАБЛЬ ПОТЕРЯН — " + reason, false, ToastTone.Danger);

            var debt = session.Debt;
            if (debt != null)
                // долг за аренду — только при аварии арендного корабля
                debt.AccidentRegistered += amount =>
                    Show($"ДОЛГ +{UiFormat.Currency} {UiFormat.FormatCredits(amount)} за арендный корабль", true, ToastTone.Danger);

            // выплата за доставку: сумма на руки + уход в долг
            if (session.Payout != null)
                session.Payout.Paid += result =>
                {
                    Show($"Доставка: +{UiFormat.Currency} {UiFormat.FormatCredits(result.Net)}", true, ToastTone.Success);
                    if (result.ToDebt > 0)
                        Show($"{UiFormat.Currency} {UiFormat.FormatCredits(result.ToDebt)} ушло в счёт долга", false, ToastTone.Danger);
                };

            // предупреждение о приближении кометы
            var comets = FindAnyObjectByType<CometSystem>();
            if (comets != null)
                comets.CometIncoming += (entry, direction) => Show("Комета приближается!", true);

            bound = true;
        }

        // показ: из пула, размер по тону, вверху экрана
        private void Display(Item item)
        {
            if (pool.Count == 0)
                pool.Add(CreateView()); // страховка, слот обязан быть свободен
            var view = pool[pool.Count - 1];
            pool.RemoveAt(pool.Count - 1);

            view.Label.text = item.Message;
            view.Label.color = ToneColor(item.Tone);
            // важные события — крупнее и шире
            view.Label.fontSize = item.Emphasized ? 30f : 22f;
            view.Rect.sizeDelta = item.Emphasized ? EmphasizedSize : NormalSize;
            view.ShownAt = Time.unscaledTime;
            view.HideAt = view.ShownAt + (item.Emphasized ? EmphasizedDuration : duration);
            view.TargetY = 0f;
            view.Rect.anchoredPosition = new Vector2(0f, -EntryDrop);
            view.Group.alpha = 0f;
            view.Go.SetActive(true);

            active.Insert(0, view); // новейший — в верхний слот, остальные сдвигаются вниз
            Relayout();

            lastShownAt[item.Message] = view.ShownAt;
            nextShowAt = view.ShownAt + Stagger;
        }

        // тост-строку в пул (фейд уже сыграл)
        private void Retire(int index)
        {
            fading.Add(active[index]);
            active.RemoveAt(index);
            Relayout();
        }

        // пересобрать стопку живых тостов сверху вниз
        private void Relayout()
        {
            float offset = 0f;
            for (int i = 0; i < active.Count; i++)
            {
                var view = active[i];
                view.TargetY = -offset;
                offset += view.Rect.sizeDelta.y + RowStep;
            }
        }

        // реплика пассажира в хвосте тоста: « — «фраза»»
        private static string Prefix(string phrase)
        {
            return string.IsNullOrEmpty(phrase) ? "" : " — «" + phrase + "»";
        }

        // цвет по тону: красный — списание, зелёный — приход, жёлтый — нейтральное
        private static Color ToneColor(ToastTone tone)
        {
            switch (tone)
            {
                case ToastTone.Danger:
                    return OnboardComputerFactory.DangerColor;
                case ToastTone.Success:
                    return OnboardComputerFactory.BrightColor;
                default:
                    return OnboardComputerFactory.TaxiColor;
            }
        }

        // перечень услуг станции по флагам планеты
        private static string StationServices(World.Planet planet)
        {
            var parts = new System.Collections.Generic.List<string>(3);
            if (planet.HasFuel)
                parts.Add("заправка");
            if (planet.HasRepair)
                parts.Add("мастерская");
            if (planet.IsHub)
                parts.Add("магазин");
            return string.Join(", ", parts);
        }
    }
}
