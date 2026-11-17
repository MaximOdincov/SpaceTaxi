using UnityEngine; // базовые типы Unity
using UnityEngine.InputSystem; // ассет ввода (новый Input System)
using SpaceTaxi.Audio; // звуки системных событий
using SpaceTaxi.Economy; // сервисы денег
using SpaceTaxi.Orders; // заказы пассажиров
using SpaceTaxi.Physics; // корабль и физика
using SpaceTaxi.UI; // тосты и модал выплаты
using SpaceTaxi.World; // построение мира

namespace SpaceTaxi.Core // корневой слой: сборка игры
{
    // Композиционный корень: создаёт все сервисы, строит мир и соединяет системы подписками
    public class GameSession : MonoBehaviour
    {
        [SerializeField] private LevelAsset level; // ассет уровня (карта тел) сцены
        [SerializeField] private PhysicsConfig physicsConfig; // баланс физики сцены
        [SerializeField] private ShipConfig currentShip; // устаревшее поле: корабль живёт во Fleet
        [SerializeField] private UpgradeConfig upgradeConfig; // ассет улучшений (фолбэк из Resources)
        [SerializeField] private InputActionAsset input; // ассет ввода для контроллеров вне сцены
        [SerializeField] private PassengerCatalog passengerCatalog; // каталог пассажиров для заказа

        [SerializeField] private Planet planetPrefab; // префаб планеты для WorldBuilder
        [SerializeField] private Ship shipPrefab; // префаб корабля для WorldBuilder

        public Ship Ship { get; private set; } // текущий борт игрока
        public OrderService Orders { get; private set; } // домен заказов
        public Wallet Wallet { get; private set; } // кошелёк — единственная точка списания
        public FleetService Fleet { get; private set; } // парк кораблей и апгрейды
        public DebtService Debt { get; private set; } // долг и увольнение
        public PayoutService Payout { get; private set; } // выплата за доставку
        public RatingService Rating { get; private set; } // рейтинг таксиста
        public LevelAsset Level => level; // доступ биндеров к данным уровня
        public PhysicsConfig Config => physicsConfig; // доступ систем к балансу физики
        // ассет ввода сцены: контроллерам вне сцены (экран станции строится кодом)
        public InputActionAsset Input => input;
        // каталог пассажиров: восстановление пассажира активного заказа из сейва (П10.1)
        public PassengerCatalog Passengers => passengerCatalog;
        // текущий корабль теперь живёт в парке; поле сцены currentShip не читается
        public ShipConfig CurrentShip => Fleet.Current;

        private bool accidentRegistered; // один инцидент = один долг
        private bool passengerAboardAtLoss; // пассажир был на борту при потере корабля (Н7 Q7.5)
        private OrderCompleteScreenController orderComplete; // спавнится сам (стиль Dismissal)

        private void Awake() // ранняя сборка чистых сервисов (до Start мира)
        {
            Wallet = new Wallet(); // кошелёк с нуля
            // новая игра: арендное «Яйцо», своих кораблей нет
            Fleet = new FleetService(); // парк: одна аренда в списке
            // улучшения: ассет сцены → ассет из Resources (инспектор правит числа)
            // → кодовый инстанс с числами плана (никаких NRE)
            UpgradeConfig upgrades = upgradeConfig; // локальная переменная для цепочки фолбэка
            if (upgrades == null) // поле сцены пусто — идём в Resources
                upgrades = Resources.Load<UpgradeConfig>("UpgradeConfig_Default"); // второй уровень цепочки
            Fleet.BindUpgrades(upgrades != null ? upgrades : UpgradeConfig.Default); // третий — кодовый дефолт
            Debt = new DebtService(); // долг до первого инцидента нулевой
            Payout = new PayoutService(); // выплаты за доставку
            Payout.Bind(Wallet, Debt); // выплате нужны кошелёк и долг
            Rating = new RatingService(); // рейтинг с константами дельт
        }

        private void Start() // сборка мира и связей после Awake-сервисов
        {
            var builder = new WorldBuilder(level, physicsConfig, Fleet.Current, input, planetPrefab, shipPrefab); // фабрика мира с данными
            builder.Build(out Ship ship, out _); // строит тела и корабль
            Ship = ship; // запоминаем борт для всех подписчиков

            Fleet.BindShip(ship); // парк узнаёт текущий корпус
            var hull = ship.GetComponent<HullSystem>(); // система корпуса борта
            // гибель/потеря корабля → долг за аренду + возврат парка на аренду
            // (ещё один подписчик тех же событий, что и OrderService)
            hull.Died += OnShipLost; // гибель = потеря борта для экономики
            hull.ShipLost += _ => OnShipLost(); // потеря без взрыва (дыра/списание)
            // долг и замена корпуса — ПОСЛЕ респавна: подмена спрайта в момент
            // удара выглядела как мгновенная пересадка в аренду (жалоба плейтеста)
            hull.Respawned += OnRespawned; // списания и смена корпуса на респавне

            var follow = Camera.main.gameObject.AddComponent<CameraFollow>(); // камера следит за кораблём
            follow.SetTarget(Ship); // цель слежения — борт

            var camGo = Camera.main.gameObject; // объект камеры для параллакса
            if (camGo.GetComponent<World.StarfieldParallax>() == null) // не добавлять второй слой
                camGo.AddComponent<World.StarfieldParallax>(); // фоновые звёзды

            Orders = gameObject.AddComponent<OrderService>(); // домен заказов — компонент сессии
            Orders.Init(level, passengerCatalog, Ship); // initData: уровень, пассажиры, борт
            Orders.Bind(Rating); // заказам нужен рейтинг
            // Н7 Q7.8: порог увольнения по сумме долга — константа прайс-конфига
            // (те же числа, что считают цену карточек); DebtService проверяет
            // его после каждого роста долга
            Debt.ConfigureDismissalDebtThreshold(Orders.Pricing.DebtDismissalThreshold); // порог из прайса
            // доставка платит (16_BETA_PLAN §3.5): цена с карточки, штраф по
            // столкновениям заказа, комиссия на аренде; оффер приходит в аргументе
            // события (Active уже обнулён) — отдельный доступ не нужен
            Orders.OrderDelivered += OnOrderDelivered; // обработчик выплаты
            // дельты рейтинга (§3.7): отказ и гибель; доставка — внутри OnOrderDelivered.
            // Отказ от заказа, пока пассажир не на борту, — без штрафа рейтингу;
            // отмена оператором — деньгами: 25% цены (ChargeCancelPenalty)
            Orders.OrderCancelled += offer => // отмена заказа оператором
            {
                if (offer.State == SpaceTaxi.Orders.OrderState.Aboard) // пассажир уже сидел
                    Rating.FailNoDeath(); // минус рейтинг за высадку
                ChargeCancelPenalty(offer); // и 25% цены деньгами
            };
            Orders.OrderFailed += _ => Rating.FailWithLoss(); // провал заказа бьёт рейтинг
            // столкновение при активном заказе — минус рейтинг. Рейтинг слушает
            // CrashImpact напрямую; штраф выплаты считает счётчик OrderService —
            // два независимых потребителя одного события
            ship.GetComponent<LandingSystem>().CrashImpact += OnCrashImpact; // удар с уроном

            // звуковые хуки заказов/экономики (П9.3): системные события, не UI.
            // «новый заказ» — реюз ComputerPing (отдельный OrderPing не заводили)
            Orders.OfferAdded += () => Sound.Play(SoundId.ComputerPing); // новая карточка
            Orders.PassengerBoarded += _ => Sound.Play(SoundId.BoardBip); // пассажир сел
            Orders.OrderDelivered += _ => Sound.Play(SoundId.DeliveryChime); // заказ сдан
            Orders.TaskSucceeded += _ => Sound.Play(SoundId.TaskSuccess); // задание выполнено
            Orders.TaskFailed += _ => Sound.Play(SoundId.TaskFail); // задание провалено

            // продолжение забега (П10.1): мир построен по умолчанию — поверх
            // применяется состояние сейва (без флага «продолжить» — тихий выход)
            SaveSystem.ApplyTo(this); // восстановить кошелёк/флот/заказ из сейва

            // Н-8: waypoint с карты в мире (скобка-луч+подпись) и краевые стрелки
            // (waypoint янтарный, цель заказа зелёная)
            gameObject.AddComponent<WaypointIndicator>(); // маркеры в мире

            // тон долга — только на рост (авария); гашения тонов не дают.
            // ПОСЛЕ ApplyTo: иначе восстановленный из сейва долг сыграл бы
            // ложный «аварийный» тон при каждом Continue
            int prevDebt = Debt.Balance; // стартовая точка сравнения
            Debt.DebtChanged += value => // следим за ростом долга
            {
                if (value > prevDebt) // только рост, не гашение
                    Sound.Play(SoundId.DebtTone); // тревожный тон
                prevDebt = value; // запоминаем новое значение
            };
        }

        private void OnOrderDelivered(OrderOffer offer) // выплата за сданный заказ
        {
            // сначала дельта рейтинга — экран завершения показывает её же
            Rating.AddDelivery(); // плюс к рейтингу за доставку
            // выплата: тосты уходят синхронно внутри Paid, экран открываем после них.
            // Чаевые (Н-14): случайная доля 10–25%, брошенная при спавне карточки;
            // перк «Меридианы» домножается в выплате.
            // v5.3 (владелец: «мимо бездны стоит очень хорошо»): бонус задания —
            // сложение с ценой (строка «Перевозка» на экране завершения)
            int taskBonus = offer.Task != null && offer.Task.Succeeded // задание было и выполнено
                && offer.Task.Kind == PassengerTaskKind.HoleFlyby // только «мимо бездны»
                    ? PassengerTasks.HoleFlybyBonus // +150 к выплате
                    : 0; // без бонуса
            Payout.Pay(Orders.Pricing, offer.Price + taskBonus, Fleet.Current.IsRental, // выплата с ценой+бонусом
                Orders.CollisionsOfActive, offer.TipsFraction, IsMeridian); // штрафы/комиссия/чаевые/перк
            // автосейв §3.9 (П10.1): сразу после выплаты — счётчики/рейтинг/долг
            // уже консистентны, экран завершения данных не меняет
            SaveSystem.Save(this); // сохранить состояние
            if (orderComplete == null) // экран ещё не искали
                orderComplete = FindAnyObjectByType<OrderCompleteScreenController>(); // ленивый поиск
            orderComplete?.Show(offer); // показать модал выплаты
        }

        // перк «комфорт-салон» (16 §3.3) — только у «Меридиана»
        private bool IsMeridian => // признак корабля с перком
            Fleet.Current != null && Fleet.Current.Id == PayoutService.MeridianShipId; // совпадение id

        private void OnCrashImpact() // удар с уроном при активном заказе
        {
            if (Orders != null && Orders.Active != null) // заказ активен
                Rating.AddCollisionPenalty(); // минус рейтинга
        }

        // единый путь штрафов (Н7 Q7.5): сколько есть — с кошелька (Wallet.TrySpend —
        // единственная точка списания), недостача — в долг (DebtService.AddCharge —
        // не авария, счётчик аварий не растёт); тон тостов — Danger (Н6 U4)
        private void ChargePenalty(int penalty, string reason, bool emphasized = false) // штраф деньгами
        {
            if (penalty <= 0) // нечего списывать
                return;
            int fromWallet = Mathf.Min(penalty, Wallet.Credits); // сколько реально в кошельке
            if (fromWallet > 0) // часть есть
                Wallet.TrySpend(fromWallet); // списать с кошелька
            int toDebt = penalty - fromWallet; // недостача
            if (toDebt > 0) // кошелёк не покрыл
                Debt.AddCharge(toDebt); // остаток в долг (не авария)
            FindAnyObjectByType<UI.ToastNotifier>()?.Show( // тост о штрафе
                $"{reason}: штраф −{UI.StationUI.Currency} {UI.OnboardBinder.FormatCredits(penalty)}", // текст суммы
                emphasized, UI.ToastTone.Danger); // красный тон
            Debug.Log($"[Economy] {reason}: {penalty} Đ (кошелёк {fromWallet}, долг {toDebt})"); // журнал
        }

        // штраф за отмену заказа оператором: 25% цены, округление вниз.
        // Рейтинг-штраф за высадку пассажира уже в OrderCancelled-хендлере выше
        private void ChargeCancelPenalty(OrderOffer offer) // 25% цены за отмену
        {
            ChargePenalty(OrderService.CancelPenalty(offer.Price), "Отказ от заказа"); // единый путь штрафов
        }

        // потеря корабля: арендный — долг за аварию. Проверка владения ДО
        // NotifyShipLost: тот списывает корабль и пересаживает на аренду.
        // Died и ShipLost при одном инциденте не должны сработать оба — guard
        // как у OrderService.FailActive, снимается на респавне
        private void OnShipLost() // ловим состояние на момент потери
        {
            // Died и ShipLost при одном инциденте не должны сработать оба — guard
            if (accidentRegistered) // инцидент уже учтён
                return;
            accidentRegistered = true; // фиксируем инцидент
            // пассажир на борту в момент гибели/отключения корпуса: FailActive
            // (OrderService) подписан позже и обнулит Active — состояние
            // ловим только здесь (Н7 Q7.5)
            passengerAboardAtLoss = Orders != null && Orders.Active != null // заказ был
                && Orders.Active.State == SpaceTaxi.Orders.OrderState.Aboard; // пассажир сидел
        }

        private void OnRespawned() // списания после респавна
        {
            if (!accidentRegistered) // инцидента не было
                return;
            if (!Fleet.IsOwned(Fleet.Current)) // борт был арендный
                Debt.RegisterAccident(); // долг за аварию аренды
            // пассажир погиб или оставлен вместе с корпусом — штраф сверх долга
            // за аренду, на своём корабле тоже (Н7 Q7.5); списание после
            // респавна — как и долг за аренду, чтобы тосты не сыпались в момент взрыва
            if (passengerAboardAtLoss) // пассажир был на борту
                ChargePenalty(PenaltyPricing.PassengerLossPenalty, // штраф за пассажира
                    "Пассажир погиб вместе с кораблём", emphasized: true); // крупный красный тост
            Fleet.NotifyShipLost(); // парк списывает борт и пересаживает на аренду
            accidentRegistered = false; // следующая потеря — новый инцидент
            passengerAboardAtLoss = false; // сброс флага пассажира
        }

        // прайс-конфиг той же цепочки, что считает цену заказа (Orders.Pricing):
        // поле сцены → ассет из Resources → кодовый дефолт, null не бывает
        private PricingConfig PenaltyPricing => Orders != null ? Orders.Pricing : PricingConfig.Default; // фолбэк-прайс
    }
}
