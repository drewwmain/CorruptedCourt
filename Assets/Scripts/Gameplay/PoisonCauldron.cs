using UnityEngine;
using CorruptedCourt.Core;

namespace CorruptedCourt.Gameplay
{
    // Physical origin for the "Poison the Feast" global sabotage. A Corrupted-faction player [E]s it to
    // start the meter drain (via SabotageManager); a living Court player carrying the antidote item [E]s
    // it to cure the poison. The antidote is spawned elsewhere in the map on trigger, so curing needs a
    // real trip.
    [RequireComponent(typeof(Collider))]
    public class PoisonCauldron : MonoBehaviour, IInteractable
    {
        public string GetInteractionPrompt()
        {
            SabotageManager sm = SabotageManager.Instance;
            if (sm == null) return "The feast cauldron simmers.";

            if (sm.IsPoisonActive)
                return "Press <color=#F4D03F>[E]</color> to add the antidote";

            if (LocalIsCorrupted())
                return sm.SabotageAvailable
                    ? "Press <color=#F4D03F>[E]</color> to <color=#E74C3C>poison the feast</color>"
                    : "<color=#7F8C8D>Sabotage on cooldown</color>";

            return "The feast cauldron simmers.";
        }

        public void OnInteract(GameObject interactor)
        {
            if (interactor == null) return;

            PlayerController pc = interactor.GetComponent<PlayerController>();
            if (pc == null || pc.Vitals == null || pc.Vitals.isGhost) return;

            SabotageManager sm = SabotageManager.Instance;
            if (sm == null) return;

            if (sm.IsPoisonActive)
            {
                // Court counterplay: no-op unless this player is carrying the antidote.
                sm.TryDeliverAntidote(pc);
                return;
            }

            if (pc.Vitals.faction == Faction.Corrupted)
                sm.TryTriggerPoison();
        }

        private static bool LocalIsCorrupted()
        {
            PlayerController local = PlayerController.Local;
            return local != null && local.Vitals != null && local.Vitals.faction == Faction.Corrupted;
        }
    }
}
