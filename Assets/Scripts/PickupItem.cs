using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class PickupItem : MonoBehaviour, IInteractable
{
    [Header("Identity")]
    [Tooltip("The typed identity of this item. Matching moves to this reference; assigned by the " +
             "'Corrupted Court/Migrate Item Definitions' tool.")]
    public ItemDefinition definition;

    [Tooltip("Runtime state flags (processed / deposited-container / spent). Replaces the old " +
             "\"Processed\" / \"Deposited\" itemName prefixes.")]
    public ItemState state;

    // [Obsolete] identity is moving to 'definition'. Kept (and still name-prefixed by ProcessItem /
    // MarkAsDepositedContainer) until matching logic is migrated off strings.
    public string itemName = "Task Tool";
    // A static master list of all items in the map so the UI can find them
    public static List<PickupItem> AllItems = new List<PickupItem>();

    /// <summary>True when this item is <paramref name="def"/> and carries every flag in <paramref name="required"/>.</summary>
    public bool Matches(ItemDefinition def, ItemState required) => definition == def && (state & required) == required;

    /// <summary>True when <paramref name="flag"/> (which may be several ORed flags) is fully set.</summary>
    public bool Has(ItemState flag) => (state & flag) == flag;
    
    [Header("Task Settings")]
    public bool requiresPartner = false;

    [Tooltip("The locationID of the TaskDepositStation this item belongs to. Anyone may deposit it there, task or no task.")]
    public string taskLocationID = "";

    // NEW: Determines if the item is a world spawner or a normal physics item
    [Tooltip("If true, picking this up gives the player a clone and leaves the original on the table.")]
    public bool isInfiniteSource = true;

    // [Obsolete] superseded by ItemState.Processed on 'state'. Still written by ProcessItem() until
    // matching logic is migrated.
    [Tooltip("Used for ItemProcessAndDeposit tasks to track if the player has modified the item.")]
    public bool isProcessed = false;

    // [Obsolete] superseded by ItemState.DepositedContainer on 'state'. Still written by
    // MarkAsDepositedContainer() until matching logic is migrated.
    [Tooltip("Set when a deposit station that received an item is converted into this pickup - " +
             "prefixes the itemName with \"Deposited\".")]
    public bool isDepositedContainer = false;

    // [Obsolete] superseded by ItemState.Spent on 'state'.
    [Tooltip("Set once a minigame has used this item up (e.g. an emptied plate). Pressing [E] on it " +
             "then does nothing - the player just carries or drops it.")]
    public bool isSpent = false;

    // NEW: Marks the item as heavy, triggering movement penalties in the PlayerController
    [Tooltip("If true, the player moves at half speed and cannot jump or sprint while holding this.")]
    public bool isHeavy = false;

    [Header("Two-handed haul")]
    [Tooltip("Carry this in front of the torso with BOTH hands IK-locked to the grip points below - a 'hauling' pose. Best paired with Is Heavy.")]
    public bool haulWithBothHands = false;
    [Tooltip("How far in front of the body the item is carried.")]
    public float haulForward = 0.55f;
    [Tooltip("Height of the item measured DOWN from the player's eyes (negative). ~-0.9 = hip height, ~-0.6 = chest height. Rig-independent.")]
    public float haulHeightBelowEye = -0.9f;
    [Tooltip("Extra rotation applied to the item while hauled (local +Z faces the player's forward, +Y is up). Tune so the chest opening faces up and the front faces away.")]
    public Vector3 haulLocalEuler = Vector3.zero;
    [Tooltip("Child transform where one hand grips (put it on one side of the item). The hands are auto-assigned to whichever grip is actually on that side of the player, so they never cross.")]
    public Transform leftGripPoint;
    [Tooltip("Child transform where the other hand grips (the other side of the item).")]
    public Transform rightGripPoint;
    [Tooltip("How far the elbows bow OUT to the sides while hauling. Raise it if the arms cross or hug the body.")]
    public float haulElbowOut = 0.45f;
    [Tooltip("How far the elbows drop below the hands while hauling.")]
    public float haulElbowDrop = 0.25f;

    // You can adjust these in the inspector to make the item look right in the hand
    [SerializeField] private Vector3 heldPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 heldRotationOffset = Vector3.zero;

    [Header("Physics & Throwing")]
    [Tooltip("Heavier items require more charge to throw far. A weight of 1 is standard.")]
    public float itemWeight = 1f;

    [Header("Minigame Settings")]
    [Tooltip("How the left hand socket should be rotated when processing this specific item.")]
    public Vector3 leftHandSocketRotation = Vector3.zero;

    [Tooltip("If set, pressing [E] while holding this item ALWAYS launches this minigame - any role, with or without a related task.")]
    public GameObject processMinigamePrefab;

    private Rigidbody rb;
    private Collider coll;
    private bool isHeld = false;
    // We store the coroutine so we can cancel it if the player picks the item back up mid-air
    private Coroutine settleCoroutine;
    private TaskDepositStation currentStation;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        coll = GetComponent<Collider>();
    }

    // Registered only while active, so a deactivated item (e.g. a role-switched Vase in round 1)
    // isn't treated as a pickup that exists in the world.
    void OnEnable()
    {
        if (!AllItems.Contains(this)) AllItems.Add(this);
    }

    void OnDisable()
    {
        AllItems.Remove(this);
    }

    void OnDestroy()
    {
        AllItems.Remove(this);
    }

    public string GetInteractionPrompt()
    {
        return $"Press <color=#F4D03F>[E]</color> to pick up <color=#5DADE2>{itemName}</color>";
    }

    public void OnInteract(GameObject interactor)
    {
        if (isHeld) return;

        // Tell the player who clicked this to equip it
        PlayerController player = interactor.GetComponent<PlayerController>();
        if (player != null)
        {
            // --- NEW: THE ANTI-SPAM PROTECTION ---
            // If the player is already holding this exact type of item in EITHER hand, abort!
            if (player.IsHoldingItemNamed(this.itemName))
            {
                Debug.Log($"You are already holding a {this.itemName}. Interaction ignored.");
                return; // Exit the method completely so no cloning or dropping happens
            }
            // -------------------------------------

            if (isInfiniteSource)
            {
                // 1. Create a physical duplicate of the item in the scene
                GameObject cloneObj = Instantiate(this.gameObject);
                PickupItem cloneItem = cloneObj.GetComponent<PickupItem>();
                
                // 2. The clone should act as a normal item, NOT another infinite spawner!
                // This ensures if a player drops the clone, other players can pick it up normally.
                cloneItem.isInfiniteSource = false;
                
                // 3. Clean up the names so Unity doesn't break string matching with "(Clone)"
                cloneItem.itemName = this.itemName;
                cloneObj.name = this.gameObject.name;

                // 4. Force the player to equip the clone instead of the original
                // (Your EquipItem method handles dropping whatever else they might be holding)
                player.EquipItem(cloneItem);
            }
            else
            {
                // Standard pickup logic for items that have been dropped on the floor
                player.EquipItem(this);
            }
        }
    }
    // Called by the Player Controller
    public void AttachToHand(Transform handSocket)
    {
        isHeld = true;

        // If the item was still trying to settle on the ground, stop that process
        if (settleCoroutine != null) StopCoroutine(settleCoroutine);
        // If we are picking this up from a station, tell the station to free up the slot
        if (currentStation != null)
        {
            currentStation.ReleaseItem(this);
            currentStation = null;
        }
        
        // 1. Disable physics so it doesn't fall or push the player
        rb.isKinematic = true;
        coll.enabled = false;
        coll.isTrigger = false; // <-- NEW: Reset trigger state just in case

        // 2. Parent it to the socket (the physical hand bone)
        transform.SetParent(handSocket);

        // 3. Snap it to the center of the hand with your predefined offsets
        transform.localPosition = heldPositionOffset;
        transform.localRotation = Quaternion.Euler(heldRotationOffset);
    }

    // Like AttachToHand but leaves parenting/posing to the caller - used for two-handed haul items
    // that ride in front of the torso instead of in a hand socket.
    public void PrepareForHaul()
    {
        isHeld = true;
        if (settleCoroutine != null) StopCoroutine(settleCoroutine);
        if (currentStation != null) { currentStation.ReleaseItem(this); currentStation = null; }
        rb.isKinematic = true;
        coll.enabled = false;
        coll.isTrigger = false;
    }

    // Called by the Player Controller if they pick up something else
    public void DetachFromHand()
    {
        isHeld = false;
        
        // 1. Unparent it from the player
        transform.SetParent(null);
        
        // 2. Re-enable physics so it falls to the ground
        rb.isKinematic = false;
        coll.enabled = true;
        coll.isTrigger = false; // <-- NEW: Must be false so it physically bounces on the floor!

        // Also, explicitly reset its velocity so it doesn't carry momentum from the player moving
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // 3. Add a small forward toss so it doesn't get stuck inside the Player's Capsule Collider
        // (Because it was parented to the camera, transform.forward is the direction you are looking)
        rb.AddForce(transform.forward * 0.5f, ForceMode.Impulse);

        // Start the process of waiting for it to hit the ground and stop
        settleCoroutine = StartCoroutine(SettlePhysics());
    }

    // Release the item to the world right where it is, with no toss (used by the sword-hang
    // minigame when the player "lets go" so it drops onto the rack). Resets isHeld so the item
    // can be picked back up again, unlike a raw unparent.
    public void DropInPlace()
    {
        if (settleCoroutine != null) StopCoroutine(settleCoroutine);
        if (currentStation != null) { currentStation.ReleaseItem(this); currentStation = null; }

        isHeld = false;
        transform.SetParent(null);

        rb.isKinematic = false;
        rb.useGravity = true;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        coll.enabled = true;
        coll.isTrigger = false;

        settleCoroutine = StartCoroutine(SettlePhysics());
    }

    // This runs in the background over multiple frames
    private IEnumerator SettlePhysics()
    {
        // Wait a tiny bit right after dropping so gravity has time to pull it down
        yield return new WaitForSeconds(0.2f);

        // While the item is still moving faster than a tiny threshold, keep waiting...
        while (rb.linearVelocity.sqrMagnitude > 0.01f)
        {
            // Wait 1/10th of a second, then check again (saves performance)
            yield return new WaitForSeconds(0.1f); 
        }

        // Once it stops moving, lock it down so nothing else can push it!
        if (!isHeld)
        {
            rb.isKinematic = true;
        }
    }

    // Called by the Task Deposit Station
    public void PlaceInStation(Transform slot, TaskDepositStation station)
    {
        // Cancel the dropping animation if it was happening
        if (settleCoroutine != null) StopCoroutine(settleCoroutine);

        isHeld = false;
        currentStation = station; // Remember which station we are attached to

        Vector3 worldScale = transform.lossyScale;

        // Parent to the drop slot so the item sits IN the station and travels with it. Hand-placed
        // slots carry their own pose (a weapon-rack notch); the station offsets are added on top.
        transform.SetParent(slot, false);
        transform.localPosition = station != null ? station.depositLocalPosition : Vector3.zero;
        transform.localRotation = Quaternion.Euler(station != null ? station.depositLocalEuler : Vector3.zero);

        // Keep the item's real-world size no matter how the slot / station is scaled.
        if (station == null || station.preserveDepositedItemScale)
        {
            Vector3 sp = slot.lossyScale;
            transform.localScale = new Vector3(
                Mathf.Approximately(sp.x, 0f) ? worldScale.x : worldScale.x / sp.x,
                Mathf.Approximately(sp.y, 0f) ? worldScale.y : worldScale.y / sp.y,
                Mathf.Approximately(sp.z, 0f) ? worldScale.z : worldScale.z / sp.z);
        }

        // Lock physics completely so it cannot be pushed or moved
        rb.isKinematic = true;
        
        // UPDATED: Disable the collider completely so NO ONE can interact with or pick up the item anymore!
        coll.enabled = false; 
    }

    
    // Now receives the specific target player directly from the PlayerController
    public void PerformMultiplayerTask(PlayerController otherPlayer)
    {
        if (!isHeld) return;

        if (requiresPartner)
        {
            // SUCCESS!
            Debug.Log($"--- {itemName.ToUpper()} TASK COMPLETED WITH {otherPlayer.gameObject.name}! ---");
            
            // FUTURE: Add RPC calls here for networking, and potentially destroy the item 
            // if the task consumes it (e.g., Destroy(gameObject);).
        }
    }

    // --- NEW: PROCESS ITEM LOGIC ---
    public void ProcessItem()
    {
        if (isProcessed) return; // already processed - don't stack the prefix

        // New: typed flag. Legacy: keep prefixing the name so string matching still works.
        state |= ItemState.Processed;

        isProcessed = true;
        itemName = "Processed" + itemName; // no space: "Flowers" -> "ProcessedFlowers"
        gameObject.name = itemName;
        Debug.Log($"The {itemName} has been successfully modified/processed!");
    }

    // Called when a deposit station that received an item is converted into this pickup.
    // Prefixes the name (no space) so only stations expecting the "Deposited" version accept it.
    public void MarkAsDepositedContainer()
    {
        if (isDepositedContainer) return; // don't stack the prefix

        // New: typed flag. Legacy: keep prefixing the name so string matching still works.
        state |= ItemState.DepositedContainer;

        isDepositedContainer = true;
        itemName = "Deposited" + itemName; // no space: "Vase" -> "DepositedVase"
        gameObject.name = itemName;
        Debug.Log($"{itemName} now carries a deposit.");
    }
}