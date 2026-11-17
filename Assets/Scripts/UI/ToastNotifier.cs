using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceTaxi.Core;
using SpaceTaxi.Economy;
using SpaceTaxi.Orders;
using SpaceTaxi.Physics;
using SpaceTaxi.World;

namespace SpaceTaxi.UI
{
    // Тон тоста (Н6 U4): Danger — деньги ушли (долг/штраф/покупка/сервис),
    // Success — деньги пришли или успех заказа, Neutral — информационное
    public enum ToastTone
    {
        Neutral,
        Danger,
        Success
    }

    // Тосты событий заказов и корабля: ОДИН на экране (владелец Н9.5:
    // «максимум одно, не накладываются»), очередь со стаггером и дедупом,
    // аварийные (emphasized) идут первыми
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
            // П11.1: HUD при панелях гасится корневой группой — тост живёт внутри
            // HUD, но должен читаться поверх панелей: своя альфа без родителей
            var rootGroup = gameObject.AddComponent<CanvasGroup>();
            rootGroup.blocksRaycasts = false;
            rootGroup.ignoreParentGroups = true;

            // поверх карты/паузы (один канвас) и станции/модала завершения
            // (100/110), под увольнением (200)
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
            var go = new GameObject("Toast", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            go.transform.SetParent(transform, false);

            var image = go.GetComponent<Image>();
            image.color = new Color(0.031f, 0.078f, 0.047f, 0.92f);
            image.raycastTarget = false;

            var group = go.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(go.transform, false);
            var label = textGo.GetComponent<TextMeshProUGUI>();
            label.fontSize = 22f;
            label.color = new Color(0.66f, 1f, 0.8f);
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
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
            rect.sizeDelta = NormalSize;

            // вью ждёт в пуле невидимым — иначе три плашки висят на экране
            // с первого кадра сцены до первого прогона через цикл фейда
            go.SetActive(false);
            group.alpha = 0f;

            return new ToastView { Go = go, Rect = rect, Label = label, Group = group };
        }

        // Н6 U4: tone — цвет текста по смыслу события; дефолт Neutral (жёлтый),
        // чужие вызовы без тона остаются валидными
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

            // истёкшие — в затухание, слот освобождается сразу
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var view = active[i];
                view.Group.alpha = Mathf.Min(1f, view.Group.alpha + dt / FadeInTime);
                if (now >= view.HideAt)
                    Retire(i);
            }

            // очередь ждёт, слотов нет — вытесняем старейший прожитый тост
            if (pending.Count > 0 && active.Count >= MaxVisible
                && now - active[active.Count - 1].ShownAt >= EvictAfter)
                Retire(active.Count - 1);

            // добор из очереди: стаггер между тостами одного кадра, аварийные — без задержки
            while (pending.Count > 0 && active.Count < MaxVisible
                && (now >= nextShowAt || pending[0].Emphasized))
            {
                Display(pending[0]);
                pending.RemoveAt(0);
            }

            // плавный сдвиг стека при появлении/уходе соседей
            for (int i = 0; i < active.Count; i++)
            {
                var view = active[i];
                float y = view.Rect.anchoredPosition.y;
                if (!Mathf.Approximately(y, view.TargetY))
                    view.Rect.anchoredPosition = new Vector2(0f, Mathf.MoveTowards(y, view.TargetY, SlideSpeed * dt));
            }

            // затухание ушедших на месте
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

            if (bound)
                return;
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
                // Н7 Q7.1/Q7.4: срыв по таймеру или разбитый груз называют причину
                string.IsNullOrEmpty(offer.FailReason)
                    ? "Заказ провален" + Prefix(PassengerProfile.PickPhrase(offer.Passenger.Complaints))
                    : "СРЫВ ЗАКАЗА: " + offer.FailReason + Prefix(PassengerProfile.PickPhrase(offer.Passenger.Complaints)),
                true, ToastTone.Danger);

            // v5.2: тост-момент — отклик в момент свершения/срыва задания.
            // Н9.5: финальные тосты TaskSucceeded/TaskFailed сняты — они
            // дублировались: момент в полёте + «ЗАКАЗ ВЫПОЛНЕН» и выплата на
            // доставке давали три плашки разом (владелец: «слишком много»)
            orders.TaskMomentSuccess += text => Show(text, false, ToastTone.Success);
            orders.TaskMomentFail += text => Show(text, true, ToastTone.Danger);

            // Н7 Q7.3: тост принятия заказа; «паникёр» предупреждает сразу (Н7 Q7.1)
            orders.OrderAccepted += offer => Show("Заказ принят: " + offer.Passenger.DisplayName
                + (offer.HurrySeconds > 0f
                    ? " · ПАССАЖИР ТОРОПИТСЯ: " + UiFormat.FormatClock(offer.HurrySeconds) + " на доставку"
                    : ""));

            // Н7 Q7.4: промежуточные предупреждения задания (повреждение посылки,
            // замечание нервному пилоту)
            orders.OrderWarning += message => Show(message, false, ToastTone.Danger);

            var landing = ship.GetComponent<LandingSystem>();
            landing.StationServiced += station => Show($"Станция «{station}»: сервис оплачен", false, ToastTone.Danger);
            landing.StationHint += planet => Show("Станция: [B] — " + StationServices(planet));
            // запас топливной сети при посадке (Н-4): сколько единиц продают и по чём
            // (с районным множителем, Н4.5); пустая сеть — своё сообщение, чтобы
            // игрок сразу знал, что слайдер тут не поможет
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
                // Н7 A-FIX: «за арендный корабль» — только на аварию аренды
                // (RegisterAccident → AccidentRegistered). Недостача штрафа идёт
                // в AddCharge (GameSession.ChargePenalty уже тостует штраф сам) —
                // без второго тоста и без ложной причины; гашение (П3.3/П4.2)
                // тоста не даёт, как и раньше
                debt.AccidentRegistered += amount =>
                    Show($"ДОЛГ +{UiFormat.Currency} {UiFormat.FormatCredits(amount)} за арендный корабль", true, ToastTone.Danger);

            // выплата за доставку (П4.2): сумма на руки + уход в долг
            if (session.Payout != null)
                session.Payout.Paid += result =>
                {
                    Show($"Доставка: +{UiFormat.Currency} {UiFormat.FormatCredits(result.Net)}", true, ToastTone.Success);
                    if (result.ToDebt > 0)
                        Show($"{UiFormat.Currency} {UiFormat.FormatCredits(result.ToDebt)} ушло в счёт долга", false, ToastTone.Danger);
                };

            // кометы (П6.3): предупреждение за 20 с до входа в систему
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

        // цвет текста по тону (Н6 U4) — палитра борткомпа: красный = списание,
        // зелёный = приход/успех, жёлтый = нейтральное
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

        // перечень услуг станции по флагам планеты (Н-3): комбинации «есть заправка»/
        // «есть мастерская», у хаба плюс магазин
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
