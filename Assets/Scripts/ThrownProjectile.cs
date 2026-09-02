using UnityEngine;

public class ThrownProjectile : MonoBehaviour
{
    private PlayerController thrower;
    private Vector3 startPosition;
    private float chargePercentage;
    private float maxForce;
    private bool hasHitTarget = false; // Prevents double-triggering
    
    [Tooltip("How much force is lost per meter traveled.")]
    public float kineticDecayRate = 1.5f; 

    public void Initialize(PlayerController throwerRef, Vector3 startPos, float charge, float maxPushback)
    {
        thrower = throwerRef;
        startPosition = startPos;
        chargePercentage = charge;
        maxForce = maxPushback;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (hasHitTarget) return; // Ignore extra collision frames

        // --- DEBUG: Print everything the item bumps into ---
        Debug.Log($"Projectile hit: {collision.collider.gameObject.name} on layer {LayerMask.LayerToName(collision.gameObject.layer)}");

        // 1. FIXED: Use GetComponentInParent in case the collider is on a sub-mesh or capsule child
        PlayerController victim = collision.collider.GetComponentInParent<PlayerController>();
        
        // Ensure we don't hit ourselves, our own thrower, or a ghost
        if (victim != null && victim != thrower && !victim.isGhost)
        {
            hasHitTarget = true;

            // 2. Calculate Distance
            float distanceTraveled = Vector3.Distance(startPosition, transform.position);
            
            // 3. Calculate Kinetic Decay
            float potentialForce = maxForce * chargePercentage;
            float finalForce = potentialForce - (distanceTraveled * kineticDecayRate);
            finalForce = Mathf.Clamp(finalForce, 0f, maxForce);

            // 4. Apply the Pushback
            if (finalForce > 1f) 
            {
                Vector3 pushDirection = (victim.transform.position - transform.position).normalized;
                pushDirection.y = 0; // Prevent upward launching
                
                victim.ApplyPushback(pushDirection, finalForce, 0.2f);
                Debug.Log($"<color=#2ECC71>Successfully pushed {victim.gameObject.name} with force {finalForce}!</color>");
            }
            else
            {
                Debug.Log($"<color=#E74C3C>Hit victim, but finalForce ({finalForce}) was too low to trigger pushback. (Potential: {potentialForce}, Distance: {distanceTraveled:F1}m)</color>");
            }

            // 5. Destroy this temporary script so the item becomes a normal floor prop
            Destroy(this);
        }
    }
}