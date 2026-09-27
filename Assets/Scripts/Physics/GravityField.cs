using System.Collections.Generic;
using UnityEngine;
using SpaceTaxi.World;

namespace SpaceTaxi.Physics
{
    public class GravityField : MonoBehaviour
    {
        private readonly List<CelestialBody> bodies = new List<CelestialBody>();

        public IReadOnlyList<CelestialBody> Bodies => bodies;

        public void Register(CelestialBody body)
        {
            if (body != null && !bodies.Contains(body))
                bodies.Add(body);
        }

        public void Unregister(CelestialBody body)
        {
            bodies.Remove(body);
        }

        public Vector2 GetAccelerationAt(Vector2 position, PhysicsConfig config)
        {
            Vector2 accel = Vector2.zero;
            foreach (CelestialBody body in bodies)
            {
                Vector2 delta = (Vector2)body.transform.position - position;
                float d2 = delta.sqrMagnitude;
                if (d2 < 0.0001f)
                    continue;
                float d = Mathf.Sqrt(d2);
                float a = Mathf.Min(config.GravParamOf(body) / d2, config.GravityAccelClamp);
                accel += delta / d * a;
            }
            return accel;
        }
    }
}
