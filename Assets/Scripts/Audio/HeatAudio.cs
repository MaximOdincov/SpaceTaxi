using UnityEngine;
using SpaceTaxi.Physics;
using SpaceTaxi.World;

namespace SpaceTaxi.Audio
{
    // Жар звезды: тревожный гул, громче к поверхности звезды
    public sealed class HeatAudio : MonoBehaviour
    {
        private Ship ship;
        private GravityField gravity;

        private void Awake()
        {
            ship = GetComponent<Ship>();
            gravity = FindAnyObjectByType<GravityField>();
        }

        private void Update()
        {
            // жар считается только в полёте (как HullSystem.ApplyHeat) —
            // стоя на планете гул не звучит
            if (ship == null || ship.State != ShipState.Flying || gravity == null)
            {
                Sound.SetLoop(SoundId.HeatZone, false);
                return;
            }

            // глубина зоны: 0 на краю HeatRadius, 1 у поверхности — формула HullSystem
            Vector2 pos = ship.transform.position;
            bool inZone = false;
            float depth = 0f;
            foreach (CelestialBody body in gravity.Bodies)
            {
                if (!(body is Star star) || star.HeatRadius <= 0f || !star.IsInHeatZone(pos))
                    continue;
                inZone = true;
                depth = Mathf.Max(depth, star.HeatDepth(pos)); // общая формула с HullSystem
            }

            Sound.SetLoop(SoundId.HeatZone, inZone);
            if (inZone)
                Sound.SetLoopIntensity(SoundId.HeatZone, depth);
        }
    }
}
