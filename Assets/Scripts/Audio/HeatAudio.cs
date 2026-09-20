using UnityEngine;
using SpaceTaxi.Physics;
using SpaceTaxi.World;

namespace SpaceTaxi.Audio
{
    // Жар звезды: нарастающий тревожный гул по близости к ближайшей звезде.
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
            // ЗВУК ВЫКЛ (2026-09-20): жар молчит, оставлена только музыка. Вернуть к аудио-итерации.
            /*
            if (ship == null)
                return;

            Star nearest = null;
            float nearestDist = float.MaxValue;
            if (gravity != null)
            {
                Vector2 pos = ship.transform.position;
                foreach (CelestialBody body in gravity.Bodies)
                {
                    if (body is Star star)
                    {
                        float d = Vector2.Distance(star.transform.position, pos);
                        if (d < nearestDist)
                        {
                            nearest = star;
                            nearestDist = d;
                        }
                    }
                }
            }

            bool inHeat = nearest != null && nearest.IsInHeatZone(ship.transform.position);
            Sound.SetLoop(SoundId.HeatZone, inHeat);
            if (inHeat)
            {
                // Ближе к поверхности — громче и выше.
                float closeness = 1f - Mathf.Clamp01(nearestDist / nearest.HeatRadius);
                Sound.SetLoopIntensity(SoundId.HeatZone, closeness);
            }
            */
        }
    }
}
