using UnityEngine;
using SpaceTaxi.Physics;
using SpaceTaxi.World;

namespace SpaceTaxi.Audio
{
    // Жар звезды: тревожный гул по близости (пока отключён)
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
            // звук жара пока отключён
        }
    }
}
