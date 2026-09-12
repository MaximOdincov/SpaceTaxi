using System.Collections.Generic;
using UnityEngine;
using SpaceTaxi.World;

namespace SpaceTaxi.Physics
{
    /// <summary>
    /// Single point of gravity computation: sums inverse-square accelerations
    /// from all registered celestial bodies and applies them to the ship in
    /// FixedUpdate. Unity's own gravity stays disabled on all rigidbodies.
    /// </summary>
    public class GravityField : MonoBehaviour
    {
        private readonly List<CelestialBody> bodies = new List<CelestialBody>();

        /// <summary>Registers a celestial body so it starts attracting.</summary>
        public void Register(CelestialBody body)
        {
            if (body != null && !bodies.Contains(body))
            {
                bodies.Add(body);
            }
        }

        /// <summary>Unregisters a body (destroyed or disabled).</summary>
        public void Unregister(CelestialBody body)
        {
            bodies.Remove(body);
        }

        /// <summary>Number of currently registered bodies.</summary>
        public int BodyCount => bodies.Count;

        /// <summary>
        /// Total gravity acceleration at a point: the sum of G * M / r^2
        /// directed towards every registered body.
        /// </summary>
        public Vector2 GetAccelerationAt(Vector2 position)
        {
            // TODO (physics iteration): inverse-square law per body:
            // dir = (body.Position - position).normalized
            // accel += dir * Config.GravityConstant * body.Mass / r^2
            return Vector2.zero;
        }

        private void FixedUpdate()
        {
            // TODO (physics iteration): apply GetAccelerationAt(ship position)
            // every physics step as a mass-independent acceleration.
        }
    }
}
