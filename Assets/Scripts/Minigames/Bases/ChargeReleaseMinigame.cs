using UnityEngine;

/// <summary>
/// Aim, charge by pulling back, release to fire. Multi-grab first (bow, then quiver), then:
/// LEFT-CLICK nocks an arrow, dragging the mouse BACK draws the string (charge 0 → 1), releasing
/// LEFT-CLICK looses a projectile with force proportional to the charge.
///
/// Fits: bow &amp; arrow.
/// </summary>
public abstract class ChargeReleaseMinigame : HandMinigame
{
    public enum Phase { Equip, Idle, Nocked, Drawing, Loosed }

    [Header("Charge / release")]
    [Tooltip("Mouse-back travel (axis units, summed) that maps to a full draw.")]
    public float fullDrawPull = 6f;
    [Tooltip("Projectile speed at 0 and 1 charge.")]
    public float minLaunchSpeed = 8f;
    public float maxLaunchSpeed = 30f;
    [Tooltip("Projectile prefab spawned on release.")]
    public Rigidbody projectilePrefab;
    [Tooltip("How many shots complete the minigame.")]
    public int shotsToComplete = 1;

    protected Phase phase = Phase.Equip;
    protected float charge01;
    protected int shotsFired;

    protected override void OnMinigameUpdate()
    {
        Hand.AimFromMouse(reachDistance);

        switch (phase)
        {
            case Phase.Equip:
                if (EquipComplete()) phase = Phase.Idle;
                break;

            case Phase.Idle:
                if (MinigameInput.PrimaryDown) { phase = Phase.Nocked; charge01 = 0f; OnNock(); }
                break;

            case Phase.Nocked:
            case Phase.Drawing:
                charge01 = Mathf.Clamp01(charge01 + MinigameInput.DrawPull() / Mathf.Max(0.01f, fullDrawPull));
                if (charge01 > 0f) phase = Phase.Drawing;
                OnDraw(charge01);
                if (MinigameInput.PrimaryUp) Loose();
                break;
        }
    }

    private void Loose()
    {
        float speed = Mathf.Lerp(minLaunchSpeed, maxLaunchSpeed, charge01);
        Vector3 dir = (MouseWorld() - (Hand.HandBone != null ? Hand.HandBone.position : player.transform.position)).normalized;
        SpawnProjectile(dir, speed);

        shotsFired++;
        charge01 = 0f;
        OnLoose(speed);

        if (shotsFired >= shotsToComplete) CompleteMinigame();
        else phase = Phase.Idle;
    }

    /// <summary>Default projectile spawn - override for custom arrows / effects.</summary>
    protected virtual void SpawnProjectile(Vector3 dir, float speed)
    {
        if (projectilePrefab == null || Hand.HandBone == null) return;
        Rigidbody p = Instantiate(projectilePrefab, Hand.HandBone.position, Quaternion.LookRotation(dir));
        p.linearVelocity = dir * speed;
    }

    // --- hooks --------------------------------------------------------------------------------

    /// <summary>True once the player has grabbed everything they need (bow + quiver).</summary>
    protected abstract bool EquipComplete();

    protected virtual void OnNock() { }
    protected virtual void OnDraw(float charge01) { }
    protected virtual void OnLoose(float launchSpeed) { }
}
