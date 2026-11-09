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
    // Тосты событий заказов и корабля: вертикальный стек до 3 с шагом,
    // очередь со стаггером и дедупом, аварийные (emphasized) поверх очереди
    public class ToastNotifier : MonoBehaviour
    {
        [Tooltip("Сколько секунд висит обычный тост")]
        public float duration = 3.5f;

        private const int MaxVisible = 3;        // слотов в стеке; старейший вытесняется
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

        private static readonly Vector2 NormalSize = new Vector2(700f, 44f);
        private static readonly Vector2 EmphasizedSize = new Vector2(900f, 58f);

        private readonly struct Item
        {
            public readonly string Message;
            public readonly bool Emphasized;

            public Item(string message, bool emphasized)
            {
                Message = message;
                Emphasized = emphasized;
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

        public void Show(string message, bool emphasized = false)
        {
            if (string.IsNullOrEmpty(message) || IsDuplicate(message))
                return;
            var item = new Item(message, emphasized);
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
                + Prefix(PassengerProfile.PickPhrase(offer.Passenger.Thanks)), true);
            orders.OrderFailed += offer => Show("Заказ провален"
                + Prefix(PassengerProfile.PickPhrase(offer.Passenger.Complaints)), true);

            // итог задания пассажира (П5.3): благодарность или жалоба
            orders.TaskSucceeded += offer => Show("Задание выполнено"
                + Prefix(PassengerProfile.PickPhrase(offer.Passenger.Thanks)));
            orders.TaskFailed += offer => Show("Жалоба пассажира"
                + Prefix(PassengerProfile.PickPhrase(offer.Passenger.Complaints)), true);

            var landing = ship.GetComponent<LandingSystem>();
            landing.StationServiced += station => Show($"Станция «{station}»: сервис оплачен");
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
                     $"{FuelNetwork.PricePerUnit(planet.FuelTier, planet.Sector, pricing):0.##} кр/ед");
            };

            var hull = ship.GetComponent<HullSystem>();
            hull.Respawned += () => Show("Респавн на планете старта");
            hull.ShipLost += reason => Show("КОРАБЛЬ ПОТЕРЯН — " + reason);

            var debt = session.Debt;
            if (debt != null)
            {
                int prevBalance = debt.Balance;
                // рост долга — новая авария; гашение (П3.3/П4.2) тоста не даёт
                debt.DebtChanged += value =>
                {
                    if (value > prevBalance)
                        Show($"ДОЛГ +{value - prevBalance} кр за арендный корабль", true);
                    prevBalance = value;
                };
            }

            // выплата за доставку (П4.2): сумма на руки + уход в долг
            if (session.Payout != null)
                session.Payout.Paid += result =>
                {
                    Show($"Доставка: +{result.Net} кр", true);
                    if (result.ToDebt > 0)
                        Show($"{result.ToDebt} кр ушло в счёт долга");
                };

            // кометы (П6.3): предупреждение за 20 с до входа в систему
            var comets = FindAnyObjectByType<CometSystem>();
            if (comets != null)
                comets.CometIncoming += (entry, direction) => Show("Комета приближается!", true);

            bound = true;
        }

        private void Display(Item item)
        {
            if (pool.Count == 0)
                pool.Add(CreateView()); // страховка, слот обязан быть свободен
            var view = pool[pool.Count - 1];
            pool.RemoveAt(pool.Count - 1);

            view.Label.text = item.Message;
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

        private void Retire(int index)
        {
            fading.Add(active[index]);
            active.RemoveAt(index);
            Relayout();
        }

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

        private static string Prefix(string phrase)
        {
            return string.IsNullOrEmpty(phrase) ? "" : " — «" + phrase + "»";
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
