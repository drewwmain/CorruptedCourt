using UnityEngine;
using CorruptedCourt.Gameplay;

namespace CorruptedCourt.Minigames
{
    /// <summary>
    /// Sibling of <see cref="PlayerController"/> that tracks "is this player emoting right now" - the
    /// missing half of the emote wheel (which today fires an animator trigger and forgets). Drives the
    /// animation, the broadcast nameplate, and <see cref="GameEvents.EmotePerformed"/>, which a future
    /// <c>EmoteStep</c> (B14) evaluates against and every partner-overlap check reads.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerEmotes : MonoBehaviour
    {
        [Tooltip("World-space nameplate shown while performing. Optional - resolved from a child if left empty.")]
        [SerializeField] private EmoteNameplate nameplate;

        private PlayerController player;

        /// <summary>The emote currently performing, or the last one performed. Null until the first <see cref="Perform"/>.</summary>
        public EmoteDefinition Current { get; private set; }

        /// <summary>True while the performance is still playing out.</summary>
        public bool IsPerforming { get; private set; }

        /// <summary>Time.time when the current/last performance began.</summary>
        public float StartedAt { get; private set; }

        /// <summary>Time.time the current performance auto-stops at. Meaningless for a looping emote (see <see cref="Stop"/>).</summary>
        public float EndsAt { get; private set; }

        private void Awake()
        {
            player = GetComponent<PlayerController>();
            if (nameplate == null) nameplate = GetComponentInChildren<EmoteNameplate>();
        }

        private void Update()
        {
            if (IsPerforming && Current != null && !Current.loops && Time.time >= EndsAt) Stop();
        }

        /// <summary>Plays <paramref name="emote"/>: animator trigger, nameplate, and the broadcast event.</summary>
        public void Perform(EmoteDefinition emote)
        {
            if (emote == null) return;

            Current = emote;
            StartedAt = Time.time;
            EndsAt = Time.time + emote.durationSeconds;
            IsPerforming = true;

            Animator animator = player.PlayerAnimator;
            if (animator != null && !string.IsNullOrEmpty(emote.animTrigger)) animator.SetTrigger(emote.animTrigger);

            if (nameplate != null) nameplate.Show(player.DisplayName, emote.broadcastVerb);

            GameEvents.RaiseEmotePerformed(player, emote);
        }

        /// <summary>Ends the performance early (or ends a looping one - see <see cref="EmoteDefinition.loops"/>).</summary>
        public void Stop()
        {
            IsPerforming = false;
            if (nameplate != null) nameplate.Hide();
        }
    }
}
