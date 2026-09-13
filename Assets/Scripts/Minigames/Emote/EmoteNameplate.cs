using UnityEngine;
using TMPro;

namespace CorruptedCourt.Minigames
{
    /// <summary>
    /// World-space billboard shown above a player's head while they emote: "&lt;DisplayName&gt;
    /// &lt;broadcastVerb&gt;." (e.g. "Drew waltzes."). Shown/hidden by <see cref="PlayerEmotes"/>.
    /// </summary>
    public class EmoteNameplate : MonoBehaviour
    {
        [Tooltip("Text on the World Space canvas child. Put this script on the Canvas root - LateUpdate " +
                 "rotates it (and its child text) to face the camera.")]
        [SerializeField] private TextMeshProUGUI label;

        private Camera cam;

        private void Awake()
        {
            if (label == null) label = GetComponentInChildren<TextMeshProUGUI>();
            Hide();
        }

        private void LateUpdate()
        {
            if (cam == null) cam = Camera.main;
            if (cam == null) return;

            transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
        }

        /// <summary>Sets the text to "&lt;displayName&gt; &lt;broadcastVerb&gt;." and shows it.</summary>
        public void Show(string displayName, string broadcastVerb)
        {
            if (label != null) label.text = $"{displayName} {broadcastVerb}.";
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}
