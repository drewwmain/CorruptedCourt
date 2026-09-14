using System;
using System.Collections.Generic;
using UnityEngine;
using CorruptedCourt.Gameplay;
using CorruptedCourt.Minigames;

namespace CorruptedCourt.UI
{
    /// <summary>
    /// Drives the two-ring radial emote wheel: detects the roleplay "hold B" gesture, renders
    /// whichever session <see cref="EmoteWheelController"/> has open (roleplay or minigame-triggered -
    /// both use the same hold/aim/release gesture), and calls <see cref="EmoteWheelController.Commit"/>
    /// / <see cref="EmoteWheelController.Cancel"/> on release.
    ///
    /// Ring layout is entirely code-driven - each slice's position is <c>radius * (cos, sin)</c>
    /// around a circle sized by however many entries exist that frame - so the Editor side only needs
    /// one reusable <see cref="EmoteWheelSlice"/> prefab and two empty ring containers, not a
    /// hand-placed layout per category/emote (the inner ring's count varies by category anyway).
    /// </summary>
    public class EmoteWheelUI : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("Root panel toggled active/inactive to show/hide the whole wheel.")]
        [SerializeField] private GameObject wheelRoot;
        [Tooltip("Empty RectTransform anchored to screen centre - outer-ring (category) slices are parented here.")]
        [SerializeField] private RectTransform outerRingRoot;
        [Tooltip("Empty RectTransform anchored to screen centre - inner-ring (emote) slices are parented here.")]
        [SerializeField] private RectTransform innerRingRoot;
        [Tooltip("Reusable slice view, instantiated once per category / per emote as needed.")]
        [SerializeField] private EmoteWheelSlice slicePrefab;

        [Header("Layout")]
        [SerializeField] private float outerRadius = 150f;
        [SerializeField] private float innerRadius = 280f;
        [Tooltip("Mouse must move at least this far from screen centre before any slice counts as hovered.")]
        [SerializeField] private float deadzoneRadius = 40f;

        private static readonly EmoteCategory[] Categories = (EmoteCategory[])Enum.GetValues(typeof(EmoteCategory));

        private readonly List<EmoteWheelSlice> outerSlices = new List<EmoteWheelSlice>();
        private readonly List<EmoteWheelSlice> innerSlices = new List<EmoteWheelSlice>();
        private readonly List<EmoteDefinition> innerOptions = new List<EmoteDefinition>();

        private bool isShowing;
        private bool isShowingInner;
        private int hoveredOuterIndex = -1;
        private int hoveredInnerIndex = -1;

        private void Awake()
        {
            if (wheelRoot != null) wheelRoot.SetActive(false);
        }

        private void Update()
        {
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

            if (Input.GetKeyUp(controller.openKey))
            {
                EmoteDefinition chosen = (isShowingInner && hoveredInnerIndex >= 0 && hoveredInnerIndex < innerOptions.Count)
                    ? innerOptions[hoveredInnerIndex]
                    : null;

                if (chosen != null) controller.Commit(chosen);
                else controller.Cancel();
            }
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
            hoveredOuterIndex = -1;
            hoveredInnerIndex = -1;

            bool browseCategories = filter.freeChoice || (filter.specific == null && filter.category == null);

            if (browseCategories)
            {
                isShowingInner = false;
                PopulateOuterRing();
                SetRingActive(outer: true, inner: false);
            }
            else
            {
                isShowingInner = true;
                EmoteCategory? cat = filter.specific != null ? filter.specific.category : filter.category;
                PopulateInnerRing(cat, filter.specific);
                SetRingActive(outer: false, inner: true);
            }

            if (wheelRoot != null) wheelRoot.SetActive(true);
        }

        private void HideWheel()
        {
            isShowing = false;
            isShowingInner = false;
            hoveredOuterIndex = -1;
            hoveredInnerIndex = -1;
            ClearRing(outerRingRoot, outerSlices);
            ClearRing(innerRingRoot, innerSlices);
            innerOptions.Clear();
            if (wheelRoot != null) wheelRoot.SetActive(false);
        }

        private void SetRingActive(bool outer, bool inner)
        {
            if (outerRingRoot != null) outerRingRoot.gameObject.SetActive(outer);
            if (innerRingRoot != null) innerRingRoot.gameObject.SetActive(inner);
        }

        private void UpdateHover()
        {
            Vector2 center = new Vector2(Screen.width, Screen.height) * 0.5f;
            Vector2 offset = (Vector2)Input.mousePosition - center;
            bool pastDeadzone = offset.magnitude >= deadzoneRadius;

            if (!isShowingInner)
            {
                hoveredOuterIndex = (pastDeadzone && outerSlices.Count > 0) ? AngleToIndex(offset, outerSlices.Count) : -1;
                Highlight(outerSlices, hoveredOuterIndex);

                if (hoveredOuterIndex >= 0)
                {
                    // Locks in for the rest of this session - see the class comment / B13b plan for why
                    // this is a deliberate one-way transition, not a re-browsable ring switch: the
                    // player can still Cancel and re-open to try a different category.
                    isShowingInner = true;
                    PopulateInnerRing(Categories[hoveredOuterIndex], null);
                    SetRingActive(outer: false, inner: true);
                    hoveredInnerIndex = -1;
                }
            }
            else
            {
                hoveredInnerIndex = (pastDeadzone && innerSlices.Count > 0) ? AngleToIndex(offset, innerSlices.Count) : -1;
                Highlight(innerSlices, hoveredInnerIndex);
            }
        }

        private void PopulateOuterRing()
        {
            ClearRing(outerRingRoot, outerSlices);
            for (int i = 0; i < Categories.Length; i++)
            {
                EmoteWheelSlice slice = SpawnSlice(outerRingRoot, i, Categories.Length, outerRadius);
                if (slice == null) continue;
                slice.SetLabel(Categories[i].ToString());
                outerSlices.Add(slice);
            }
        }

        // category == null && specific == null means "every current option" - mirrors
        // EmoteWheelController.CurrentOptions treating an all-default filter the same as free choice.
        private void PopulateInnerRing(EmoteCategory? category, EmoteDefinition specific)
        {
            ClearRing(innerRingRoot, innerSlices);
            innerOptions.Clear();

            EmoteWheelController controller = EmoteWheelController.Instance;
            if (controller != null)
            {
                foreach (EmoteDefinition e in controller.CurrentOptions)
                {
                    if (e == null) continue;
                    if (specific != null) { if (e == specific) innerOptions.Add(e); continue; }
                    if (category != null) { if (e.category == category.Value) innerOptions.Add(e); continue; }
                    innerOptions.Add(e);
                }
            }

            for (int i = 0; i < innerOptions.Count; i++)
            {
                EmoteWheelSlice slice = SpawnSlice(innerRingRoot, i, innerOptions.Count, innerRadius);
                if (slice == null) continue;
                slice.SetLabel(innerOptions[i].displayName);
                innerSlices.Add(slice);
            }
        }

        private EmoteWheelSlice SpawnSlice(RectTransform root, int index, int count, float radius)
        {
            if (slicePrefab == null || root == null || count <= 0) return null;

            EmoteWheelSlice slice = Instantiate(slicePrefab, root);
            RectTransform rt = slice.transform as RectTransform;
            if (rt != null) rt.anchoredPosition = SliceDirection(index, count) * radius;
            return slice;
        }

        private static void ClearRing(RectTransform root, List<EmoteWheelSlice> slices)
        {
            if (root != null)
            {
                for (int i = root.childCount - 1; i >= 0; i--) Destroy(root.GetChild(i).gameObject);
            }
            slices.Clear();
        }

        private static void Highlight(List<EmoteWheelSlice> slices, int hoveredIndex)
        {
            for (int i = 0; i < slices.Count; i++)
                if (slices[i] != null) slices[i].SetHighlighted(i == hoveredIndex);
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
