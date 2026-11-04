using UnityEngine;

namespace SpaceTaxi.World
{
    // Маркер астероида пояса: посадки нет, урон и отскок считает принимающая сторона (LandingSystem)
    public class Asteroid : MonoBehaviour
    {
        public float Radius { get; private set; }

        public void Init(float radius)
        {
            Radius = radius;
        }
    }
}
