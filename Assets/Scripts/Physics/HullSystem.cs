using System; // события Action
using System.Collections.Generic; // список скрытых рендереров
using UnityEngine; // MonoBehaviour
using SpaceTaxi.Audio; // звук взрыва/респавна
using SpaceTaxi.Economy; // прайс и данные
using SpaceTaxi.World; // звёзды/тела/уровень

namespace SpaceTaxi.Physics
{
    // Прочность: урон от столкновений и жара, гибель и респавн
    public class HullSystem : MonoBehaviour // корпус корабля
    {
        private Ship ship; // модель борта
        private GravityField gravity; // реестр тел (для жара)
        private LevelAsset level; // уровень (радиус мира, стартовая планета)
        private Vector3 initialScale = Vector3.one; // респавн откатывает сжатие дырой
        private ShipModelVisual modelVisual; // визуал модели
        // рендереры, спрятанные на «мёртвую» паузу до респавна: возвращаем ровно их,
        // чтобы не воскресить корневой спрайт под визуалом модели (двойной корпус)
        private readonly List<SpriteRenderer> hiddenVisuals = new List<SpriteRenderer>();
        // палитра взрыва: копия OnboardComputerFactory.DangerColor — UI-неймспейс
        // в физику не тащим, цвет уходит параметром в ShipExplosion.Spawn
        private static readonly Color DangerTint = new Color(1f, 0.32f, 0.24f); // красный взрыва

        // лениво с кэшем: ShipModelVisual добавляется позже Awake (WorldBuilder)
        private ShipModelVisual Visual => modelVisual != null // кэш или поиск
            ? modelVisual
            : (modelVisual = GetComponent<ShipModelVisual>());

        public event Action HpChanged; // корпус изменился (HUD)
        public event Action Died; // корабль погиб
        public event Action<string> ShipLost; // потерян без взрыва (причина)
        public event Action Respawned; // респавн завершён

        public void Init(LevelAsset levelAsset) // уровень для радиуса мира
        {
            level = levelAsset;
        }

        private void OnShipReconfigured() // корпус сменился — HUD обновить
        {
            HpChanged?.Invoke();
        }

        private void Awake() // бинд соседей по кораблю
        {
            ship = GetComponent<Ship>(); // модель
            modelVisual = GetComponent<ShipModelVisual>(); // визуал
            initialScale = transform.localScale; // исходный масштаб
            // пересадка на другой корпус — HP стал полным, HUD должен увидеть
            ship.Reconfigured += OnShipReconfigured; // подписка на смену корпуса
            gravity = FindAnyObjectByType<GravityField>(); // реестр тел
            if (level == null) // уровень не передан
            {
                var session = FindAnyObjectByType<SpaceTaxi.Core.GameSession>(); // фолбэк через сессию
                if (session != null && session.Level != null) // сессия есть
                    level = session.Level; // взять её уровень
            }
        }

        private void Update() // выход за границу мира
        {
            if (ship == null || ship.State != ShipState.Flying) // не в полёте
                return;
            float worldRadius = level != null ? level.WorldRadius : 2500f; // радиус мира
            if (ship.transform.position.magnitude > worldRadius) // слишком далеко
                Abandon("ПОТЕРЯНА СВЯЗЬ"); // потеря борта
        }

        // потеря корабля без взрыва
        public void Abandon(string reason) // списать борт без взрыва
        {
            if (ship == null || ship.State == ShipState.Respawning) // уже респавнится
                return;
            ShipLost?.Invoke(reason); // сообщить причину
            ship.SetState(ShipState.Respawning); // окно респавна
            ship.Body.linearVelocity = Vector2.zero; // стоп
            // старый корпус прячется мгновенно: до телепорта на месте потери
            // остаётся только обломок
            HideVisuals(); // старый корпус исчезает сразу
            StartCoroutine(RespawnAfter(0.6f)); // короткая пауза
        }

        private void FixedUpdate() // жар только в полёте
        {
            if (ship == null || ship.ShipConfig == null || ship.State != ShipState.Flying) // не летит
                return;
            ApplyHeat(Time.fixedDeltaTime); // урон жаром за шаг
        }

        public void ApplyDamage(float amount) // урон корпуса
        {
            if (amount <= 0f || ship == null || ship.Hp <= 0f) // нечего наносить
                return;
            ship.SetHp(ship.Hp - amount); // вычесть (кламп в SetHp)
            HpChanged?.Invoke(); // HUD обновить
            if (ship.Hp <= 0f) // корпус кончился
                Die();
        }

        // через событие HpChanged, иначе HUD не обновится
        public void RepairAll() // полный ремонт
        {
            if (ship == null || ship.ShipConfig == null) // корабля нет
                return;
            Repair(ship.Eff.HpMax); // до максимума
        }

        // частичный ремонт (экран станции) — тоже через HpChanged
        public void Repair(float amount) // частичный ремонт
        {
            if (amount <= 0f || ship == null || ship.ShipConfig == null) // нечего чинить
                return;
            ship.SetHp(ship.Hp + amount); // починить (кламп в SetHp)
            HpChanged?.Invoke(); // HUD
        }

        // жар: линейно от 0 на краю зоны до максимума у поверхности
        private void ApplyHeat(float dt) // урон жаром звёзд
        {
            if (gravity == null) // реестра нет
                return;
            Vector2 pos = ship.transform.position; // позиция корабля
            foreach (CelestialBody body in gravity.Bodies) // по телам
            {
                if (!(body is Star star) || star.HeatRadius <= 0f) // не звезда без жара
                    continue;
                float d = Vector2.Distance(star.transform.position, pos); // дистанция до звезды
                if (d >= star.HeatRadius) // вне зоны жара
                    continue;
                float k = star.HeatDepth(pos); // глубина зоны — общая формула с HeatAudio
                ApplyDamage(k * star.HeatDamagePerSecond * dt * ship.Eff.HeatMul); // жар-защита корпуса
            }
        }

        private void Die() // гибель корабля
        {
            if (ship.State == ShipState.Respawning) // уже респавнится
                return;
            Died?.Invoke(); // событие гибели
            Sound.Play(SoundId.Explosion); // дакнет остальные группы в AudioManager
            CameraFollow.AddShake(0.85f); // гибель — самый сильный толчок
            // взрыв в точке корабля до телепорта респавна: вспышка закрывает корпус,
            // сам корпус на «мёртвую» паузу прячем — видны только частицы (0.6 c)
            ShipExplosion.Spawn(transform.position, Visual != null ? Visual.HalfExtent : 0f, DangerTint); // взрыв
            HideVisuals(); // спрятать корпус
            ship.SetState(ShipState.Respawning); // окно респавна
            ship.Body.linearVelocity = Vector2.zero; // стоп
            StartCoroutine(RespawnAfter(0.6f)); // короткая пауза на взрыв
        }

        // спрятать корпус на время между смертью и респавном (взрыв вместо него);
        // кэшируем только включённые рендереры — выключенные сами вернутся как были
        private void HideVisuals() // спрятать все рендереры
        {
            hiddenVisuals.Clear(); // очистить прошлый список
            foreach (SpriteRenderer renderer in GetComponentsInChildren<SpriteRenderer>(true)) // все рендереры
            {
                if (!renderer.enabled) // уже выключен — не трогаем
                    continue;
                renderer.enabled = false; // выключить
                hiddenVisuals.Add(renderer); // запомнить для возврата
            }
        }

        private void RestoreVisuals() // вернуть спрятанное
        {
            foreach (SpriteRenderer renderer in hiddenVisuals) // ровно сохранённые
                if (renderer != null) // ещё живы
                    renderer.enabled = true;
            hiddenVisuals.Clear(); // список пуст
        }

        private System.Collections.IEnumerator RespawnAfter(float delay) // идемпотентный респавн
        {
            yield return new WaitForSeconds(delay); // пауза на взрыв/потерю

            // планету ищем до телепорта: она же пойдёт в LandedOn
            Planet respawnPlanet = FindAnyObjectByType<GravityField>() != null ? LandedPlanet() : null; // стартовая планета

            if (level != null && level.StartPlanetIndex >= 0 && level.StartPlanetIndex < level.Bodies.Count) // старт определён
            {
                CelestialBodyData start = level.Bodies[level.StartPlanetIndex]; // данные стартовой
                float rad = level.SpawnAngleDeg * Mathf.Deg2Rad; // угол спавна
                Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)); // направление от центра
                // радиус живой планеты, а не сырых данных: облик планеты умножен
                // на PlanetSizeMul (CelestialBody.Init), респавн от start.Radius
                // оставлял корпус хвостом в планете; отступ — как у посадки (ShipPlacement)
                Vector2 center = respawnPlanet != null ? (Vector2)respawnPlanet.transform.position : start.Position; // центр планеты
                float radius = respawnPlanet != null ? respawnPlanet.Radius : start.Radius; // живой радиус
                Vector2 pos = center + dir * (radius + ShipPlacement.LandedExtent(Visual)); // точка спавна
                float nose = GetComponent<ShipController>() != null ? GetComponent<ShipController>().SpriteNoseAngle : 0f; // угол носа
                ShipPlacement.TeleportLanded(ship, pos, level.SpawnAngleDeg - nose); // телепорт в посадочную позу
            }

            ship.Body.linearVelocity = Vector2.zero; // стоп
            ship.SetHp(ship.Eff.HpMax); // полный корпус
            ship.Fuel = ship.Eff.FuelMax; // полный бак
            ship.PoweredDown = false; // новый борт — не заглушенный
            transform.localScale = initialScale; // сжатие дырой не переносится на новый корпус
            ship.LandedOn = respawnPlanet; // стоит на стартовой
            ship.SetState(ShipState.Landed); // состояние «стоит»
            // корпус невидим, пока камера летит от места смерти к планете старта;
            // появляется в момент прилёта (кап 2.5с — на случай застрявшей камеры)
            float waited = 0f; // сколько ждём прилёта
            Camera cam = Camera.main; // камера слежения
            while (waited < 2.5f)
            {
                yield return null; // кадр ожидания
                waited += Time.unscaledDeltaTime; // кап и на паузе модалов
                if (cam != null && Vector2.Distance(cam.transform.position, ship.transform.position)
                    <= cam.orthographicSize * 1.5f) // камера долетела
                    break;
            }
            RestoreVisuals(); // корпус материализуется под прилетевшей камерой
            HpChanged?.Invoke(); // HUD корпус
            Respawned?.Invoke(); // событие респавна
            Sound.Play(SoundId.Respawn); // звук
        }

        private Planet LandedPlanet() // стартовая планета по имени
        {
            if (level == null || level.StartPlanetIndex >= level.Bodies.Count) // данных нет
                return null;
            foreach (Planet planet in FindObjectsByType<Planet>()) // по построенным планетам
                if (planet.name == level.Bodies[level.StartPlanetIndex].DisplayName) // имя совпало
                    return planet;
            return null;
        }
    }
}
