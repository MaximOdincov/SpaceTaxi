using UnityEngine;

namespace SpaceTaxi.World
{
    /// <summary>
    /// A landable planet. Contact below PhysicsConfig.LandSpeed means landing,
    /// above means damage plus bounce; both are resolved by the LandingSystem
    /// in a later iteration.
    /// </summary>
    public class Planet : CelestialBody
    {
        // TODO (landing iteration): touchdown feedback hooks for LandingSystem.
    }
}
