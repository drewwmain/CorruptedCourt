using System.Collections.Generic;
using UnityEngine;
using CorruptedCourt.Core;

namespace CorruptedCourt.Gameplay
{
    // A map light source for the "Douse the Braziers" global sabotage. A Corrupted-faction player [E]s a
    // lit brazier to snuff every brazier and plunge the map into darkness (via SabotageManager); a
    // living Court player [E]s a snuffed brazier to relight it. Relighting enough separate braziers
    // (SabotageManager.relightsToCure) lifts the darkness.
    [RequireComponent(typeof(Collider))]
    public class Brazier : MonoBehaviour, IInteractable
    {
        [Tooltip("Flame visual (mesh / particle system root) enabled while the brazier burns and " +
                 "disabled while it is doused.")]
        [SerializeField] private GameObject flameVisual;
        [Tooltip("Optional Light that is this brazier's glow. Toggled together with the flame visual.")]
        [SerializeField] private Light flameLight;

        // Self-registering registry so SabotageManager can douse / relight every brazier in the scene.
        public static readonly List<Brazier> AllBraziers = new List<Brazier>();

        public bool IsLit { get; private set; } = true;

        void Awake()
        {
            SetLit(true);
        }

        void OnEnable()
        {
            if (!AllBraziers.Contains(this)) AllBraziers.Add(this);
        }

        void OnDisable()
        {
            AllBraziers.Remove(this);
        }

        void OnDestroy()
        {
            AllBraziers.Remove(this);
        }

        public void SetLit(bool lit)
        {
            IsLit = lit;
            if (flameVisual != null) flameVisual.SetActive(lit);
            if (flameLight != null) flameLight.enabled = lit;
        }

        public string GetInteractionPrompt()
        {
            SabotageManager sm = SabotageManager.Instance;
            bool douseActive = sm != null && sm.IsDouseActive;

            if (douseActive && !IsLit)
                return "Press <color=#F4D03F>[E]</color> to relight the brazier";

            if (!douseActive && IsLit && LocalIsCorrupted())
                return sm.SabotageAvailable
                    ? "Press <color=#F4D03F>[E]</color> to <color=#E74C3C>snuff the braziers</color>"
                    : "<color=#7F8C8D>Sabotage on cooldown</color>";

            return IsLit ? "The brazier burns steadily." : "The brazier is cold.";
        }

        public void OnInteract(GameObject interactor)
        {
            if (interactor == null) return;

            PlayerController pc = interactor.GetComponent<PlayerController>();
            if (pc == null || pc.Vitals == null || pc.Vitals.isGhost) return;

            SabotageManager sm = SabotageManager.Instance;
            if (sm == null) return;

            bool corrupted = pc.Vitals.faction == Faction.Corrupted;

            if (sm.IsDouseActive)
            {
                // Court counterplay: relight a snuffed brazier.
                if (!corrupted && !IsLit)
                {
                    SetLit(true);
                    sm.NotifyBrazierRelit(this);
                }
                return;
            }

            // No sabotage running: a Corrupted can snuff the braziers to trigger the map-wide darkness.
            if (corrupted && IsLit)
                sm.TryTriggerDouse();
        }

        private static bool LocalIsCorrupted()
        {
            PlayerController local = PlayerController.Local;
            return local != null && local.Vitals != null && local.Vitals.faction == Faction.Corrupted;
        }
    }
}
