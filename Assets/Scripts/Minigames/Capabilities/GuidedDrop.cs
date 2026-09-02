using UnityEngine;

/// <summary>
/// Turns a released item's rigidbody into a controlled "guided drop": no tumble, no bounce, gentle
/// depenetration, and (optionally) a horizontal funnel that steers it onto a target slot column as
/// it falls - so the player doesn't have to release dead-centre.
///
/// Consolidates the copy of ConfigureGuidedDrop(bool) in SwordHangMinigame and ChestDepositMinigame.
/// Usage:
/// <code>
///   _drop = GuidedDrop.Begin(rb, col, new GuidedDrop.Settings { funnelSpeed = 3f });
///   // each FixedUpdate while falling, if you want the funnel:
///   _drop.Funnel(targetSlot.position);
///   // on seat / miss:
///   _drop.End();
/// </code>
/// </summary>
public static class GuidedDrop
{
    [System.Serializable]
    public struct Settings
    {
        [Tooltip("Cap on angular speed while falling (rad/s). Low = it barely spins on contact.")]
        public float maxAngularVelocity;
        [Tooltip("Depenetration speed cap - keeps it from popping out of the station.")]
        public float maxDepenetrationVelocity;
        [Tooltip("Also lock rotation entirely (tip-down items).")]
        public bool freezeRotation;
        [Tooltip("Horizontal correction toward the target slot, m/s. 0 = fall straight down.")]
        public float funnelSpeed;

        public static Settings Default => new Settings
        {
            maxAngularVelocity = 2.5f,
            maxDepenetrationVelocity = 0.5f,
            freezeRotation = true,
            funnelSpeed = 0f
        };
    }

    /// <summary>Live handle: keeps the saved rigidbody state so <see cref="End"/> can restore it.</summary>
    public class Handle
    {
        internal Rigidbody rb;
        internal Collider col;
        internal Settings settings;
        internal RigidbodyConstraints savedConstraints;
        internal float savedMaxAngVel;
        internal float savedMaxDepen;
        internal PhysicsMaterial savedMaterial;
        internal PhysicsMaterial dropMaterial;
        internal bool ended;

        /// <summary>Call from FixedUpdate to steer the item's X/Z velocity toward the slot column.</summary>
        public void Funnel(Vector3 slotWorldPos)
        {
            if (ended || rb == null || rb.isKinematic || settings.funnelSpeed <= 0f) return;
            Vector3 p = rb.position;
            Vector3 toColumn = new Vector3(slotWorldPos.x - p.x, 0f, slotWorldPos.z - p.z);
            Vector3 want = Vector3.ClampMagnitude(toColumn / Time.fixedDeltaTime, settings.funnelSpeed);
            Vector3 v = rb.linearVelocity;
            v.x = want.x;
            v.z = want.z;
            rb.linearVelocity = v;
        }

        /// <summary>Restore the rigidbody / collider to how they were before the drop.</summary>
        public void End()
        {
            if (ended) return;
            ended = true;
            if (rb != null)
            {
                rb.constraints = savedConstraints;
                rb.maxAngularVelocity = savedMaxAngVel;
                rb.maxDepenetrationVelocity = savedMaxDepen;
            }
            if (col != null) col.sharedMaterial = savedMaterial;
            if (dropMaterial != null) Object.Destroy(dropMaterial);
        }
    }

    public static Handle Begin(Rigidbody rb, Collider col, Settings settings)
    {
        var h = new Handle { rb = rb, col = col, settings = settings };

        if (rb != null)
        {
            h.savedConstraints = rb.constraints;
            h.savedMaxAngVel = rb.maxAngularVelocity;
            h.savedMaxDepen = rb.maxDepenetrationVelocity;

            rb.constraints = settings.freezeRotation
                ? rb.constraints | RigidbodyConstraints.FreezeRotation
                : rb.constraints;
            rb.maxAngularVelocity = settings.maxAngularVelocity;
            rb.maxDepenetrationVelocity = settings.maxDepenetrationVelocity;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        if (col != null)
        {
            h.savedMaterial = col.sharedMaterial;
            h.dropMaterial = new PhysicsMaterial("GuidedDrop")
            {
                bounciness = 0f,
                dynamicFriction = 0.9f,
                staticFriction = 0.9f,
                bounceCombine = PhysicsMaterialCombine.Minimum,
                frictionCombine = PhysicsMaterialCombine.Maximum
            };
            col.sharedMaterial = h.dropMaterial;
        }

        return h;
    }
}
