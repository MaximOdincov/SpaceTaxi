using System; // события Action
using UnityEngine; // MonoBehaviour
using SpaceTaxi.Core; // StubSprites
using SpaceTaxi.Economy; // конфиги статов

namespace SpaceTaxi.Physics
{
    public enum ShipState // состояния корабля
    {
        Flying = 0, // в полёте
        Landed = 1, // стоит на планете
        Respawning = 2, // окно респавна после гибели
        Swallowed = 3, // затянуло за горизонт дыры (Н-10): болтается без управления до отключения
    }

    [RequireComponent(typeof(Rigidbody2D))] // физическое тело обязательно
    public class Ship : MonoBehaviour // модель борта: состояние + статы + бак
    {
        public ShipState State = ShipState.Flying; // текущее состояние (публичное — инварианты на дисциплине)
        public float Hp = 100f; // корпус

        // v5.1: борт заглушен оператором в захвате чёрной дыры (BlackHole.
        // RequestAbandon) — двигатели молчат (ShipController), воронка дотягивает
        // до конца окна; сбрасывается при респавне
        public bool PoweredDown { get; set; } // флаг заглушенного борта

        public PhysicsConfig Config { get; private set; } // физический баланс
        public ShipConfig ShipConfig { get; private set; } // модель корпуса
        public Rigidbody2D Body { get; private set; } // физическое тело
        public World.Planet LandedOn { get; set; } // планета стоянки

        // уровни улучшений [Engine, Tank, Hull, Maneuver], 0..3; 0 = сток
        public int[] UpgradeLevels { get; private set; }

        // покраска корпуса: 0 = заводская, 1..N — PaintVariants (Н-3а);
        // переживает Reconfigure (улучшения/респавн не перекрашивают)
        public int PaintIndex { get; private set; }

        // эффективные статы (база + улучшения) — единственный источник чисел
        // для полёта и корпуса; пересчитывается в Init/Reconfigure/BindUpgradeConfig
        public EffectiveStats Eff { get; private set; } // пересчитанные характеристики

        private UpgradeConfig upgradeConfig; // конфиг улучшений

        private float fuel = 100f; // текущее топливо

        public float Fuel // свойство бака с клампом и событием
        {
            get => fuel;
            set
            {
                float clamped = Mathf.Clamp(value, 0f, ShipConfig != null ? Eff.FuelMax : 100f); // кламп к максимуму
                if (Mathf.Approximately(clamped, fuel)) // не изменилось
                    return;
                fuel = clamped; // применить
                FuelChanged?.Invoke(); // сообщить подписчикам
            }
        }

        public event Action FuelChanged; // бак изменился

        // смена корпуса на живом корабле (FleetService) — HullSystem разошлёт HpChanged
        public event Action Reconfigured; // корпус/статы пересчитаны

        public void Init(PhysicsConfig config, ShipConfig shipConfig) // первичная настройка
        {
            Config = config; // физический баланс
            ShipConfig = shipConfig; // модель
            Body = GetComponent<Rigidbody2D>(); // физическое тело
            Body.gravityScale = 0f; // притяжение считает GravityField

            UpgradeLevels = ShipStats.NormalizeLevels(null); // стоковые уровни
            RecomputeEff(); // пересчёт статов
            Hp = Eff.HpMax; // полный корпус
            Fuel = Eff.FuelMax; // полный бак
        }

        // конфиг улучшений привязывается после постройки корабля (FleetService.BindShip)
        public void BindUpgradeConfig(UpgradeConfig config) // привязка конфига апгрейдов
        {
            if (config == null || config == upgradeConfig) // пустой или тот же
                return;
            upgradeConfig = config; // запомнить
            RecomputeEff(); // статы изменились
        }

        // стоковый корпус (старый вызов): уровни обнуляются
        public void Reconfigure(ShipConfig shipConfig) // смена корпуса без уровней
        {
            Reconfigure(shipConfig, null);
        }

        // Новый корпус = полные HP и полный бак (refill); остальное (тяга, MaxV)
        // читается из Eff динамически контроллерами, респавн тоже берёт новые статы.
        // refill = false — пересчёт статов без лечения/заправки: покупка улучшения
        // на стоящий корабль не должна быть халявным сервисом станции
        public void Reconfigure(ShipConfig shipConfig, int[] upgradeLevels, bool refill = true) // смена корпуса/статов
        {
            if (shipConfig == null) // пустой конфиг
                return;
            ShipConfig = shipConfig; // новая модель
            UpgradeLevels = ShipStats.NormalizeLevels(upgradeLevels); // нормализация уровней
            RecomputeEff(); // пересчёт Eff
            ApplyModelSprite(shipConfig); // визуал новой модели
            // Полные HP/бак только живому корпусу: при гибели Reconfigure
            // вызывается до респавна, и мёртвый корабль не должен «ожить»
            // на месте гибели (иначе Crash усыплёт его посадкой)
            if (refill && Hp > 0f) // живой корпус и разрешено
            {
                Hp = Eff.HpMax; // полный корпус
                Fuel = Eff.FuelMax; // полный бак — сеттер сам шлёт FuelChanged
            }
            Reconfigured?.Invoke(); // подписчики узнали
        }

        // Спрайт покраски (Н-3а): вариант или заводская — решает GetPaintSprite.
        // Перекрашивает текущий корпус; смена модели проходит через Reconfigure
        public void SetPaint(int paintIndex) // сменить покраску
        {
            if (paintIndex == PaintIndex || ShipConfig == null) // та же или модели нет
                return;
            PaintIndex = paintIndex; // запомнить индекс
            ApplyModelSprite(ShipConfig); // применить визуал
        }

        // спрайт модели (арт А1/А2): визуал-префаб модели (ShipModelVisual) или
        // корневой SpriteRenderer; чужой корпус оставлять нельзя (пересадка
        // должна быть видна)
        private ShipModelVisual modelVisual; // кэш визуала модели

        private void ApplyModelSprite(ShipConfig shipConfig) // применить визуал корпуса
        {
            if (modelVisual == null) // визуал ещё не кэширован
                modelVisual = GetComponent<ShipModelVisual>();
            if (modelVisual != null) // есть визуал-префаб
            {
                modelVisual.Apply(shipConfig, PaintIndex); // применить модель+покраску
                // корпус другой — габарит другой: на стоянке переставить на
                // поверхность в позу покоя капсулы (ShipPlacement), иначе новый
                // корпус «заползает» в планету
                if (State == ShipState.Landed && LandedOn != null) // стоим на планете
                {
                    Vector2 dir = ((Vector2)transform.position - (Vector2)LandedOn.transform.position).normalized; // нормаль поверхности
                    Vector2 pos = (Vector2)LandedOn.transform.position // позиция на поверхности
                        + dir * (LandedOn.Radius + ShipPlacement.LandedExtent(modelVisual)); // радиус + габарит
                    ShipPlacement.TeleportLanded(this, pos, transform.eulerAngles.z); // переставить в позу покоя
                }
                return;
            }
            var spriteRenderer = GetComponent<SpriteRenderer>(); // фолбэк — корневой рендерер
            if (spriteRenderer == null) // рендерера нет
                return;
            Sprite sprite = shipConfig.GetPaintSprite(PaintIndex); // спрайт покраски
            spriteRenderer.sprite = sprite != null ? sprite : StubSprites.ShipTriangle(); // арт или треугольник
        }

        // применить визуал текущей модели (WorldBuilder после Init)
        public void ApplyModelVisual() // построить визуал после сборки
        {
            if (ShipConfig != null) // модель назначена
                ApplyModelSprite(ShipConfig);
        }

        private void RecomputeEff() // пересчёт эффективных статов
        {
            Eff = ShipStats.Compute(Config, ShipConfig, UpgradeLevels, upgradeConfig); // чистая функция статов
        }

        // Для HUD.
        public float HpFraction => Eff.HpMax > 0f ? Hp / Eff.HpMax : 0f; // доля корпуса
        public float FuelFraction => Eff.FuelMax > 0f ? Fuel / Eff.FuelMax : 0f; // доля бака
    }
}
