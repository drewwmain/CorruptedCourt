using UnityEngine;
using CorruptedCourt.Core;

namespace CorruptedCourt.Gameplay
{
    // Owns the dead player's spectator loop (G3.2): free-fly movement, and the Haunt ability - a
    // cooldown-gated poltergeist shove that the living can witness. Full role visibility (the dead
    // seeing every player's faction) is a view concern and lives on UIManager's ghost HUD.
    //
    // A ghost NEVER credits the Court meter and stays locked out of combat, arrest, power-ups,
    // reporting and voting. Those guards already live on their own systems (PlayerVitals,
    // PlayerPowerUps, VotingManager, Corpse, TaskManager) and are unchanged - nothing here re-opens
    // them. Haunt only pushes loose physics props; it touches no task, station or meter code.
    //
    // Sibling logic component on the Player GameObject, resolved by PlayerController.Awake the same
    // way as PlayerMotor / PlayerVitals / PlayerStrangle. Free-fly is driven from
    // PlayerController.Update; Haunt is routed from PlayerController.OnPunch while ghosted.
    [RequireComponent(typeof(PlayerController))]
    [RequireComponent(typeof(PlayerVitals))]
    [RequireComponent(typeof(CharacterController))]
    public class PlayerGhost : MonoBehaviour
    {
        [Header("Free-Fly")]
        [Tooltip("Spectator fly speed in world units per second while dead. Movement follows the " +
                 "camera aim; there is no gravity, and the controller is set to ignore all collision " +
                 "layers so the ghost passes through geometry.")]
        [SerializeField] private float flySpeed = 9f;

        [Header("Haunt")]
        [Tooltip("Seconds between Haunt uses. Haunt shoves loose physics objects around the ghost so " +
                 "living players nearby see a poltergeist is about.")]
        [SerializeField] private float hauntCooldown = 12f;

        [Tooltip("Radius in world units around the ghost that a Haunt disturbs.")]
        [SerializeField] private float hauntRadius = 4f;

        [Tooltip("Impulse strength applied to each loose object caught in a Haunt.")]
        [SerializeField] private float hauntForce = 6f;

        [Tooltip("Optional. Spawned at the ghost's position on every Haunt (dust puff / cold breath / " +
                 "guttering-candle VFX). Leave empty to rely on the physics shove alone.")]
        [SerializeField] private GameObject hauntEffectPrefab;

        // The sibling components on this same GameObject.
        private PlayerController player;
        private PlayerVitals vitals;
        private CharacterController cc;

        private float lastHauntTime = Mathf.NegativeInfinity;
        private bool freeFlyEngaged;

        // Non-alloc buffer for the Haunt overlap sweep - main-thread, consumed immediately. 32 is
        // comfortably above the number of loose props in one room-sized radius.
        private static readonly Collider[] hauntBuffer = new Collider[32];
        private static bool hauntBufferFullWarned;

        /// <summary>True once Haunt has recharged. Read by UIManager's ghost HUD.</summary>
        public bool IsHauntReady => vitals != null && vitals.isGhost && Time.time >= lastHauntTime + hauntCooldown;

        void Awake()
        {
            player = GetComponent<PlayerController>();
            vitals = GetComponent<PlayerVitals>();
            cc = GetComponent<CharacterController>();
        }

        void OnEnable()
        {
            GameEvents.PlayerGhosted += OnPlayerGhosted;
        }

        void OnDisable()
        {
            GameEvents.PlayerGhosted -= OnPlayerGhosted;
        }

        private void OnPlayerGhosted(PlayerController ghosted)
        {
            if (ghosted != player || player == null || !player.IsLocal) return;

            // A spectator has no collision: excluding every layer lets the free-fly Move() sweeps
            // pass through all geometry (and lets the living walk through the invisible ghost). The
            // controller stays ENABLED on purpose - a disabled one would make any lingering
            // CustodyFollowRoutine spam "Move called on inactive controller".
            if (cc != null) cc.excludeLayers = Physics.AllLayers;
            freeFlyEngaged = true;
        }

        /// <summary>Free-fly step. Called every frame from PlayerController.Update() while the local
        /// player is a ghost, in place of the normal grounded-movement calls. Camera-relative: the
        /// move vector is mapped onto the camera's forward / right, so looking up and holding forward
        /// flies upward. No gravity term, so the ghost holds altitude when the stick is centred.</summary>
        public void UpdateFreeFly(Vector2 moveInput)
        {
            if (!freeFlyEngaged || cc == null || player == null) return;

            Transform cam = player.PlayerCamera;
            if (cam == null) return;

            Vector3 dir = cam.forward * moveInput.y + cam.right * moveInput.x;
            if (dir.sqrMagnitude > 1f) dir.Normalize();

            cc.Move(dir * (flySpeed * Time.deltaTime));
        }

        /// <summary>Haunt. Routed from PlayerController.OnPunch while ghosted (the living punch, the
        /// dead haunt - same bind). Cooldown-gated. Shoves loose physics props near the ghost and
        /// spawns the optional VFX, giving the living a physical "something's here" cue. Never
        /// touches a task, a station, a meter or another player.</summary>
        public void TryHaunt()
        {
            if (vitals == null || !vitals.isGhost) return;

            if (Time.time < lastHauntTime + hauntCooldown)
            {
                Log.Game($"Haunt is still gathering strength ({lastHauntTime + hauntCooldown - Time.time:0.0}s).");
                return;
            }
            lastHauntTime = Time.time;

            if (hauntEffectPrefab != null)
                Instantiate(hauntEffectPrefab, transform.position, Quaternion.identity);

            int count = Physics.OverlapSphereNonAlloc(
                transform.position, hauntRadius, hauntBuffer, Physics.AllLayers, QueryTriggerInteraction.Ignore);

            if (count == hauntBuffer.Length && !hauntBufferFullWarned)
            {
                hauntBufferFullWarned = true;
                Log.Warn($"[PlayerGhost] haunt overlap buffer full ({hauntBuffer.Length}) - some objects near the ghost were not disturbed.");
            }

            int shoved = 0;
            for (int i = 0; i < count; i++)
            {
                Collider c = hauntBuffer[i];
                if (c == null) continue;

                Rigidbody rb = c.attachedRigidbody;
                if (rb == null) continue;
                if (rb.GetComponentInParent<PlayerController>() != null) continue; // never the living (or the ghost)

                // Wake a settled loose pickup (kinematic once it stops), but never anything parented
                // into a hand or a station, and nothing structural.
                if (rb.isKinematic)
                {
                    PickupItem pi = rb.GetComponentInParent<PickupItem>();
                    if (pi == null || pi.transform.parent != null) continue;
                    rb.isKinematic = false;
                    rb.useGravity = true;
                }

                Vector3 push = rb.worldCenterOfMass - transform.position;
                push.y = Mathf.Abs(push.y) + 0.35f; // always a little lift so the shove reads
                rb.AddForce(push.normalized * hauntForce, ForceMode.Impulse);
                rb.AddTorque(Random.insideUnitSphere * hauntForce, ForceMode.Impulse);
                shoved++;
            }

            Log.Game($"<color=#8E44AD>{gameObject.name} unleashes a Haunt! ({shoved} object(s) disturbed)</color>");
        }
    }
}
