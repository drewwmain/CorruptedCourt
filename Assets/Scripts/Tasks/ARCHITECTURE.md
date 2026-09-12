# Task System Architecture

Revision 4. Supersedes revisions 1–3. Companion document: *Corrupted Court — Task Implementation
Specification* (consolidated edition), which is the authority on **what** the 30 tasks do. This
document is the authority on **how** they are built.

> **Status:** design proposal — nothing in Parts I–V is implemented yet. Part III is an audit of
> the *pre-existing* live codebase against this design (its "already correct" list is code that
> predates this document, not work done under it). All Part VI items are now ruled (see Part VI) —
> the B1–B19 prompt pack lives at `Assets/Scripts/Tasks/IMPLEMENTATION_PROMPTS.md`.

---

## 0. How to use this document

Part I restates the task model as the architecture sees it. Part II is the architecture itself,
layer by layer. Part III reconciles it against the current repo — what already exists, what
changes, what gets deleted. Part IV is the build order. Part V is the fixed per-task contract for
the one-at-a-time implementation loop. Part VI lists the few things still open.

When prompts get requested, §IV.2 is the table to work from: each row is one prompt, and each
names its dependencies and the files it touches.

### 0.1 What changed from revision 3

Revision 3 was written against a stale picture of the repo. The audit in Part III found that four
of its planned phases are already shipped, and two of its proposals are wrong for the code as it
now stands.

| Revision 3 said | Reality |
|---|---|
| T0 — Step surgery. Un-nest five step types hiding inside `DepositItemStep`; highest-risk phase in the plan | Already done. All 11 step types are top-level, one file each under `Tasks/Steps/`, and `WaypointManager`'s `using static DepositItemStep;` is gone |
| Items need typed identity and variant state flags | Already done. `PickupItem.Matches(ItemDefinition, ItemState)` with `Processed` / `DepositedContainer` / `Spent`. No runtime renaming anywhere |
| `taskTier` needs adding | Already exists, with `TaskManager.tierWeights = {1, 2, 4}` and lobby-sized meter targets |
| Base items need infinite spawners | Already exists — `PickupItem.isInfiniteSource`, default true |
| Add a `MutualPlayerInteractStep` alongside `PlayerInteractStep` | Reversed. The spec now folds both into `PlayerInteractStep`; `MutualPlayerInteractStep` is deleted |
| `MinigameChain` for cheers → drink | Dropped. No task chains minigames any more |
| Facing checks on `EmoteStep` | Dropped. The two tasks that needed facing are minigames now |

Net effect: the plan is shorter at the front and longer in the middle. The risky serialization
surgery is behind us; the work that remains is station state, the emote system, and the
partner-minigame foundation.

---

# Part I — The task model

## 1. The nine rules

Restated from the specification, in the form the architecture enforces them.

| # | Rule | Enforced by |
|---|---|---|
| R1 | Any action is open to any player; only credit is gated | Minigames never check for a matching task. `TaskManager.CompleteTask` gates the meter on faction |
| R2 | Availability replaces prerequisites — tasks wait on items and station states, never on other tasks | `ItemSourceResolver` walking `ItemRecipe`s back to an infinite spawner |
| R3 | One truth per station and per item | State lives on the station component. One exception: the `ConfirmationBox` is per-player |
| R4 | Stations are reversed by players, not by a timer | `TaskStationState` + `StationReversal`. No scheduler exists |
| R5 | Items are shared; nothing decays; one staged drop per player | `DroppedItemRegistry`. Forced drops exempt |
| R6 | Partners are anyone, including the Corrupted; refusing is free | `PartnerLink` consent, graceful cancel |
| R7 | Competition is cosmetic — both participants complete when a session ends | `MinigameBase.participants` |
| R8 | Minigames are explicit; everything else resolves on the qualifying action | `TaskStep.minigamePrefab` null or not |
| R9 | Nothing is renamed at runtime; variants are state | `ItemState` flags + `ItemPayload` + variant display names |

## 2. Lifecycle

```
stage begins
   │
   ├─ TaskManager hands each living non-King player a fresh allotment
   │  plus carried-over incomplete work
   ▼
player acts  ──►  an evaluation event fires
   │              (interact · inventory change · zone change · emote performed
   │               · station condition change · minigame completed)
   ▼
for each active assignment: does the CURRENT step accept this event?
   │
   ├─ no  ──► nothing happens (the world action still happened — R1)
   │
   └─ yes ──► step carries a minigame?
                 ├─ yes ──► launch; completes on success, for EVERY participant
                 │          holding a matching step
                 └─ no  ──► completes now
                              ▼
                    advance; auto-advance following steps already satisfied
                              ▼
                    last step? ──► TaskManager.CompleteTask
                                     Court: meter += tierWeight
                                     Corrupted: logged, meter unchanged
```

Two behaviours that must survive every change below:

- **Regression.** An item leaving the player's hands steps a satisfied `AcquireItemStep` back
  (`TaskInstance.CheckForTaskRegression`). Under R5 this is recoverable — the dropped item is
  still on the floor.
- **Reversal never revokes.** Snuffing a candle someone lit does not un-complete their step. Step
  progress is per-player and permanent; station condition is world state.

## 3. The 30 tasks, structurally

| Shape | Pattern | # |
|---|---|---|
| Fetch & Place | `Acquire(X) → Deposit(X → S)` | 10 |
| Station Action | `Station(S)` | 6 |
| Emote Performance | `[Acquire] → [Navigate] → Emote` | 4 |
| Partner Physical | `[Acquire] → PlayerInteract(X, X)` | 3 |
| Produce then Place | `Station(S) → Acquire(X) → Deposit(X → S2)` | 2 |
| Fetch & Consume | `Acquire(X) → Consume(Y)` | 2 |
| Fetch, Process & Place | `Acquire → Process → Deposit` | 1 |
| Loadout & Act | `Acquire → Acquire → Station(S)` | 1 |
| Order Recall | `OrderRecall(Src → In)` | 1 |

57 steps. `AcquireItemStep` ×20, `DepositItemStep` ×12, `StationInteractStep` ×9, `EmoteStep` ×4,
`NavigateStep` ×4, `PlayerInteractStep` ×3, `ProcessItemStep` ×2, `ConsumeItemStep` ×2,
`OrderRecallStep` ×1.

24 tasks carry a minigame; every stage 1 task does.

Tier spread is 10 / 10 / 10. That is worth noting because `TaskManager.InitializeCourtMeter` sizes
the meter from `TierWeightAverage()` — the unweighted mean of `tierWeights`, currently 2.333. An
even spread across the three tiers makes that mean exactly the true average value of a randomly
drawn task, so the meter target is calibrated rather than approximated. Skewing the tier
distribution later would silently mis-size the meter unless `TierWeightAverage()` is changed to
weight by how many tasks sit in each tier.

---

# Part II — Architecture

## 4. Invariants

Restated in every implementation prompt.

- Authoring data is immutable at runtime. `TaskData`, `TaskStep`, `ItemDefinition`,
  `EmoteDefinition`, `ItemRecipe`. Per-player state on `TaskInstance` / `TaskStepRuntime`;
  per-object state on the scene component.
- One mutation surface per kind of state. Items → `ItemLifecycle`. Station condition →
  `TaskStationState`. Step advancement → `TaskInstance`. Emote performance → `PlayerEmotes`. A
  minigame never calls `Instantiate` / `Destroy` / `SetActive` on gameplay state; it requests.
- Identity is typed and never renamed. `(ItemDefinition, ItemState)` for items;
  `TaskLocation.locationID` through `TaskStep.ResolveLocation` for stations; asset reference or
  category for emotes.
- Steps describe, systems decide. A step declares requirements. It does not raycast, spawn, or
  drive UI.
- Any action, gated credit. Every minigame prefab must be launchable by a player with no matching
  task.
- Everything runs solo. Every partner feature ships with a `DummyPartner` path.
- Additive migration. New behaviour arrives as new types or virtual overloads with forwarding
  defaults. `[SerializeReference]` type names in `TaskData` assets remain load-bearing.
- Subclasses override `OnMinigameUpdate`, never `Update`. The existing house rule; declaring
  `Update()` hides the base loop.

## 5. Layer map

```
  ┌────────────────────────────────────────────────────────────────────┐
  │ A · AUTHORING   ItemDefinition · ItemState · ItemIdentity           │
  │                 ItemDefinitionSet · ItemRecipe · TaskData           │
  │                 TaskStep(×11) · EmoteDefinition · MatchConfig       │
  └──────────────┬─────────────────────────────────────────────────────┘
  ┌──────────────▼─────────────────────────────────────────────────────┐
  │ B · TASK RUNTIME   TaskManager · TaskInstance · TaskStepRuntime     │
  │                    PlayerTaskBook · TaskEvalContext                 │
  └──┬────────────┬──────────────┬──────────────┬──────────────────────┘
  ┌──▼───────┐ ┌──▼──────────┐ ┌─▼────────────┐ ┌▼───────────────────┐
  │ C·       │ │ D·          │ │ E·           │ │ F·                 │
  │ STATIONS │ │ ITEMS       │ │ MINIGAMES    │ │ EMOTES             │
  │          │ │             │ │              │ │                    │
  │TaskSta-  │ │ItemLifecycle│ │MinigameBase  │ │EmoteWheelController│
  │tionState │ │DroppedItem- │ │+ families    │ │PlayerEmotes        │
  │Station-  │ │ Registry    │ │PartnerLink   │ │EmoteNameplate      │
  │Reversal  │ │ItemPayload  │ │Participants  │ │EmoteCategory       │
  │Station-  │ │ItemSource-  │ │ToolLoan      │ │                    │
  │Registry  │ │ Resolver    │ │Product       │ │                    │
  └──┬───────┘ └──┬──────────┘ └─┬────────────┘ └┬───────────────────┘
     └────────────┴──────────────┴───────────────┘
  ┌──────────────▼─────────────────────────────────────────────────────┐
  │ G · EVENTS & VIEWS   GameEvents · ObjectiveResolver                 │
  │                      WaypointManager · UIManager                    │
  └────────────────────────────────────────────────────────────────────┘
```

Dependencies point downward and inward. Views subscribe; they never drive. This already holds and
must not break.

## 6. Layer A — Authoring and identity

### 6.1 ItemIdentity

```csharp
[System.Serializable]
public struct ItemIdentity {
    public ItemDefinition definition;
    public ItemState state;          // every flag must be present
    public bool Matches(PickupItem item) => item != null && item.Matches(definition, state);
}
```

A convenience wrapper over the pair the code already passes separately. Used by recipes,
production and objective resolution — not retrofitted into existing step fields, which already
carry the pair and whose serialized layout should not be disturbed.

### 6.2 Variant display names

Nothing is renamed (R9), so "Polished Sword" has to be derived from `Sword` + `Processed`:

```csharp
// added to ItemDefinition
[System.Serializable] public struct VariantName {
    public ItemState whenState;
    public string displayName;
}
public List<VariantName> variantNames;
public string DisplayNameFor(ItemState state);   // most-specific flag match wins; falls back to displayName
```

Every objective string and interaction prompt routes through `DisplayNameFor`. Four variants need
entries: `Sword+Processed`, `Plate+DepositedContainer`, `Platter+DepositedContainer`,
`Glass+DepositedContainer`.

### 6.3 ItemPayload — the deposited-into record

`ItemState.DepositedContainer` says that a container was filled. Four chains need to know what and
how many:

```csharp
public class ItemPayload : MonoBehaviour {     // sits beside PickupItem
    public ItemDefinition contents;            // CakePiece · TurkeyLeg · Wine · Arrow
    public int count;
    public int capacity = 1;                   // Quiver: 10
}
```

Written by `RoundRoleSwitch.TransferDepositedItems` as a station becomes a pickup, and by the pour
minigame. Read by `ConsumeItemStep` (eat the cake off the plate) and `AcquireItemStep` (a quiver
with arrows in it).

### 6.4 ItemDefinitionSet

```csharp
[CreateAssetMenu] public class ItemDefinitionSet : ScriptableObject {
    public List<ItemDefinition> members;
    public bool Contains(ItemDefinition def);
}
```

One asset needed: `Instrument = {Guitar, Trumpet, Flute}`. Consumed by
`AcquireItemStep.requiredItemSet` and `EmoteDefinition.requiresHeldItem`. Do not generalise
further.

### 6.5 ItemRecipe — the production graph

R2's entire weight. A recipe is a lookup table, not an execution engine — minigames still do the
producing.

```csharp
[CreateAssetMenu] public class ItemRecipe : ScriptableObject {
    public ItemIdentity output;
    public ItemIdentity[] inputs;
    public string producerLocationID;
    public ProductionKind kind;      // FlagHeldItem | SpawnAtStation | DepositIntoContainer
    public TaskData producingTask;   // documentation only
    [TextArea] public string objectiveHint;   // "Polish a sword at the Armory"
}
```

Seven assets, mirroring §3.3 of the specification: `Sword→Processed`; `Cake→CakePiece`;
`Turkey→TurkeyLeg`; `CakePiece→Plate`; `TurkeyLeg→Platter`; `WinePitcher→Glass`; `Arrow→Quiver`. A
`RecipeIndex` asset makes them queryable by output identity.

## 7. Layer B — Runtime and evaluation

### 7.1 TaskEvalContext

Today a step is judged by `CheckCompletion(player, GameObject target, TaskStepRuntime runtime)`.
That signature assumes something was interacted with. Three new behaviours have no interaction
target: an emote is ambient, a zone change is passive, a station reversal happens elsewhere.
Rather than fake a `GameObject`:

```csharp
public enum TaskEvalReason {
    Interact, InventoryChanged, ZoneChanged,
    EmotePerformed, StationConditionChanged, MinigameCompleted, PartnerAction
}

public class TaskEvalContext {
    public PlayerController Player;
    public TaskEvalReason Reason;
    public GameObject Target;          // what today's callers pass
    public TaskLocation Location;      // resolved once, not per step
    public PickupItem Item;
    public PlayerController Partner;
    public EmoteDefinition Emote;
    public bool SkipMinigame;
}
```

`TaskStep` gains one virtual with a forwarding default — precisely the pattern `TaskStepRuntime`
was introduced with, so no existing step changes:

```csharp
public virtual bool CheckCompletion(TaskEvalContext ctx, TaskStepRuntime runtime)
    => CheckCompletion(ctx.Player, ctx.Target, runtime);
```

`PlayerTaskBook.EvaluateActiveTasks` gains a context overload; the existing
`(GameObject, bool, bool)` signature builds one internally and keeps working for all current call
sites.

Six call sites raise contexts:

| Source | Reason |
|---|---|
| `PlayerInteractor` interaction | `Interact` |
| `PlayerInventory` equip / drop / swap | `InventoryChanged` |
| `TaskZone` enter / exit | `ZoneChanged` |
| `PlayerEmotes.Perform` | `EmotePerformed` — for the performer and every player in scan range |
| `TaskStationState` condition change | `StationConditionChanged` |
| `MinigameBase.CompleteMinigame` | `MinigameCompleted` — once per participant |

### 7.2 Step inventory after this work

| Step | Fate |
|---|---|
| `AcquireItemStep` | + `requiredItemSet`, + `ItemPayload` awareness |
| `DepositItemStep` | + `requireOwnDeposit` becomes the authoring default (see §III.4) |
| `StationInteractStep` | seats replace the `OverlapSphere` count; + `requiresStationPending` |
| `ProcessItemStep` | unchanged; two uses (`sword_armory`, `wine_emptyGlass`) |
| `ConsumeItemStep` | + accept the held container's `ItemPayload.contents` |
| `PlayerInteractStep` | absorbs the two-sided form — `myRequiredItem` + `targetRequiredItem`, either nullable |
| `NavigateStep` | unchanged |
| `MutualPlayerInteractStep` | deleted (migrated into `PlayerInteractStep`) |
| `DataRetrievalStep` | deleted — no task uses it; `OrderRecallStep` replaces it |
| `EquipClothingStep` | deleted — unused, and the codebase's last `name.Contains` holdout |
| `GroupNavigateStep` | deleted — unused |
| `EmoteStep` | new (§9.5) |
| `OrderRecallStep` | new (§7.3) |

Four deletions and one merge. All five rewrite `[SerializeReference]` type strings, so they share
one commit and one reserialize pass — see §III.7 for the procedure.

### 7.3 OrderRecallStep

```csharp
public class OrderRecallStep : TaskStep {
    public string sourceStationID;    // Ledger
    public string inputStationID;     // ConfirmationBox
    public int sequenceLength = 3;
}
```

Runtime state on `TaskStepRuntime`, replacing the `HasCode` / `GeneratedCode` /
`CodeRevealPending` / `DataInputPending` quartet with `ObservedSequence` / `HasObserved` /
`SequenceRevealPending` / `InputPending`. The one-shot "pending" flags and the `LocalTasksChanged`
signal that drives the UI off them are a working pattern — keep the mechanism, change the payload.

The sequence belongs to the Ledger station and is fixed for the match. The step copies it into the
runtime when the player reads it. The `ConfirmationBox` result is per-player — which the existing
architecture gets right for free, since `TaskStepRuntime` is already per-player and `UIManager`
already reads pending flags off the local player's runtime.

## 8. Layer C — Stations

This is the largest genuinely new subsystem.

### 8.1 TaskStationState

Sits beside `TaskLocation`. R3 and R4 hang on it.

```csharp
public class TaskStationState : MonoBehaviour {
    public enum Condition { Pending, Busy, Satisfied }

    [SerializeField] private int seatCapacity = 1;
    [SerializeField] private bool playerReversible = true;
    [SerializeField] private string revertVerb = "reset";     // "snuff" · "tilt" · "re-roll"
    [SerializeField] private float revertHoldSeconds = 1.0f;
    [SerializeField] private GameObject pendingVisual, satisfiedVisual;

    public Condition Current { get; private set; }
    public PlayerController SatisfiedBy { get; private set; }   // logging only, never gating

    public bool TryClaimSeat(PlayerController p);
    public void ReleaseSeat(PlayerController p);
    public bool SeatsFull { get; }

    public void MarkSatisfied(PlayerController by);
    public bool TryRevert(PlayerController by);
}
```

- `Pending` = the action is available here (unlit candle, crooked painting, rolled banner).
  `Satisfied` = done. `Busy` = someone is mid-minigame.
- Seats replace proximity. `StationInteractStep` currently counts players with
  `Physics.OverlapSphere`, which is a poor fit for the chess board's two seats — two players
  standing near it is not the same as two players at it. Claims are explicit and also prevent a
  station being reverted out from under an open minigame.
- Visuals are two `GameObject` roots or an animator parameter, driven only by condition changes.
  No minigame touches them.
- Condition changes raise `GameEvents.StationConditionChanged`.
- Deposit stations derive their condition from `TaskDepositStation.HasReceivedItem()` rather than
  storing a second copy. That method already exists.

### 8.2 StationReversal

An `IInteractable` active only while `Current == Satisfied && playerReversible`:

- Prompt: `Hold [E] to snuff the candle`, from the authored `revertVerb`.
- Hold `revertHoldSeconds` → `TryRevert()` → `Pending`. No minigame, no tool.
- Uncredited and unrestricted (R1, R4). Never touches a completed step.
- On a deposit station, reversal is retrieval. `TaskDepositStation` already has stage-gated
  retrieval (`retrievableFromStage`, `RetrieveTestMode`). Wire reversal to that path rather than
  building a second one.

Reversibility per station, from §3.4 of the specification: reversible are Candle (snuff), Painting
(tilt), Banner (re-roll) and every deposit station (take back). Not reversible are Cake, Turkey,
EmptyGlass, WeddingContract, ChessBoard, ArcheryTarget, Ledger, ConfirmationBox.

### 8.3 StationRegistry

Singleton indexing `TaskStationState` by `locationID`, mirroring the existing
`TaskLocation.AllLocations` pattern.

```csharp
IReadOnlyList<TaskStationState> ByLocation(string locationID);
TaskStationState FindNearestPending(string locationID, Vector3 from);
int PendingCount(string locationID);
```

`FindNearestPending` is what makes waypoints honest under R4 — the marker points at an unlit
candle, not the nearest candle. `PendingCount` reaching zero means every task needing that station
is stalled until someone reverses one; that must surface in the debug panel and in the player's
objective hint.

## 9. Layer D — Items

### 9.1 ItemLifecycle

One surface for every item state change, per invariant 2:

```csharp
PickupItem Spawn(PickupItem prefab, ItemIdentity identity, Vector3 pos, Quaternion rot);
void GiveToHand(PlayerController player, PickupItem item, Hand hand);
void SetState(PickupItem item, ItemState add, ItemState remove);
void SetPayload(PickupItem item, ItemDefinition contents, int count);
void PlaceInStation(PickupItem item, TaskDepositStation station, int slot);
void Drop(PlayerController dropper, PickupItem item, DropCause cause);
void Despawn(PickupItem item);
```

Everything here exists today, scattered across `PickupItem`, `PlayerInventory` and
`TaskDepositStation`. Consolidating is what makes R5 and a future network layer tractable.

### 9.2 DroppedItemRegistry — R5

```csharp
public enum DropCause { Manual, Thrown, Forced }

void RegisterDrop(PlayerController dropper, PickupItem item, DropCause cause);
void Clear(PickupItem item);              // anyone picked it up
PickupItem OutstandingFor(PlayerController p);
```

- On a `Manual` or `Thrown` drop, if the dropper already has an outstanding dropped item, despawn
  that one now.
- Forced drops are exempt — the pre-meeting scramble's `PlayerInventory.DropHeavyItems` must not
  consume a drop slot or destroy an earlier drop.
- Anyone picking the item up clears it, freeing the dropper's slot.
- Station placements, consumption and hand-to-hand moves are not drops.
- Despawning the previous drop should be visible — a fade or puff, not an instant vanish, or it
  reads as a bug.

### 9.3 ItemSourceResolver — R2's answer

```csharp
public struct ObjectiveTarget {
    public Transform Transform;
    public string Hint;
    public bool IsDirect;   // true = the thing itself; false = a step up the chain
}

bool ExistsInWorld(ItemIdentity id, out PickupItem found);
ObjectiveTarget Resolve(ItemIdentity id, Vector3 from);
```

`Resolve` asks: is an instance in the world? Point at it. Otherwise find the recipe whose output
matches and resolve its inputs the same way, depth-capped at 3 and cycle-guarded, terminating at
an infinite spawner. This turns "Find and pick up: Polished Sword" — useless when none exists —
into "Polish a sword at the Armory."

`PickupItem.AllItems` and `isInfiniteSource` already give the resolver everything it needs on the
world-scan side.

### 9.4 MinigameProduct

```csharp
[System.Serializable] public class MinigameProduct {
    public enum Mode { FlagHeldItem, SpawnAtStation, SpawnIntoHand, FillContainerStation }
    public Mode mode;
    public ItemState addFlags;
    public PickupItem spawnPrefab;
    public ItemDefinition payloadContents;
    public int payloadCount = 1;
    public bool consumeSourceItem;
}
```

Serialized as a list, not a single entry — the pour minigame does two things at once:
`FillContainerStation(Wine)` on the glass and `addFlags = Spent` on the held pitcher. Applied on
success through `ItemLifecycle`. `SpawnAndCarryObjective` already covers the spawn case and folds
in here.

## 10. Layer E — Minigames

The existing tree stands. `MinigameBase` → `PanelMinigame` / `HandMinigame` / `PartnerMinigame`,
with `ItemDepositMinigame`, `ToolOnTargetMinigame`, `DragObjectMinigame`, `PourMinigame`,
`ConsumeMinigame`, `ChargeReleaseMinigame` beneath `HandMinigame`. All capabilities stay.

### 10.1 Participants

`MinigameBase` completes one task for one player. Four tasks are shared sessions with two
participants who may hold different tasks, or none: `game_play`, `handShake_court`,
`wineGlass_cheers`, `polishedSword_duel`.

```csharp
public struct MinigameParticipant { public PlayerController Player; public TaskInstance Task; }

protected readonly List<MinigameParticipant> participants;
public void AddParticipant(PlayerController p, TaskInstance task);
```

`SetupMinigame` seeds a one-element list from the existing player / `activeTask` fields, so all 20
single-player minigames are unaffected. `CompleteMinigame()` iterates and calls `FinishMinigame`
per participant. A participant without the task gets nothing (R1); the outcome is irrelevant (R7).

### 10.2 PartnerLink and PartnerHandMinigame

The blocking structural problem. Three tasks need both hand-rig plumbing and partner resolution.
`HandMinigame` owns the first (freeze, RMB look, footwork leash, `MouseWorld`, `MinigameHandRig`,
grip constraint). `PartnerMinigame` owns the second (`PartnerResolver`, `DummyPartner`,
`FacePartner`, `MirrorOnPartner`). They are siblings under `MinigameBase`, so nothing can inherit
both. CLAUDE.md already flags this: "`PartnerMinigame` does not extend `HandMinigame` — handshake
/ duel must compose `MinigameHandRig` themselves."

Composing the hand rig by hand in three subclasses is exactly the duplication `HandMinigame` was
created to kill. The fix inverts it:

```csharp
public class PartnerLink {                      // plain class, no MonoBehaviour
    public PlayerController Partner { get; }
    public bool PartnerIsDummy { get; }
    public bool PartnerAccepted { get; }
    public void Resolve(MinigameContext ctx, PlayerController initiator,
                        GameObject dummyPrefab, float distance);
    public void FaceEachOther();
    public void Mirror(string animTrigger);
    public void Dismiss();
}

public abstract class PartnerHandMinigame : HandMinigame {
    protected PartnerLink Link;                 // resolved in OnHandMinigameBegin
}
```

`PartnerMinigame` keeps working by composing the same `PartnerLink`, so nothing already written
breaks. The extraction is mechanical — `PartnerMinigame`'s body moves into `PartnerLink` almost
verbatim.

### 10.3 MutualReachMinigame

Handshake and toast share one mechanic:

```csharp
public abstract class MutualReachMinigame : PartnerHandMinigame {
    [SerializeField] private float anchorHeight = 1.2f;
    [SerializeField] private float reachTolerance = 0.15f;
    [SerializeField] private float consentTimeout = 8f;
}
```

Per the specification: one player interacts, which puts their hand out; the second may interact
back, which puts theirs out; both then guide their hand to a shared invisible anchor and confirm
with left click.

Two decisions worth fixing here rather than per-task:

- The anchor belongs to the session, spawned at the midpoint and despawned on end — not parented
  to either player. This is the thing that needs replicating first when networking lands.
- Refusal is silent and free. A partner who never reaches simply times out; the session cancels
  with nothing changed. This is the Corrupted's cheapest sabotage under R6, so it must fail
  gracefully and visibly rather than hang.

Concretes: `HandshakeMinigame` (empty hands) and `CheersMinigame` (glass rims meet instead of
palms). `DuelMinigame : PartnerHandMinigame` is separate — mouse-swing arm control and a three-hit
counter, no shared anchor.

### 10.4 MinigameToolLoan

```csharp
[System.Serializable] public class MinigameToolLoan {
    public PickupItem toolPrefab;
    public bool stashHeldItems = true;
    public void Begin(PlayerController p);   // stash both hands, spawn + equip the tool
    public void End();                       // despawn the tool, restore the stash
}
```

Stashing rather than dropping matters even without decay: force-dropping a polished sword to light
a candle would consume the player's one drop slot (R5). `End()` runs on success and cancel.
`PlayerInventory.itemSwappedToLeftHand` / `ReturnSwappedItem` is the existing single-item version
of this and is the right starting point.

### 10.5 Minigame inventory — 24 tasks

| Family | # | Tasks |
|---|---|---|
| `ItemDepositMinigame` | 7 | flower_vase, jewels_dowry, coin_giftBox, firework_fireworkStand, arrow_quiver, giftBox_tentTable, polishedSword_weaponRack |
| `ToolOnTargetMinigame` | 6 | cake_cakePiece, turkey_turkeyLeg, sword_armory, candle_light, weddingContract_sign, fireworkStand_light |
| `MutualReachMinigame` | 2 | handShake_court, wineGlass_cheers |
| `DragObjectMinigame` | 2 | banner_straighten, painting_straighten |
| `ConsumeMinigame` | 2 | cakePlate_eat, turkeyLeg_eat |
| `PourMinigame` | 1 | wine_emptyGlass |
| `ChargeReleaseMinigame` | 1 | bowAndQuiver_shoot |
| `PanelMinigame` | 1 | ledger_check |
| `SharedStationMinigame` | 1 | game_play |
| `DuelMinigame` | 1 | polishedSword_duel |

`EmoteMinigame` and `InstrumentMinigame` (+ Wind / String) become unreachable — the four emote
tasks are satisfied by performing an emote, not by opening a minigame. Delete them.

## 11. Layer F — Emotes

Four tasks depend on it, and it is the one subsystem with nothing behind it but a stub.

### 11.1 Categories

```csharp
public enum EmoteCategory { Gesture, Speech, Dance, Music }
```

Replaces `{ Speech, Dance, Conversation, Gesture, Taunt }`. Conversation and Taunt go; Music
arrives. Gesture now carries no task — handshake and toast became minigames — so it exists purely
for roleplay, which is desirable: the wheel should not read as a task menu.

### 11.2 Definition and performance

```csharp
// EmoteDefinition gains:
public string emoteID;
public string broadcastVerb;                 // "waltzes" · "strums" · "argues"
public ItemDefinitionSet requiresHeldItem;   // Music: trumpet emote ↔ trumpet
// and loses: partnered (the step owns partner rules now)

public class PlayerEmotes : MonoBehaviour {   // sibling of PlayerController
    public EmoteDefinition Current { get; }
    public bool IsPerforming { get; }
    public float StartedAt { get; }
    public float EndsAt { get; }
    public void Perform(EmoteDefinition emote);
    public void Stop();
}
```

`PlayerEmotes` is the missing half. Today `EmoteWheelController.Commit` fires an animator trigger
and forgets — there is no notion of an emote being in progress, which is exactly what every
partner-overlap check reads. `Perform` plays the clip, holds `IsPerforming` for `durationSeconds`
(or until `Stop` when looping), raises `GameEvents.EmotePerformed`, and shows the nameplate.

`broadcastVerb` is authored rather than derived. English third-person is nearly regular and a
naive rule gets the current examples right, but it fails on the first irregular emote added later;
one string per asset costs nothing.

### 11.3 Broadcast

`EmoteNameplate` — a world-space billboard reading `"<DisplayName> <broadcastVerb>."`:

> Drew waltzes. · Drew strums. · Drew argues.

It must use `PlayerController.displayName`, not the GameObject name, so `StolenHeraldry` carries
through — the codebase already holds that distinction carefully and this is a new place to get it
wrong.

### 11.4 The wheel

`EmoteWheelController` exists with the right shape: singleton, catalogue, `openKey = KeyCode.B`
(already correct), `Open` / `Commit` / `Cancel`. What is missing is the UI and a second ring.

- Hold B → outer ring of the four categories. Hover a category → inner ring of its emotes. Release
  on an emote to commit, off the wheel to cancel.
- `Open(player, EmoteFilter, onCommit)` where
  `EmoteFilter = { EmoteCategory? category; EmoteDefinition specific; bool freeChoice; }` — a
  widening of today's `EmoteCategory?` parameter.
- Never filtered by the player's task. Filtering would broadcast to every observer which category
  their neighbour needs. Free-choice is the only mode tasks use; the step judges the result
  afterwards.
- Emotes whose `requiresHeldItem` is unmet still appear and still play — mime a trumpet
  empty-handed. They just do not satisfy the step.
- Cannot open during a minigame (`MinigameBase.IsAnyActive`), a strangle, or an arrest.

### 11.5 EmoteStep

```csharp
public class EmoteStep : TaskStep {
    public EmoteCategory requiredCategory;
    public EmoteDefinition requiredEmote;         // optional; overrides the category
    public ItemDefinitionSet requiredHeldItemSet; // optional

    public PartnerRule partner;      // None | SameCategory | MatchingItemEmote
    public float partnerMaxDistance = 4f;
    public bool partnerMustShareZone = true;
    public string requiredZoneID;
}
```

Evaluated on `TaskEvalReason.EmotePerformed`:

1. The emote matches `requiredEmote`, or its category matches `requiredCategory`.
2. Held-item requirement satisfied.
3. Zone requirement satisfied at emote time — the preceding `NavigateStep` gets the player there
   but does not keep them there.
4. Partner rule satisfied: a living player within `partnerMaxDistance` (and the same zone) whose
   `PlayerEmotes.IsPerforming` is true and whose emote satisfies the rule. Overlap in time, not
   sequence.

Because either player may emote first, `EmotePerformed` evaluates for the performer and every
player in scan range, so the second dancer completes both assignments.

| Task | Category | Partner rule | Held item | Zone |
|---|---|---|---|---|
| instrument_play | Music | MatchingItemEmote | Instrument set | Stage |
| podium_speech | Speech | None | — | Podium |
| dance_court | Dance | SameCategory | — | DanceFloor |
| converse_court | Speech | SameCategory | — | CourtYard |

## 12. Layer G — Objectives

`WaypointManager` pattern-matches every step type in one long if-else chain. Move that knowledge
onto the steps:

```csharp
// on TaskStep
public virtual ObjectiveTarget GetObjectiveTarget(PlayerController player, TaskStepRuntime runtime);
```

Each step answers for itself, delegating to `ItemSourceResolver` (acquire),
`StationRegistry.FindNearestPending` (station and deposit), `TaskZone` (navigate), or nearby-player
scans (emote and partner). `WaypointManager` becomes a renderer over `ObjectiveTarget`s. `Hint`
feeds the HUD line, which is how R2's fallback ("polish a sword first") and R4's edge case ("every
candle is lit — snuff one") reach the player.

## 13. Authority and the network seam

There is no networking layer. `PlayerController.isLocalPlayer` is a serialized bool,
`RoleManager.allPlayers` is self-registering but local, and remote players are `DummyPartner`
stand-ins. R3 cannot be satisfied now, only designed for.

| State | Single mutation surface |
|---|---|
| Station condition, seats, reversal | `TaskStationState` |
| Item spawn / despawn / state / payload / placement | `ItemLifecycle` |
| Dropped-item ownership | `DroppedItemRegistry` |
| Emote performance | `PlayerEmotes.Perform` |
| Two-player session: consent, anchor, outcome | `PartnerLink` + `MinigameBase.participants` |
| Step advancement, task completion | `TaskInstance` / `TaskManager` |
| Cosmetic (IK, grip, VFX, camera, nameplates) | the minigame itself — never replicated |

The partner row is the hard one: three tasks hinge on two clients agreeing that both hands reached
the same point at the same time. Keeping the anchor session-owned rather than parented to either
player is what makes that arbitrable later.

Two standing rules that cost nothing now: no gameplay state is mutated from inside a minigame's
`Update` (accumulate intent, act on completion); and no system reads another player's private
state except through the public surfaces (`PlayerEmotes.Current`, `PickupItem` identity, zone ID,
`transform.forward`).

---

# Part III — Codebase reconciliation

An audit of the repo against the architecture above. Each finding is keep, change, add or delete.

## III.1 Already correct — do not touch

These are the architecture's foundations and they already exist. Several were planned work in
earlier revisions.

| Area | State |
|---|---|
| Step un-nesting | Done. All 11 step types top-level, one file each under `Tasks/Steps/`, namespace `CorruptedCourt.Tasks`, assembly `CorruptedCourt.Gameplay`. The `DepositItemStep` missing-brace bug is fixed and `WaypointManager`'s `using static DepositItemStep;` is gone |
| Typed item identity | Done. `PickupItem.Matches(ItemDefinition, ItemState)`; `ItemState` has `Processed`, `DepositedContainer`, `Spent`. R9 is already enforced — `ProcessItem()` and `MarkAsDepositedContainer()` OR flags instead of mutating `itemName` |
| Typed station identity | Done. `TaskStep.ResolveLocation(go)` walks to the nearest `TaskLocation` ancestor; exact `locationID` match |
| Per-player task state | Done. `TaskInstance` + one `TaskStepRuntime` per step; minigame interception; `CheckForTaskRegression` |
| Tiers and the meter | Done. `TaskData.taskTier` (1–3), `TaskManager.tierWeights {1,2,4}`, `InitializeCourtMeter` sizing from lobby × tasksPerStage × avg tier × targetStages |
| Infinite spawners | Done. `PickupItem.isInfiniteSource`, default true — §1.2's "base items never run out" is already the item-level behaviour |
| Heavy items | Done. `isHeavy`, `haulWithBothHands`, movement penalty, two-handed IK haul pose |
| Scramble drop | Done. `MatchManager.SetScrambleGraceForAll` → `PlayerInventory.DropHeavyItems`, with the penalty suspended for the window |
| Minigame launch | Done. Both paths build a `MinigameContext`; `MinigameBase.ActiveMinigames` is the single "is busy" source; `LeaveActiveRegistry` / `RejoinActiveRegistry` handle mid-lifecycle handback |
| Minigame bases | Written and compiling: `PanelMinigame`, `HandMinigame`, `PartnerMinigame`, `ItemDepositMinigame`, `ToolOnTargetMinigame`, `DragObjectMinigame`, `PourMinigame`, `ConsumeMinigame`, `ChargeReleaseMinigame`. Only `ItemDepositMinigame` has shipped concretes |
| Capabilities | `MinigameHandRig`, `GuidedDrop`, `StationContactProbe`, `MinigameInput`, `MinigameProgressTracker`, `SpawnAndCarryObjective` |
| Solo testing | `PartnerResolver` + `DummyPartner` are real and work |
| Deposit stations | `TaskDepositStation` with `requiredState`, drop slots, `HasReceivedItem()`, `GetSlotDepositor`, stage-gated retrieval, `DepositContactPoint` / `DepositTarget` contact gating |
| Role switching | `RoundRoleSwitch` with `flagAsDepositedContainer`, `TransferDepositedItems`, event-driven (not polled) |
| Events | `GameEvents` covers `MatchStateChanged`, `StationReceivedDeposit`, `PlayerZoneChanged`, `PlayerGhosted`, `LocalTasksChanged`, `CourtProgressChanged` |
| Balance surface | `MatchConfig` with loud override warnings |
| Editor tooling | `TaskStepDrawer`, `TaskStepDefinitionMigration`, `ReserializeTaskData` |

## III.2 Add — new types

| Type | Layer | Why |
|---|---|---|
| `TaskEvalContext`, `TaskEvalReason` | B | Emotes and station changes have no interaction target |
| `TaskStationState` | C | R4 has nowhere to live today |
| `StationReversal` | C | R4's player-facing verb |
| `StationRegistry` | C | Honest waypoints under R4 |
| `ItemIdentity`, `VariantName` | A | Variant display names |
| `ItemPayload` | A | Deposited-into contents and count |
| `ItemDefinitionSet` | A | The instrument set |
| `ItemRecipe`, `RecipeIndex` | A | R2's production graph |
| `ItemSourceResolver` | D | R2's fallback objective |
| `ItemLifecycle` | D | Mutation surface + network seam |
| `DroppedItemRegistry` | D | R5 |
| `MinigameParticipant` | E | Shared sessions credit both players |
| `MinigameProduct` | D/E | Polish, cut, pour outputs |
| `MinigameToolLoan` | E | Knife, cloth, flint, pencil |
| `PartnerLink` | E | Extracted from `PartnerMinigame` |
| `PartnerHandMinigame` | E | Hand rig and partner in one base |
| `MutualReachMinigame` | E | Handshake + toast |
| `SharedStationMinigame` | E | Two seats, one chess session |
| `PlayerEmotes` | F | "Is an emote in progress" — the missing half |
| `EmoteNameplate` | F | The broadcast line |
| `EmoteStep` | B | Four tasks |
| `OrderRecallStep` | B | ledger_check |
| `ObjectiveTarget` | G | Waypoints stop knowing the step taxonomy |
| `TaskDataValidator`, `StationDebugPanel` | tooling | R4 is untestable without the latter |

## III.3 Change — existing types

| Type | Change |
|---|---|
| `TaskStep` | + `CheckCompletion(TaskEvalContext, TaskStepRuntime)` with a forwarding default; + `GetObjectiveTarget` |
| `PlayerTaskBook` | + `EvaluateActiveTasks(TaskEvalContext, …)` overload; existing signature builds one internally |
| `TaskStepRuntime` | Replace the `HasCode` / `GeneratedCode` / `CodeRevealPending` / `DataInputPending` quartet with the sequence equivalents. Keep the one-shot-pending-flag mechanism |
| `PlayerInteractStep` | Absorb `myRequiredItem` + `targetRequiredItem` from `MutualPlayerInteractStep` |
| `StationInteractStep` | Seats via `TaskStationState` replace `Physics.OverlapSphere`; + `requiresStationPending` |
| `ConsumeItemStep` | Accept the held container's `ItemPayload.contents` |
| `AcquireItemStep` | + `requiredItemSet`; read `ItemPayload` |
| `ItemDefinition` | + `variantNames` + `DisplayNameFor(state)` |
| `PickupItem` | Route drops through `ItemLifecycle` |
| `PlayerInventory` | Drops register with `DroppedItemRegistry`; `DropHeavyItems` passes `DropCause.Forced` |
| `TaskDepositStation` | Register with `StationRegistry`; populate `ItemPayload`; reversal wired to the existing retrieval path |
| `RoundRoleSwitch` | `TransferDepositedItems` writes `ItemPayload` alongside the flag |
| `MinigameBase` | + `participants`; `CompleteMinigame` iterates |
| `PartnerMinigame` | Body extracted into `PartnerLink`; composes it |
| `EmoteCategory` | `{ Gesture, Speech, Dance, Music }` |
| `EmoteDefinition` | + `emoteID`, `broadcastVerb`, `requiresHeldItem`; − `partnered` |
| `EmoteWheelController` | Two-ring UI; `EmoteFilter` widens the category parameter; `Commit` routes through `PlayerEmotes.Perform` instead of poking the animator |
| `WaypointManager` | Reduced to a renderer over `ObjectiveTarget` |
| `TaskManager` | Delete the prerequisite auto-spawn block (§III.5) |
| `TaskData` | Delete `prerequisiteTask`, `autoSpawnItemPrefab`, `autoSpawnLocationID` |
| `UIManager` | Data-code popup / input panel become the order-recall panel |

## III.4 The deposit-credit problem

This is the most consequential finding in the audit.

`DepositItemStep.CheckCompletion` scans every `TaskLocation` with the target ID and returns true
if any of them holds a matching item, unless `requireOwnDeposit` is set — and that flag defaults
to false, with a comment noting the default "preserves the old behaviour."

Under R3 (shared world) plus R4 (no reset timer), that default is now wrong. Once one player puts
flowers in one vase, the vase stays filled for the rest of the match unless somebody reverses it.
Every other player holding `flower_vase` then has a step that evaluates true on their next
interaction anywhere near it — and `PlayerController.FinishMinigame`'s auto-advance loop will walk
them straight through it. Twenty players complete the task off one player's work.

Three fixes, and all three should be taken:

1. Flip the default. `requireOwnDeposit = true` on every `DepositItemStep` in the 30 tasks. The
   flag and the `slotDepositors` array that backs it already exist and work.
2. Scope the scan. With `TaskStationState` in place, the step should consider only stations that
   were `Pending` when the player started, not every station with that ID.
3. Make it authoring-visible. `TaskDataValidator` flags any `DepositItemStep` with
   `requireOwnDeposit = false` as a warning, so the permissive form is a deliberate choice rather
   than an inherited default.

The same reasoning applies to `StationInteractStep`, which today has no notion of station state at
all — re-lighting a lit candle would complete. `requiresStationPending` fixes it.

> **Ruled — see Part VI item 3.** All three fixes above are adopted; `requireOwnDeposit = true` is
> confirmed as the default this section, §III.8 and Part V already assume.

## III.5 Delete

| Thing | Why |
|---|---|
| `MutualPlayerInteractStep` | Merged into `PlayerInteractStep` per the specification |
| `DataRetrievalStep` | No task uses it; `OrderRecallStep` replaces it. Its `TaskStepRuntime` fields and `UIManager` popup go with it |
| `EquipClothingStep` | Unused by all 30 tasks, and the codebase's last `name.Contains` identity holdout |
| `GroupNavigateStep` | Unused by all 30 tasks |
| `EmoteMinigame` | The four emote tasks are step-driven, not minigame-driven |
| `InstrumentMinigame` + `WindInstrumentMinigame` + `StringInstrumentMinigame` | Same — `instrument_play` is an `EmoteStep`. **Confirmed in Part VI item 5: instruments are now played purely via the matching emote; no future "actually play it" minigame is planned.** |
| `TaskData.prerequisiteTask` / `autoSpawnItemPrefab` / `autoSpawnLocationID` | Superseded by R2. The auto-spawn block in `AssignTasksForNewStage` goes with them |
| `TaskDepositStation.isSabotaged` | Already commented as dead — nothing reads it since the G2.3 sabotage rework. Delete with the prefab churn |
| `bow_string` task asset | Removed by owner ruling |
| `MinigameChain` (never built) | No task chains minigames |
| `StationResetScheduler` / `StationPoolConfig` (never built) | Superseded by R4 |

Deleting four step types is a `[SerializeReference]` change — see §III.7.

## III.6 Known stubs to finish

From CLAUDE.md, each blocking at least one task:

| Stub | Blocks |
|---|---|
| `EmoteWheelController.Open()` → `// TODO(P4): show the radial UI` — nothing calls `Commit()` | All 4 emote tasks |
| `MinigameHandRig.SetGrip()` is a no-op | Any minigame needing a real grip pose, notably the sword tasks |
| `SpawnAndCarryObjective.PushCarryHint()` is a no-op | cake, turkey follow-up hints |
| `PartnerMinigame` does not extend `HandMinigame` | handshake, cheers, duel (§10.2) |
| No arrival-order data model | ledger_check |

## III.7 Serialization procedure for the step changes

Four deletions and one field merge rewrite `[SerializeReference]` records in every `TaskData`
asset. The repo has been through this once and the tooling exists.

1. One commit, nothing else in it.
2. `[MovedFrom]` is not applicable to deleted types — instead, strip the dead step entries from the
   assets first, using `TaskStepDefinitionMigration`, then delete the classes.
3. For the `PlayerInteractStep` merge, `[FormerlySerializedAs]` on the new fields, and rewrite any
   asset whose record names `MutualPlayerInteractStep` before deleting the class.
4. Run `ReserializeTaskData`, then verify `{class, ns, asm}` in the YAML — not just that the
   Inspector looks right.
5. Push immediately. This is the one change with no compile-time safety net.

`TaskStepDrawer` reflects over `TaskStep` subclasses to build the add-step menu, so deleted types
vanish from it automatically and new ones appear — no editor work needed.

## III.8 Asset-level work

| Asset | Action |
|---|---|
| `quiver_fill` | Rename to `arrow_quiver`; update `taskID` |
| `sword_polish` | Rename to `sword_armory`. Its steps already match the spec exactly: `Acquire(Sword) → Process(Sword) → Deposit(Sword + Processed → Armory)` |
| `bow_string` | Delete |
| All 30 `TaskData` | Set `taskTier` per §5.2 of the specification; clear the three prerequisite fields |
| Station prefabs | Add `TaskStationState`; multiple scene instances where R3 wants a pool |
| New stations | WineCask (Cellar), ConfirmationBox, Ledger, ChessBoard, ArcheryTarget, CourtYard zone |
| Item assets | `variantNames` for the four variants; `ItemDefinitionSet` for Instrument |
| Emote assets | ~13 across four categories, each with a `broadcastVerb` |
| Recipe assets | 7 |

---

# Part IV — Build order

## IV.1 Principles

Each phase compiles and is play-verifiable alone. Dependencies run strictly downward. The first
concrete on each untouched base will shake bugs out of that base — budget for it.

## IV.2 Phases

| Phase | Contents | Depends on | Touches | Risk |
|---|---|---|---|---|
| B1 — Evaluation spine | `TaskEvalContext`, `TaskEvalReason`, `TaskStep` context overload, `PlayerTaskBook` overload, wire the interact / inventory / zone call sites | — | `TaskStep`, `TaskInstance`, `PlayerTaskBook`, `PlayerInteractor`, `PlayerInventory`, `TaskZone` | Low |
| B2 — Step consolidation | Merge `MutualPlayerInteractStep` into `PlayerInteractStep`; delete `DataRetrievalStep`, `EquipClothingStep`, `GroupNavigateStep`; asset migration + reserialize + verify | B1 | `Tasks/Steps/**`, every `TaskData` asset, `UIManager` | High (serialization) |
| B3 — Identity & variants | `ItemIdentity`, `VariantName` + `DisplayNameFor`, `ItemPayload`, `ItemDefinitionSet`; `AcquireItemStep` / `ConsumeItemStep` updates; `RoundRoleSwitch` payload | B1 | `ItemDefinition`, `PickupItem`, `RoundRoleSwitch`, 2 steps | Low |
| B4 — Item lifecycle | `ItemLifecycle` consolidation; route `PickupItem` / `PlayerInventory` / `TaskDepositStation` through it | B3 | `PickupItem`, `PlayerInventory`, `TaskDepositStation` | Medium |
| B5 — Drop registry | `DroppedItemRegistry`, `DropCause`, forced-drop exemption, despawn feedback | B4 | `PlayerInventory`, `ItemLifecycle` | Low |
| B6 — Station state | `TaskStationState`, seats and claims, `StationRegistry`, visual binding, `StationInteractStep` rework incl. `requiresStationPending` | B1 | new + `StationInteractStep`, `TaskStation`, `TaskDepositStation` | Medium |
| B7 — Station reversal | `StationReversal` interactable, hold-to-revert prompts and verbs, deposit reversal wired to the existing retrieval path | B6 | new + `TaskDepositStation` | Low |
| B8 — Recipes & availability | `ItemRecipe`, `RecipeIndex`, `ItemSourceResolver`; delete the prerequisite auto-spawn block and the three `TaskData` fields | B3, B4 | new + `TaskManager`, `TaskData` | Low |
| B9 — Deposit credit | `requireOwnDeposit` default flip, pending-scoped deposit scan, validator warning (§III.4) | B6 | `DepositItemStep`, all `TaskData` assets | Low but wide |
| B10 — Partner foundation | `MinigameParticipant` on `MinigameBase`; extract `PartnerLink`; `PartnerHandMinigame`; `DummyPartner` reach + emote poses | B1 | `MinigameBase`, `PartnerMinigame`, `DummyPartner` | Medium |
| B11 — Mutual reach | `MutualReachMinigame`: session-owned anchor, per-side reach from mouse aim, dual confirm, consent timeout, graceful cancel | B10 | new | Medium |
| B12 — Emote data | `EmoteCategory` rework, `EmoteDefinition` fields, `PlayerEmotes`, `EmoteNameplate`, `GameEvents.EmotePerformed` | B1 | `EmoteSystem.cs`, new | Low |
| B13 — Emote wheel UI | Two-ring hold-B radial, category hover, commit / cancel, input gating, `Commit` → `PlayerEmotes.Perform` | B12 | `EmoteWheelController`, new UI prefab | Medium |
| B14 — Emote step | `EmoteStep`, partner rules, zone re-check, dual-side evaluation | B12 | new + `PlayerTaskBook` | Low |
| B15 — Minigame capabilities | `MinigameProduct` (list form), `MinigameToolLoan`, `SharedStationMinigame`; delete `EmoteMinigame` and the three instrument bases | B4, B6, B10 | `Minigames/**` | Low |
| B16 — Objectives | `ObjectiveTarget`, per-step `GetObjectiveTarget`, `WaypointManager` → renderer, HUD hint line | B6, B8 | `WaypointManager`, all steps | Low |
| B17 — Order recall | `OrderRecallStep`, station-owned fixed sequence, drag-and-drop panel replacing the code popup | B2, B6 | new + `UIManager`, `TaskStepRuntime` | Low |
| B18 — Tooling | `TaskDataValidator`, `StationDebugPanel`, force-satisfy / force-revert cheats | B6–B17 | `Editor/**` | Low |
| B19 — Authoring pass | Item definitions and variant names, station prefabs and instance placement, 7 recipes, ~13 emotes, zones, asset renames | B18 | assets | Low |
| B20 — Tasks | The 30 tasks, one at a time, against Part V | B19 | per-task | per-task |

Suggested granularity: one prompt each for B1, B2, B3, B5, B7, B8, B9, B11, B12, B14, B16, B17; two
each for B4, B6, B10, B13, B15, B18, B19. Roughly 20 prompts before the first task.

B2 first among the risky ones. It is the only phase with no compile-time safety net, and
everything downstream authors against the step set it leaves behind. Doing it early means one
asset migration rather than two.

## IV.3 Testing posture

- `DummyPartner` gains `PerformEmote(EmoteDefinition)`, a facing pose and a reach pose. Without the
  reach pose, three tasks are untestable solo.
- `StationDebugPanel` lists every `locationID`, its instances and conditions. R4 is untestable
  without it, and it is the fastest way to spot a station type starved to zero pending.
- `TaskDataValidator` walks every `TaskData` and reports: steps with no matching station in the
  scene; item identities with no `ItemDefinition`; deposit targets with no `TaskDepositStation`;
  `DepositItemStep` with `requireOwnDeposit = false`; emote steps whose category has no assets;
  emotes with no `broadcastVerb`; recipes with unreachable inputs; station types with no
  reversible instance; and any `locationID` or `taskID` containing invisible characters.
- Every base class gets its first concrete treated as a debugging session for the base, not just
  the task.

---

# Part V — Per-task implementation contract

Once B1–B19 land, each of the 30 tasks is the same fixed shape of work.

**Given:** task ID, name, description, stage, tier, ordered step list with parameters, and the
minigame description — all from Part 4 of the specification.

**Deliverables:**

1. `ItemDefinition` assets for any new item, plus `variantNames` entries for every state the task
   displays.
2. `ItemRecipe` asset if the task produces an identity another task consumes.
3. Station prefab(s) with `TaskLocation` (+ `TaskDepositStation` where applicable) +
   `TaskStationState` carrying seat count, reversibility and revert verb — and multiple scene
   instances where R3 wants a pool.
4. `TaskData` asset: ID, name, description, `allowedStages`, `taskTier`, `isSabotage = false`,
   typed step references, `requireOwnDeposit = true` on deposits. No strings pasted from the spec
   (invisible characters).
5. Minigame script + prefab where the spec gives one: the right family from §10.5,
   `MinigameProduct` if it produces, `MinigameToolLoan` if it needs a tool, `participants` if it
   seats two, and a `PanelMinigame` faker variant where the physical version is long.
6. Emote assets the task names, each with a `broadcastVerb`.
7. Objective text per step plus a verified waypoint target, including the R2 fallback ("go make
   one") and the R4 edge case ("nothing pending — go reverse one").
8. Solo test path end to end, including dummy setup and the reach pose for the three mutual tasks.

**Validation:** `TaskDataValidator` clean, plus a Play Mode run of the full chain including
deliberate failure — abandon the minigame mid-way, drop the produced item, have the partner walk
away mid-reach, have someone reverse the station after completion.

**Out of scope for a task prompt:** new base classes, new step types, changes to the evaluation
spine. If a task appears to need one, that is a gap in this architecture and gets its own prompt.

---

# Part VI — Open items

The specification is in good shape; two of its own open items stand, plus three raised by the
audit.

### From the specification

1. **The chess endgame position.** Two bare kings is a draw by insufficient material and would
   never resolve. The authored position needs at least one piece beyond the kings (king-and-rook
   or king-and-pawn against king both force a result in a handful of moves), and the terminal
   condition needs naming. Safest: checkmate, stalemate, resignation or either player leaving all
   resolve it, with both credited — so an abandoned game never traps two players at a board.
   **Ruling: King+Rook vs. King**, the simplest forced-mate position — no pawn-promotion or
   under-promotion edge cases to author or explain. Terminal condition: checkmate, stalemate,
   resignation, or either player leaving the board all end the session, with **both** participants
   credited (R7) — an abandoned game never traps two players at a board. This is a ruling on the
   *rule*, not the content: the actual piece placement on the `ChessBoard` prefab is authored when
   `game_play` itself is implemented (Part V / B20), not an architecture concern. It does not block
   B1–B19 — `SharedStationMinigame` (B15) only needs *some* terminal condition to fire and to
   credit every participant when it does, which `MinigameParticipant` (B10) already covers
   generically.

2. **Spawner placement.** Every base item has an infinite spawner and `PickupItem.isInfiniteSource`
   already implements it, but which of the eight map locations hosts which spawner is unassigned.
   `ItemSourceResolver` needs it before any task tests end to end.
   **Ruling: deferred.** This will be determined when map building begins. Does not block B1–B19:
   `ItemSourceResolver` (B8) only needs spawners to exist and be flagged `isInfiniteSource`, not to
   know their positions in advance.

### From the audit

3. **Deposit credit default (§III.4).** Assumed `requireOwnDeposit = true` everywhere — needs
   confirming, since it changes how nine tasks feel, and the permissive alternative means one
   player can satisfy a deposit task for the whole lobby.
   **Ruling: confirmed, `requireOwnDeposit = true`.** All three fixes in §III.4 are adopted as the
   B9 deliverable: flip the default, scope the scan to stations that were `Pending` when the
   player started, and have `TaskDataValidator` (B18) warn on any task that sets it back to
   `false`. This is the load-bearing default for every `DepositItemStep` in all 30 tasks.

4. **ConfirmationBox seats.** The spec makes its result per-player, which the runtime model already
   supports. But can two players use the same box simultaneously, or does it take a seat like
   other stations? Per-player results suggest no seat; a shared physical prop suggests one.
   **Ruling: yes** — multiple players can use a confirmation box at the same time. No
   `TaskStationState` seat capacity on `ConfirmationBox`; it stays purely per-player.

5. **InstrumentMinigame deletion.** Three written-but-unused bases (`InstrumentMinigame`, Wind,
   String) implement hand-to-mouth and strum gestures. `instrument_play` is now purely an emote,
   so they are unreachable. Delete them, or keep them parked for a later "actually play the
   instrument" feature?
   **Ruling: delete, confirmed.** Instruments are played by a player performing the matching
   emote — permanently, not as a placeholder. No future minigame is planned for this. (Reflected
   in §III.5 above.)

### Housekeeping

Part 5 of the specification has a numbering glitch — the "Resolved in the previous round" list
restarts at 2 after item 11, so items 2–8 appear twice. Content is fine; only the numbering is
off. (Applies to the companion specification document, not this one.)

### Status

All five items above are now ruled (2026-09-12). The ordered prompt pack — B1–B19, 26 prompts —
lives at `Assets/Scripts/Tasks/IMPLEMENTATION_PROMPTS.md`, one paste-ready prompt per phase (some
phases split into two), matching the pattern the now-deleted `Minigames/IMPLEMENTATION_PROMPTS.md`
established. B20 (the 30 tasks themselves) is deliberately **not** in that file — tasks are handed
in one at a time, each against the Part V contract, once B1–B19 land.
