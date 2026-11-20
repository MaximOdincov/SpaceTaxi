using UnityEngine; // коллайдеры и вращение

namespace SpaceTaxi.World
{
    [RequireComponent(typeof(CircleCollider2D))] // телу нужен круглый коллайдер
    public class CelestialBody : MonoBehaviour // базовое небесное тело
    {
        public string DisplayName = "Body"; // имя (сопоставление по всему проекту)
        public BodyType Type = BodyType.Planet; // тип тела
        public float Radius = 50f; // логический радиус
        public float Mass = 1f; // множитель массы

        // услуги станции: заправка и мастерская; хаб — ещё магазин/улучшения
        public bool HasFuel { get; private set; } // есть заправка
        public bool HasRepair { get; private set; } // есть мастерская
        public bool IsHub { get; private set; } // это хаб МТО
        // топливная сеть: тир планеты — цена/ед и запас визита из PricingConfig
        public int FuelTier { get; private set; } // тир топлива
        // сеть ремонта: тир планеты — макс. пакет и потолок визита из PricingConfig
        public int RepairTier { get; private set; } // тир ремонта
        // класс района: множитель цены топлива сети (PricingConfig) —
        // читается из данных уровня, у заказов тот же сектор считает OrderService
        public Sector Sector { get; private set; } // район системы
        // сеть станции пуста: детерминировано именем станции (не прыгает
        // между визитами); у двух соседних заправок топлива не бывает одновременно
        public bool FuelOutage { get; private set; } // топливо кончилось

        protected Physics.PhysicsConfig config; // физический баланс

        private Physics.GravityField gravityField; // реестр тел
        private float spinSpeed; // скорость вращения спрайта
        private float landingRingOverride; // переопределение посадочного кольца
        private float captureRingOverride; // переопределение кольца качелей

        public float LandingRing => // посадочное кольцо
            landingRingOverride > 0f ? landingRingOverride : Radius + (config != null ? config.LandingRingOffset : 90f); // оверрайд или формула

        public float CaptureRing => // кольцо качелей/захвата
            captureRingOverride > 0f ? captureRingOverride // оверрайд из данных
                : Radius * (config != null ? config.CaptureRingK : 3.3f) + (config != null ? config.CaptureRingBase : 30f); // или формула

        public virtual void Init(CelestialBodyData data, Physics.PhysicsConfig physicsConfig, bool fuelOutage = false) // инициализация тела
        {
            DisplayName = data.DisplayName; // имя из данных
            Type = data.Type; // тип
            // пользовательские планеты крупнее (не все): множитель из данных
            // (VisualSizeMul > 0 — ручной оверрайд редактора уровней) или по имени —
            // гравитация/кольца/посадка считают от увеличенного радиуса согласованно;
            // звёзды и дыры не растут (их зоны жара/горизонт в данных точные)
            float sizeMul = data.VisualSizeMul > 0f // ручной множитель
                ? data.VisualSizeMul
                : Core.WorldSprites.PlanetSizeMul(data.DisplayName); // или авто по имени
            Radius = data.Type == BodyType.Planet ? data.Radius * sizeMul : data.Radius; // растут только планеты
            Mass = data.MassMultiplier; // масса
            HasFuel = data.HasFuel; // услуги
            HasRepair = data.HasRepair;
            IsHub = data.IsHub;
            FuelTier = data.FuelTier;
            RepairTier = data.RepairTier;
            Sector = data.Sector;
            FuelOutage = fuelOutage; // «сухая» станция
            spinSpeed = data.Visuals != null ? data.Visuals.SpinSpeed : 0f; // вращение
            landingRingOverride = data.LandingRingRadius; // кольца из данных
            captureRingOverride = data.CaptureRingRadius;

            transform.position = data.Position; // позиция из данных

            var renderer = GetComponent<SpriteRenderer>(); // рендерер тела
            if (renderer != null && renderer.sprite != null) // спрайт назначен
            {
                // по контенту арта (альфа-границы), не по канве: видимый шар
                // должен совпадать с логическим радиусом и коллайдером
                Vector2 content = Core.SpriteArt.ContentSize(renderer.sprite); // габариты контента
                float d = Mathf.Max(content.x, content.y); // бо́льшая сторона
                if (d > 0.0001f) // контент есть
                {
                    float s = Radius * 2f / d; // масштаб под логический радиус
                    transform.localScale = new Vector3(s, s, 1f); // применить
                }
            }

            float scale = Mathf.Max(Mathf.Abs(transform.localScale.x), Mathf.Abs(transform.localScale.y), 0.0001f); // итоговый масштаб
            GetComponent<CircleCollider2D>().radius = Radius / scale; // коллайдер под радиус в масштабе

            config = physicsConfig; // баланс для формул колец
        }

        protected virtual void OnEnable() // включилось — в реестр гравитации
        {
            gravityField = FindAnyObjectByType<Physics.GravityField>(); // реестр
            if (gravityField != null) // найден
                gravityField.Register(this); // зарегистрировать тело
        }

        protected virtual void OnDisable() // выключилось — из реестра
        {
            if (gravityField != null) // реестр есть
                gravityField.Unregister(this); // разрегистрировать
        }

        private void Update() // вращение спрайта
        {
            if (!Mathf.Approximately(spinSpeed, 0f)) // скорость задана
                transform.Rotate(0f, 0f, spinSpeed * Time.deltaTime); // крутить вокруг Z
        }
    }
}
