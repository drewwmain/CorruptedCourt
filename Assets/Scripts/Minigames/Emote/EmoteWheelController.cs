using System;
using System.Collections.Generic;
using UnityEngine;
using CorruptedCourt.Gameplay;

namespace CorruptedCourt.Minigames
{
    /// <summary>
    /// Restricts which emotes <see cref="EmoteWheelController.Open(PlayerController, EmoteFilter, Action{EmoteDefinition})"/>
    /// offers. <see cref="freeChoice"/> is the only mode a task step uses - the step judges the
    /// committed emote afterward (a future EmoteStep) rather than the wheel narrowing it, since
    /// narrowing would broadcast to every observer which category their neighbour needs
    /// (Tasks/ARCHITECTURE.md §11.4). <see cref="category"/> restricts to one category (e.g. a
    /// Dance/Conversation minigame); <see cref="specific"/> restricts to exactly one emote. Leaving
    /// every field at its default behaves the same as <see cref="freeChoice"/>.
    /// </summary>
    public struct EmoteFilter
    {
        public EmoteCategory? category;
        public EmoteDefinition specific;
        public bool freeChoice;
    }

    /// <summary>
    /// Radial "hold a key, mouse to a slice, release to commit" emote picker. A persistent service
    /// (one in the scene / on the player HUD), used both by roleplay gameplay and by the emote-driven
    /// minigames (Speech at the podium, Dance, Conversation).
    ///
    /// Scaffolding: the open/commit API and the catalogue are here; the actual wheel UI (slices,
    /// hover-by-angle, categories tab) is built at B13b. <see cref="Open(PlayerController, EmoteFilter, Action{EmoteDefinition})"/>
    /// currently just exposes the filtered options and waits for <see cref="Commit"/> / <see cref="Cancel"/>
    /// to be driven by that UI.
    /// </summary>
    public class EmoteWheelController : MonoBehaviour
    {
        public static EmoteWheelController Instance { get; private set; }

        [Tooltip("Every emote the wheel can offer. Filtered by category when a minigame requests one.")]
        public List<EmoteDefinition> catalogue = new List<EmoteDefinition>();

        [Tooltip("Hold this to open the wheel during normal gameplay.")]
        public KeyCode openKey = KeyCode.B;

        public bool IsOpen { get; private set; }
        public EmoteFilter ActiveFilter { get; private set; }

        private Action<EmoteDefinition> onCommit;
        private PlayerController performer;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        /// <summary>Options currently offered (respects <see cref="ActiveFilter"/>).</summary>
        public IEnumerable<EmoteDefinition> CurrentOptions
        {
            get
            {
                foreach (EmoteDefinition e in catalogue)
                {
                    if (e == null) continue;
                    if (ActiveFilter.freeChoice) { yield return e; continue; }
                    if (ActiveFilter.specific != null) { if (e == ActiveFilter.specific) yield return e; continue; }
                    if (ActiveFilter.category != null) { if (e.category == ActiveFilter.category.Value) yield return e; continue; }
                    yield return e; // every field at default - same as freeChoice
                }
            }
        }

        /// <summary>
        /// Open the wheel. <paramref name="filter"/> restricts the offered emotes - see
        /// <see cref="EmoteFilter"/>. <paramref name="onCommit"/> fires with the chosen emote, or null
        /// if cancelled. Refuses (no-op) while the player is strangling or arrested.
        ///
        /// Deliberately does NOT gate on <see cref="MinigameBase.IsAnyActive"/> here: a minigame is
        /// already in that registry by the time its own OnMinigameBegin runs (see
        /// MinigameBase.SetupMinigame), so a minigame calling this from its own OnMinigameBegin would
        /// otherwise be blocked by its own presence. The "don't let the roleplay wheel interrupt an
        /// unrelated minigame" check belongs on whoever opens the FREE-CHOICE roleplay wheel
        /// (EmoteWheelUI), not here, since a minigame legitimately opens the wheel while active.
        /// </summary>
        public void Open(PlayerController player, EmoteFilter filter, Action<EmoteDefinition> onCommit)
        {
            if (player == null) return;
            if (player.Vitals.isStrangling || player.Vitals.isArrested) return;

            performer = player;
            ActiveFilter = filter;
            this.onCommit = onCommit;
            IsOpen = true;
            // TODO(B13b): show the radial UI, populate from CurrentOptions.
        }

        /// <summary>Category-only convenience overload - forwards to
        /// <see cref="Open(PlayerController, EmoteFilter, Action{EmoteDefinition})"/>. Its original
        /// caller, EmoteMinigame, was deleted in B15b (instrument_play/podium_speech are EmoteStep-driven
        /// now); currently unused, kept as a simpler entry point for any future single-category caller.</summary>
        public void Open(PlayerController player, EmoteCategory? filter, Action<EmoteDefinition> onCommit)
            => Open(player, new EmoteFilter { category = filter }, onCommit);

        /// <summary>Called by the wheel UI when the player releases on a slice.</summary>
        public void Commit(EmoteDefinition choice)
        {
            if (!IsOpen) return;
            IsOpen = false;
            ActiveFilter = default;

            if (choice != null && performer != null)
            {
                PlayerEmotes emotes = performer.GetComponent<PlayerEmotes>();
                if (emotes != null) emotes.Perform(choice);
            }

            Action<EmoteDefinition> cb = onCommit;
            onCommit = null;
            cb?.Invoke(choice);
        }

        /// <summary>Called by the wheel UI when the player releases off any slice.</summary>
        public void Cancel()
        {
            if (!IsOpen) return;
            IsOpen = false;
            ActiveFilter = default;
            Action<EmoteDefinition> cb = onCommit;
            onCommit = null;
            cb?.Invoke(null);
        }
    }
}
