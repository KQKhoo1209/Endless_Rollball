using UnityEngine;

namespace EndlessRollball.Track
{
    public sealed partial class ObstacleManager
    {
        internal bool OwnsStreetConeCollider(Collider shape)
        {
            if (shape == null || !shape.enabled || !shape.gameObject.activeInHierarchy ||
                shape.GetComponentInParent<TrafficConeProp>() == null) return false;
            var encounter = shape.GetComponentInParent<RoadworksTrackEncounter>();
            return encounter != null && encounter.isActiveAndEnabled &&
                encounter.HazardRoot != null &&
                shape.transform.IsChildOf(encounter.HazardRoot.transform) &&
                activeRoadworks.ContainsKey(encounter.Track);
        }

        // Called only after active hazard ownership and the dynamic cargo check.
        internal bool IsStreetBarrierCollider(Collider shape)
        {
            if (OwnsRoadworksCollider(shape)) return true;
            var combined = shape.GetComponentInParent<CombinedObstacleEncounter>();
            return combined != null && combined.JumpHazards != null &&
                shape.transform.IsChildOf(combined.JumpHazards.transform);
        }

        internal bool IsStreetVehicleCollider(Collider shape)
        {
            if (OwnsCityVehicleCollider(shape) || OwnsJunctionCollider(shape)) return true;
            var combined = shape.GetComponentInParent<CombinedObstacleEncounter>();
            if (combined != null && combined.NavigationHazards != null &&
                shape.transform.IsChildOf(combined.NavigationHazards.transform)) return true;

            foreach (var pairs in active.Values)
                foreach (var pair in pairs)
                {
                    if (pair.Instance != null && pair.Family == "Street.ParkedVehicles" &&
                        shape.transform.IsChildOf(pair.Instance.transform)) return true;
                }
            return false;
        }

        internal bool IsBeachWoodCollider(Collider shape)
        {
            var barrier = shape.GetComponentInParent<BeachBarrierEncounter>();
            if (barrier != null && barrier.Hazard == shape) return true;
            foreach (var pairs in active.Values)
                foreach (var pair in pairs)
                    if (pair.Instance != null && pair.Family == "Beach.WoodenFence" &&
                        shape.transform.IsChildOf(pair.Instance.transform)) return true;
            return false;
        }
    }
}
