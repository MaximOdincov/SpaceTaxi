using UnityEngine;

namespace SpaceTaxi.Physics
{
    // Единственное место расчёта посадочной позы корабля: спавн (WorldBuilder),
    // посадка (LandingSystem), респавн (HullSystem), пересадка корпуса (Ship),
    // загрузка сейва (SaveSystem). Раньше каждый считал смещение сам, часть —
    // от сырого радиуса данных уровня, а фактическая поверхность планеты
    // умножена на PlanetSizeMul (CelestialBody.Init): крупный корпус уходил
    // хвостом в планету, солвер выталкивал («провалился и отскочил»)
    public static class ShipPlacement
    {
        // отступ без визуала модели (ShipModelVisual.HalfExtent возвращает то же)
        private const float DefaultHalfExtent = 7f;

        // посадочный отступ: половина высоты капсулы (0.96 высоты контента) —
        // корабль стоит ровно в позе физического покоя, «чуть ниже» прежнего
        // снапа по канве. Глубже опускать нельзя: солвер снова выталкивает
        public static float LandedExtent(ShipModelVisual visual)
        {
            float halfExtent = visual != null ? visual.HalfExtent : DefaultHalfExtent;
            return halfExtent * ShipModelVisual.CapsuleHeightFraction;
        }

        // телепорт в позу на поверхности: одновременно тело и transform.
        // Только transform при Interpolate отдаёт 1–2 кадра старой
        // интерполированной позы (Auto Sync Transform выключен); только
        // тело — рассинхронизирует визуал. Body ещё нет (спавн) —
        // деградирует до записи transform
        public static void TeleportLanded(Ship ship, Vector2 pos, float zAngle)
        {
            if (ship == null)
                return;
            if (ship.Body != null)
            {
                ship.Body.position = pos;
                ship.Body.rotation = zAngle;
            }
            ship.transform.position = pos;
            ship.transform.rotation = Quaternion.Euler(0f, 0f, zAngle);
        }
    }
}
