using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using CorruptedCourt.Gameplay;
using CorruptedCourt.Minigames;

namespace CorruptedCourt.UI
{
    /// <summary>
    /// Drives the paginated radial emote wheel: detects the roleplay "hold B" gesture, renders
    /// whichever session <see cref="EmoteWheelController"/> has open (roleplay or minigame-triggered -
    /// both use the same hold/aim/release gesture), and calls <see cref="EmoteWheelController.Commit"/>
    /// / <see cref="EmoteWheelController.Cancel"/> on release.
    ///
    /// One fixed 8-slot ring is shown at a time, one page per <see cref="EmoteCategory"/>. A free-choice
    /// session can scroll the mouse wheel to turn pages; a minigame-restricted session (category or a
    /// specific emote) opens locked to the one relevant page with paging disabled. Every page always
    /// renders exactly <see cref="SlotsPerPage"/> slots regardless of how many emotes exist in that
    /// category - unused slots render vacant - so slot position never has to renumber, and the same
    /// gesture lands the same slot in every category.
    ///
    /// Ring layout is entirely code-driven - each slice's position is <c>radius * (cos, sin)</c> around
    /// a circle - so the Editor side only needs one reusable <see cref="EmoteWheelSlice"/> prefab and
    /// one empty ring container, not a hand-placed layout per category.
    /// </summary>
    public class EmoteWheelUI : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("Root panel toggled active/inactive to show/hide the whole wheel.")]
        [SerializeField] private GameObject wheelRoot;
        [Tooltip("Empty RectTransform anchored to screen centre - the ring's slices are parented here.")]
        [SerializeField] private RectTransform ringRoot;
        [Tooltip("Reusable slice view, instantiated once per slot (up to SlotsPerPage) on every page.")]
        [SerializeField] private EmoteWheelSlice slicePrefab;
        [Tooltip("Optional - shows the active category name. The only on-screen page indicator, since paging is scroll-only.")]
        [SerializeField] private TextMeshProUGUI pageLabel;

        [Header("Layout")]
        [SerializeField] private float ringRadius = 220f;
        [Tooltip("Mouse must move at least this far from screen centre before any slice counts as hovered.")]
        [SerializeField] private float deadzoneRadius = 40f;
        [Tooltip("Minimum time between scroll-driven page turns, so one scroll tick is one page, not a spin.")]
        [SerializeField] private float pageScrollCooldown = 0.2f;
        [Tooltip("How long the committed slot's selected-glow flashes before the wheel actually closes.")]
        [SerializeField] private float selectedFlashSeconds = 0.2f;

        private const int SlotsPerPage = 8;
        private const float ScrollThreshold = 0.05f;

        private static readonly EmoteCategory[] Categories = (EmoteCategory[])Enum.GetValues(typeof(EmoteCategory));

        private readonly List<EmoteWheelSlice> slices = new List<EmoteWheelSlice>();
        private readonly List<EmoteDefinition> slotEntries = new List<EmoteDefinition>();
        private readonly List<EmoteDefinition> pageOptionsBuffer = new List<EmoteDefinition>();

        private bool isShowing;
        private bool canPage;
        private bool isClosing;
        private bool ownsCursorLock;
        private int activeCategoryIndex;
        private int hoveredIndex = -1;
        private float lastPageScrollTime;
        private float pendingCloseTime;

        private void Awake()
        {
            if (wheelRoot != null) wheelRoot.SetActive(false);
        }

        private void Update()
        {
            if (isClosing)
            {
                if (Time.time >= pendingCloseTime) HideWheel();
                return;
            }

            EmoteWheelController controller = EmoteWheelController.Instance;
            if (controller == null)
            {
                if (isShowing) HideWheel();
                return;
            }

            if (GameEvents.SettingsMenuOpen) return; // don't fight the pause menu

            if (!controller.IsOpen)
            {
                if (isShowing) HideWheel();
                TryOpenFromHotkey(controller);
                return;
            }

            if (!isShowing) BeginShow(controller.ActiveFilter);

            UpdateHover();
            if (canPage) UpdatePaging();

            if (Input.GetKeyUp(controller.openKey))
            {
                EmoteDefinition chosen = (hoveredIndex >= 0 && hoveredIndex < slotEntries.Count)
                    ? slotEntries[hoveredIndex]
                    : null;

                if (chosen != null)
                {
                    controller.Commit(chosen);
                    BeginClosingFlash();
                }
                else
                {
                    controller.Cancel();
                    HideWheel();
                }
            }
        }

        // Commit fires the emote and closes the controller's session immediately, but the chosen
        // slice's selected-glow needs a beat on screen first - hold the visual open for
        // selectedFlashSeconds rather than hiding on the very next frame.
        private void BeginClosingFlash()
        {
            isClosing = true;
            pendingCloseTime = Time.time + selectedFlashSeconds;
            if (hoveredIndex >= 0 && hoveredIndex < slices.Count && slices[hoveredIndex] != null)
                slices[hoveredIndex].SetSelected(true);
        }

        // Only the free-choice roleplay path originates a session here - a minigame-triggered wheel
        // (a future SharedStationMinigame-family concrete, say) would call EmoteWheelController.Open
        // itself, already active by the time it does, which is exactly why Open no longer gates on
        // MinigameBase.IsAnyActive - that check belongs here instead, for the session THIS hotkey starts.
        private void TryOpenFromHotkey(EmoteWheelController controller)
        {
            PlayerController local = PlayerController.Local;
            if (local == null) return;
            if (!Input.GetKeyDown(controller.openKey)) return;
            if (MinigameBase.IsAnyActive || local.Vitals.isStrangling || local.Vitals.isArrested) return;

            controller.Open(local, new EmoteFilter { freeChoice = true }, null);
        }

        private void BeginShow(EmoteFilter filter)
        {
            isShowing = true;
            hoveredIndex = -1;
            activeCategoryIndex = 0;

            // Every field at default behaves like freeChoice - mirrors EmoteWheelController.CurrentOptions.
            canPage = filter.freeChoice || (filter.specific == null && filter.category == null);

            if (!canPage)
            {
                EmoteCategory? cat = filter.specific != null ? filter.specific.category : filter.category;
                if (cat != null)
                {
                    int idx = Array.IndexOf(Categories, cat.Value);
                    if (idx >= 0) activeCategoryIndex = idx;
                }
            }

            PopulateRing();
            if (wheelRoot != null) wheelRoot.SetActive(true);

            // Hover reads raw Input.mousePosition, which doesn't track real movement while the cursor
            // is locked - the roleplay hotkey path is the only one responsible for that lock (a
            // minigame-triggered wheel's host minigame already manages its own cursor state, e.g.
            // HandMinigame's OnHandBegin/OnHandEnd - stealing that back on close would fight it).
            ownsCursorLock = !MinigameBase.IsAnyActive;
            if (ownsCursorLock)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void HideWheel()
        {
            isShowing = false;
            isClosing = false;
            hoveredIndex = -1;
            ClearRing();
            slotEntries.Clear();
            if (wheelRoot != null) wheelRoot.SetActive(false);

            if (ownsCursorLock)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            ownsCursorLock = false;
        }

        private void UpdateHover()
        {
            Vector2 center = new Vector2(Screen.width, Screen.height) * 0.5f;
            Vector2 offset = (Vector2)Input.mousePosition - center;
            bool pastDeadzone = offset.magnitude >= deadzoneRadius;

            int idx = pastDeadzone ? AngleToIndex(offset, SlotsPerPage) : -1;
            if (idx >= 0 && (idx >= slotEntries.Count || slotEntries[idx] == null)) idx = -1; // vacant slot = nothing hovered

            hoveredIndex = idx;
            for (int i = 0; i < slices.Count; i++)
                if (slices[i] != null) slices[i].SetHighlighted(i == hoveredIndex);
        }

        private void UpdatePaging()
        {
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) < ScrollThreshold) return;
            if (Time.time < lastPageScrollTime + pageScrollCooldown) return;
            lastPageScrollTime = Time.time;

            int dir = scroll > 0f ? 1 : -1;
            activeCategoryIndex = ((activeCategoryIndex + dir) % Categories.Length + Categories.Length) % Categories.Length;
            hoveredIndex = -1;
            PopulateRing();
        }

        private void PopulateRing()
        {
            ClearRing();
            slotEntries.Clear();
            pageOptionsBuffer.Clear();

            EmoteCategory activeCategory = Categories[activeCategoryIndex];
            EmoteWheelController controller = EmoteWheelController.Instance;
            if (controller != null)
            {
                foreach (EmoteDefinition e in controller.CurrentOptions)
                {
                    if (e == null || e.category != activeCategory) continue;
                    pageOptionsBuffer.Add(e);
                    if (pageOptionsBuffer.Count >= SlotsPerPage) break;
                }
            }

            for (int i = 0; i < SlotsPerPage; i++)
            {
                EmoteDefinition entry = i < pageOptionsBuffer.Count ? pageOptionsBuffer[i] : null;
                slotEntries.Add(entry);

                EmoteWheelSlice slice = SpawnSlice(i);
                if (slice == null) continue;

                if (entry != null) slice.SetLabel(entry.displayName);
                else slice.SetVacant(true);

                slices.Add(slice);
            }

            if (pageLabel != null) pageLabel.text = activeCategory.ToString();
        }

        private EmoteWheelSlice SpawnSlice(int index)
        {
            if (slicePrefab == null || ringRoot == null) return null;

            EmoteWheelSlice slice = Instantiate(slicePrefab, ringRoot);
            RectTransform rt = slice.transform as RectTransform;
            if (rt != null) rt.anchoredPosition = SliceDirection(index, SlotsPerPage) * ringRadius;
            return slice;
        }

        private void ClearRing()
        {
            if (ringRoot != null)
            {
                for (int i = ringRoot.childCount - 1; i >= 0; i--) Destroy(ringRoot.GetChild(i).gameObject);
            }
            slices.Clear();
        }

        // Index 0 points straight up; increasing index rotates clockwise. Shared by SpawnSlice's
        // placement and AngleToIndex's hover test so the two always agree with each other, even if
        // "clockwise" turns out mirrored on screen - I can't preview this visually, so if it feels
        // backwards once you run it, flip the sign on `deg` in both methods.
        private static Vector2 SliceDirection(int index, int count)
        {
            float deg = 90f - index * (360f / count);
            float rad = deg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        }

        private static int AngleToIndex(Vector2 offset, int count)
        {
            float mouseDeg = Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg;
            float step = 360f / count;
            float rel = ((90f - mouseDeg) % 360f + 360f) % 360f;
            return Mathf.RoundToInt(rel / step) % count;
        }
    }
}
