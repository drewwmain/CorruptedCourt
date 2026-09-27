using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CorruptedCourt.UI
{
    /// <summary>
    /// One slot of the emote wheel's ring. Pure view - <see cref="EmoteWheelUI"/> instantiates,
    /// positions, labels, and highlights these; this component has no knowledge of what it represents.
    /// A slot renders either a real emote (<see cref="SetLabel"/>) or vacant (<see cref="SetVacant"/>)
    /// when its page has fewer emotes than slots. <see cref="frame"/> is the always-visible medallion
    /// ring; <see cref="hoverGlow"/> and <see cref="selectedGlow"/> are separate backdrop sprites shown
    /// on hover / on commit, not a color tint on the frame itself.
    /// </summary>
    public class EmoteWheelSlice : MonoBehaviour
    {
        [SerializeField] private Image frame;
        [SerializeField] private Image hoverGlow;
        [SerializeField] private Image selectedGlow;
        [SerializeField] private TextMeshProUGUI label;

        [Tooltip("Frame alpha while this slot is vacant (unauthored).")]
        public float vacantAlpha = 0.35f;
        [Tooltip("Label shown on a vacant slot.")]
        public string vacantLabel = "—"; // em dash

        private bool isVacant;

        public void SetLabel(string text)
        {
            isVacant = false;
            if (label != null) label.text = text;
            SetFrameAlpha(1f);
            SetGlow(hoverGlow, false);
            SetGlow(selectedGlow, false);
        }

        /// <summary>Marks this slot as unauthored: dims the frame, shows a placeholder, and stops it
        /// from counting as a hover/commit target (EmoteWheelUI excludes vacant slots before
        /// SetHighlighted/SetSelected is ever called with true).</summary>
        public void SetVacant(bool vacant)
        {
            isVacant = vacant;
            if (label != null) label.text = vacant ? vacantLabel : label.text;
            SetFrameAlpha(vacant ? vacantAlpha : 1f);
            SetGlow(hoverGlow, false);
            SetGlow(selectedGlow, false);
        }

        public void SetHighlighted(bool on)
        {
            if (isVacant) return; // vacant slots never highlight - they're never a valid hover target
            SetGlow(hoverGlow, on);
        }

        /// <summary>Brief flash shown on the committed slot before the wheel closes - see
        /// EmoteWheelUI's closing delay.</summary>
        public void SetSelected(bool on)
        {
            if (isVacant) return;
            SetGlow(selectedGlow, on);
        }

        private void SetFrameAlpha(float alpha)
        {
            if (frame == null) return;
            Color c = frame.color;
            c.a = alpha;
            frame.color = c;
        }

        private static void SetGlow(Image glow, bool on)
        {
            if (glow != null) glow.gameObject.SetActive(on);
        }
    }
}
