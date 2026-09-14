using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CorruptedCourt.UI
{
    /// <summary>
    /// One slice of the emote wheel (a category on the outer ring, an emote on the inner ring).
    /// Pure view - <see cref="EmoteWheelUI"/> instantiates, positions, labels, and highlights these;
    /// this component has no knowledge of what it represents.
    /// </summary>
    public class EmoteWheelSlice : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI label;
        [SerializeField] private Image background;

        [Tooltip("Background tint while not hovered.")]
        public Color normalColor = new Color(1f, 1f, 1f, 0.6f);
        [Tooltip("Background tint while hovered.")]
        public Color hoverColor = new Color(1f, 0.85f, 0.3f, 0.9f);

        private void Awake()
        {
            if (label == null) label = GetComponentInChildren<TextMeshProUGUI>();
            if (background == null) background = GetComponent<Image>();
        }

        public void SetLabel(string text)
        {
            if (label != null) label.text = text;
        }

        public void SetHighlighted(bool on)
        {
            if (background != null) background.color = on ? hoverColor : normalColor;
        }
    }
}
