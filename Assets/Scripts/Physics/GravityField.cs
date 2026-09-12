using System.Collections.Generic;
using UnityEngine;
using SpaceTaxi.World;

namespace SpaceTaxi.Physics
{
    // Единая точка расчёта притяжения: сумма a = G*M/r^2 по всем телам.
    public class GravityField : MonoBehaviour
    {
        private readonly List<CelestialBody> bodies = new List<CelestialBody>();

        public void Register(CelestialBody body)
        {
            if (body != null && !bodies.Contains(body))
                bodies.Add(body);
        }

        public void Unregister(CelestialBody body)
        {
            bodies.Remove(body);
        }

        public Vector2 GetAccelerationAt(Vector2 position)
        {
            // TODO: a += dir * G * Mass / r^2 по каждому телу
            return Vector2.zero;
        }

        private void FixedUpdate()
        {
            // TODO: применять ускорение к кораблю каждый шаг физики
        }
    }
}
