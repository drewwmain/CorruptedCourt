using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
[RequireComponent(typeof(TaskLocation))] // Needs to know where it is
public class TaskDepositStation : MonoBehaviour, IInteractable
{
    [Tooltip("One entry per physical drop slot. Resize this to choose how many items the station can hold - " +
             "the slot layout is generated from the count. 1 slot = dead centre of the station.")]
    public PickupItem[] depositedItemSlots = new PickupItem[1];

    [Tooltip("Optional: hand-placed slot Transforms (e.g. each notch on a weapon rack). A deposited " +
             "item takes each slot's exact position AND rotation. If any are set, the auto grid below is ignored.")]
    public Transform[] customDropSlots;

    [Tooltip("Auto-grid only: local height of the drop slots above the station's origin.")]
    public float slotHeight = 0.5f;

    [Tooltip("Auto-grid only: spacing between slots when there is more than one.")]
    public float slotSpacing = 0.33f;

    public enum RetrieveTestMode { Off, AfterDeposit, Now }

    [Header("Stage-Gated Retrieval")]
    [Tooltip("From this match stage onward, anyone may take deposited items back out (no matching task needed). 999 = never.")]
    public int retrievableFromStage = 999;
    [Tooltip("Off = follow the stage. AfterDeposit = retrievable as soon as it holds an item. Now = always retrievable. (For testing without reaching the stage.)")]
    public RetrieveTestMode retrieveTestMode = RetrieveTestMode.Off;

    [Header("Deposited Item Pose (relative to its drop slot)")]
    [Tooltip("Local position of a deposited item inside its slot - nudge Y up so the item rests on the station.")]
    public Vector3 depositLocalPosition = Vector3.zero;
    [Tooltip("Local rotation (euler) of a deposited item inside its slot.")]
    public Vector3 depositLocalEuler = Vector3.zero;
    [Tooltip("Keep the item's own world size, ignoring any (non-uniform) scale on the station.")]
    public bool preserveDepositedItemScale = true;

    [Header("Deposit Minigame")]
    [Tooltip("Optional: a prefab with a SwordHangMinigame (or similar). When set, interacting with a " +
             "valid item held launches this drag-to-place minigame instead of depositing instantly.")]
    public GameObject depositMinigamePrefab;

    public bool isSabotaged = false;

    private Transform[] dropSlots;
    private TaskLocation taskLocation;

    void Awake()
    {
        taskLocation = GetComponent<TaskLocation>();
        BuildDropSlots();
    }

    // Creates a child Transform per slot, positioned from the slot count.
    private void BuildDropSlots()
    {
        // Hand-placed slots win: use them exactly as authored (position + rotation).
        if (customDropSlots != null && customDropSlots.Length > 0)
        {
            dropSlots = customDropSlots;
            if (depositedItemSlots == null || depositedItemSlots.Length != dropSlots.Length)
                depositedItemSlots = new PickupItem[dropSlots.Length];
            return;
        }

        int count = Mathf.Max(1, depositedItemSlots != null ? depositedItemSlots.Length : 1);

        // Keep the tracking array the same length as the requested slot count.
        if (depositedItemSlots == null || depositedItemSlots.Length != count)
            depositedItemSlots = new PickupItem[count];

        dropSlots = new Transform[count];

        if (count == 1)
        {
            // Single slot: dead centre of the station.
            dropSlots[0] = MakeSlot(0, new Vector3(0f, slotHeight, 0f));
            return;
        }

        // Otherwise lay the slots out in a square-ish grid centred on the station.
        int cols = Mathf.CeilToInt(Mathf.Sqrt(count));
        int rows = Mathf.CeilToInt((float)count / cols);

        for (int i = 0; i < count; i++)
        {
            int col = i % cols;
            int row = i / cols;
            float x = (col - (cols - 1) * 0.5f) * slotSpacing;
            float z = (row - (rows - 1) * 0.5f) * slotSpacing;
            dropSlots[i] = MakeSlot(i, new Vector3(x, slotHeight, z));
        }
    }

    private Transform MakeSlot(int index, Vector3 localPos)
    {
        GameObject slot = new GameObject($"DropSlot_{index}");
        slot.transform.SetParent(this.transform, false);
        slot.transform.localPosition = localPos;
        slot.transform.localRotation = Quaternion.identity;

        // Cancel out the station's (possibly non-uniform) scale so items parented here aren't squished.
        Vector3 s = transform.lossyScale;
        slot.transform.localScale = new Vector3(
            Mathf.Approximately(s.x, 0f) ? 1f : 1f / s.x,
            Mathf.Approximately(s.y, 0f) ? 1f : 1f / s.y,
            Mathf.Approximately(s.z, 0f) ? 1f : 1f / s.z);

        return slot.transform;
    }

    // Public so other systems (e.g. TaskManager auto-spawn) can position an item into a slot.
    public Transform GetDropSlot(int index)
    {
        if (dropSlots != null && index >= 0 && index < dropSlots.Length) return dropSlots[index];
        return transform;
    }

    // Whether deposited items can currently be taken back out freely.
    private bool IsRetrievalUnlocked()
    {
        switch (retrieveTestMode)
        {
            case RetrieveTestMode.Now: return true;
            case RetrieveTestMode.AfterDeposit: return HasReceivedItem();
            default:
                int stage = MatchManager.Instance != null ? MatchManager.Instance.currentStage : 1;
                return stage >= retrievableFromStage;
        }
    }

    // True once at least one item has been deposited here (deposits are name-gated, so any filled
    // slot is the accepted item).
    public bool HasReceivedItem()
    {
        if (depositedItemSlots == null) return false;
        foreach (PickupItem it in depositedItemSlots)
        {
            if (it != null) return true;
        }
        return false;
    }

    public string GetInteractionPrompt()
    {
        return "Press <color=#F4D03F>[E]</color> to use station";
    }

    // Spawns the deposit minigame (SwordHang, ChestDeposit, ...) and hands it the held item, this
    // station, and the player's matching DepositItemStep task so completing it advances the task.
    private void LaunchDepositMinigame(PlayerController player, PickupItem heldItem)
    {
        // Advance any AcquireItemStep this held item already satisfies, so the following
        // DepositItemStep is the current step and the minigame can complete it.
        if (player.activeTasks != null)
        {
            for (int i = player.activeTasks.Count - 1; i >= 0; i--)
            {
                TaskData t = player.activeTasks[i];
                if (t != null && t.GetCurrentStep() is AcquireItemStep)
                    t.EvaluateCurrentStep(player, heldItem.gameObject);
            }
        }

        GameObject mgObj = Instantiate(depositMinigamePrefab);
        ItemDepositMinigame mg = mgObj.GetComponent<ItemDepositMinigame>();
        if (mg == null)
        {
            Debug.LogWarning("[TaskDepositStation] depositMinigamePrefab has no ItemDepositMinigame - depositing instantly instead.");
            Destroy(mgObj);
            int slot = GetFirstAvailableSlotIndex();
            if (slot != -1) DepositIntoSlot(heldItem, slot);
            player.ClearHeldItem();
            return;
        }

        mg.SetupMinigame(player, FindMatchingDepositTask(player, heldItem.itemName));
        mg.BeginDeposit(heldItem, this); // the item stays with the player for the minigame
    }

    private TaskData FindMatchingDepositTask(PlayerController player, string itemName)
    {
        if (player.activeTasks == null) return null;
        foreach (TaskData t in player.activeTasks)
        {
            if (t == null) continue;
            TaskStep step = t.GetCurrentStep();
            if (step is DepositItemStep d && d.targetStationID == taskLocation.locationID
                && (string.IsNullOrEmpty(d.requiredItemName) || d.requiredItemName == itemName))
                return t;
        }
        return null;
    }

    // Places an item into a specific slot (used by the deposit minigame on success).
    public void DepositIntoSlot(PickupItem item, int slotIndex)
    {
        if (item == null || dropSlots == null || slotIndex < 0 || slotIndex >= dropSlots.Length) return;

        item.PlaceInStation(dropSlots[slotIndex], this);
        depositedItemSlots[slotIndex] = item;
        Debug.Log($"{item.itemName} hung on the {taskLocation.locationID} (slot {slotIndex}).");
    }

    public bool IsSlotFree(int slotIndex)
    {
        return depositedItemSlots != null && slotIndex >= 0 && slotIndex < depositedItemSlots.Length
               && depositedItemSlots[slotIndex] == null;
    }

    public int SlotCount => depositedItemSlots != null ? depositedItemSlots.Length : 0;

    public bool HasFreeSlot() => GetFirstAvailableSlotIndex() != -1;
    public string AcceptedItemName => taskLocation != null ? taskLocation.acceptedItemName : "";

    public void OnInteract(GameObject interactor)
    {
        PlayerController player = interactor.GetComponent<PlayerController>();
        if (player == null) return;

        if (isSabotaged && player.currentRole != PlayerRole.Corrupted)
        {
            Debug.Log("Station was sabotaged! You are stunned!");
            player.ApplyStun(3f);
            isSabotaged = false;
        }

        PickupItem heldItem = player.GetHeldItem();

        // --- PHASE 1: DEPOSIT LOGIC (Player is holding an item) ---
        if (heldItem != null)
        {
            int availableSlot = GetFirstAvailableSlotIndex();
            if (availableSlot == -1)
            {
                Debug.Log("Task Area Full! Cannot deposit item.");
                return;
            }

            // A deposit is allowed only when the held item's name exactly matches this location's
            // acceptedItemName - never by whether the player holds a matching task. If the station
            // expects a processed item (e.g. "ProcessedFlowers") the item must actually have been
            // processed, since that is what renames it.
            bool isValidDeposit = !string.IsNullOrEmpty(taskLocation.acceptedItemName)
                                  && heldItem.itemName == taskLocation.acceptedItemName;

            // 2. If it's a valid match, accept the item
            if (isValidDeposit)
            {
                // Drag-to-place minigame, if configured (e.g. hanging a sword on the weapon rack).
                if (depositMinigamePrefab != null)
                {
                    LaunchDepositMinigame(player, heldItem);
                    return;
                }

                heldItem.PlaceInStation(dropSlots[availableSlot], this);
                depositedItemSlots[availableSlot] = heldItem;
                player.ClearHeldItem();

                Debug.Log($"Item {heldItem.itemName} deposited into slot {availableSlot}.");

                // Instantly refresh UI.
                // The PlayerController's PerformInteraction loop will evaluate this immediately after and complete the task step!
                player.RefreshLocalWaypoints();
            }
            else
            {
                if (!heldItem.isProcessed) Debug.Log("This item is not required here, or it needs to be processed first.");
                else Debug.Log("This item is not required here.");
            }
        }
        // --- PHASE 2: RETRIEVAL LOGIC (Player's hands are empty) ---
        else
        {
            bool itemRetrieved = false;

            // Stage-gated free retrieval: once unlocked, ANYONE can take a deposited item out.
            if (IsRetrievalUnlocked())
            {
                for (int i = 0; i < depositedItemSlots.Length; i++)
                {
                    if (depositedItemSlots[i] == null) continue;

                    PickupItem item = depositedItemSlots[i];
                    depositedItemSlots[i] = null;
                    player.EquipItem(item); // AttachToHand also frees the slot / clears currentStation
                    Debug.Log($"Took {item.itemName} out of the {taskLocation.locationID}.");
                    return;
                }
            }

            // Loop through active tasks to see if they need to acquire something from this table
            foreach (TaskData task in player.activeTasks)
            {
                TaskStep activeStep = task.GetCurrentStep();

                // If their current objective is to acquire an item...
                if (activeStep is AcquireItemStep acquireStep)
                {
                    // Look through the station's slots for a match
                    for (int i = 0; i < depositedItemSlots.Length; i++)
                    {
                        PickupItem depositedItem = depositedItemSlots[i];

                        if (depositedItem != null && depositedItem.itemName == acquireStep.requiredItemName)
                        {
                            player.EquipItem(depositedItem);
                            depositedItemSlots[i] = null; // Clear the slot

                            Debug.Log($"Retrieved {depositedItem.itemName} from slot {i}.");
                            itemRetrieved = true;
                            break;
                        }
                    }
                }
                if (itemRetrieved) break;
            }

            if (!itemRetrieved)
            {
                Debug.Log("There are no items here that you need for your current tasks.");
            }
        }
    }

    private int GetFirstAvailableSlotIndex()
    {
        for (int i = 0; i < depositedItemSlots.Length; i++)
        {
            if (depositedItemSlots[i] == null) return i;
        }
        return -1;
    }

    public void ReleaseItem(PickupItem itemToRemove)
    {
        for (int i = 0; i < depositedItemSlots.Length; i++)
        {
            if (depositedItemSlots[i] == itemToRemove)
            {
                depositedItemSlots[i] = null;
                Debug.Log($"Item removed from slot {i}. Space is now available.");
                break;
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;

        // Hand-placed slots: show position + facing.
        if (customDropSlots != null && customDropSlots.Length > 0)
        {
            foreach (Transform s in customDropSlots)
            {
                if (s == null) continue;
                Gizmos.DrawWireSphere(s.position, 0.08f);
                Gizmos.DrawLine(s.position, s.position + s.forward * 0.25f);
            }
            return;
        }

        int count = Mathf.Max(1, depositedItemSlots != null ? depositedItemSlots.Length : 1);

        if (count == 1)
        {
            Gizmos.DrawWireSphere(transform.TransformPoint(new Vector3(0f, slotHeight, 0f)), 0.08f);
            return;
        }

        int cols = Mathf.CeilToInt(Mathf.Sqrt(count));
        int rows = Mathf.CeilToInt((float)count / cols);
        for (int i = 0; i < count; i++)
        {
            int col = i % cols;
            int row = i / cols;
            float x = (col - (cols - 1) * 0.5f) * slotSpacing;
            float z = (row - (rows - 1) * 0.5f) * slotSpacing;
            Gizmos.DrawWireSphere(transform.TransformPoint(new Vector3(x, slotHeight, z)), 0.08f);
        }
    }
}
