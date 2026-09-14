# Task System — Implementation Prompts

Paste-ready prompts for `ARCHITECTURE.md`'s build order (§IV.2, phases B1–B19). One section below
per prompt, in dependency order — do not skip ahead. **B20 (the 30 tasks themselves) is
deliberately not here**: those are handed in one at a time, each against the Part V contract,
once B1–B19 are done. Mirrors the pattern the now-deleted `Minigames/IMPLEMENTATION_PROMPTS.md`
established for the minigame refactor.

Every prompt was checked against the live code as of 2026-09-12 (exact current signatures, file
paths, and a few corrections to `ARCHITECTURE.md`'s prose — noted inline where they occur). Re-read
the target files yourself before editing regardless; code moves faster than prompts.

## Before every prompt

- Read `ARCHITECTURE.md` in full at least once, then re-read the §-references a prompt names.
- Read `CLAUDE.md` at the repo root. Its hazards apply every time, not just when a prompt calls one
  out by name: null-guard loops over public `List<T>`, unregister whatever you register, no
  per-frame allocation/logging, `[FormerlySerializedAs]` on renamed serialized fields,
  `[MovedFrom]` on relocated `[SerializeReference]` types, flag a serialized-field deletion before
  doing it, `git mv` (never delete-and-recreate) to move a `.cs`, return complete files, plan before
  touching >2 files.
- **Assembly placement — a correction to `ARCHITECTURE.md` §5's layer diagram.** The Layer A–G
  boxes are a conceptual grouping, not an assembly map. The real constraint (CLAUDE.md's asmdef
  graph) is `Core, Items ──► Gameplay ──► UI`, plus `EditorTools` on top of all three — arrows point
  from a dependency toward whatever references it, so `Items` **cannot** see `Gameplay` types
  (`PickupItem`, `TaskData`, `PlayerController`, …) even though the architecture doc's Layer A lists
  `ItemIdentity`/`ItemRecipe` alongside `ItemDefinition`. This was checked type-by-type against
  every new type the doc introduces; each prompt below states the corrected file path + assembly.
  The short version: only `ItemDefinitionSet` and `VariantName`/`ItemIdentity` **as pure data** (no
  `PickupItem`-touching methods) actually belong in the `Items` assembly. Everything else the doc
  places in Layers C–G (stations, item lifecycle, minigames, emotes, objectives) lives in `Gameplay`
  (which is where `Tasks/` and `Minigames/` already compile to) because it touches `PickupItem`,
  `PlayerController`, or `TaskData`.
- No `dotnet build` catches a `[SerializeReference]` mistake. Any phase touching `TaskData` assets
  needs a Unity Editor re-open + Inspector check, not just a green compile.
- End every phase by telling the user exactly what to check in the Editor, per CLAUDE.md.

---

## B1 — Evaluation spine

**Depends on:** nothing. **Risk:** Low.

### What's actually there today (verified)

`TaskStep` (`Tasks/TaskStep.cs`) already has the exact pattern this phase extends:
```csharp
public abstract bool CheckCompletion(PlayerController player, GameObject targetInteractable = null);
public virtual bool CheckCompletion(PlayerController player, GameObject targetInteractable, TaskStepRuntime runtime)
    => CheckCompletion(player, targetInteractable);
```
`TaskInstance.EvaluateCurrentStep` (`Tasks/TaskInstance.cs:67`) already calls the 3-arg runtime-aware
overload for every step — runtime is already threaded end to end. `PlayerTaskBook.EvaluateActiveTasks`
(`Gameplay/Player/PlayerTaskBook.cs:76`) is `(GameObject target, bool stopOnMinigame = false, bool
skipMinigame = false)`. Its 5 current call sites are all in `Gameplay/Player/PlayerInteractor.cs`
(lines 434, 481, 524, 539) and `Gameplay/PlayerController.cs:748` — every one of them is
`Interact`-shaped. `CheckRegressionForAll()` is called separately, from
`Gameplay/PlayerController.cs:612` and `Gameplay/Player/PlayerInventory.cs:94,127,152` (all after a
drop/clear). `Gameplay/TaskZone.cs` does **not** call into task code at all today — it only writes
`player.Vitals.currentZoneID` and raises `GameEvents.RaisePlayerZoneChanged(player)`; nothing
currently subscribes to that event except `MatchManager`.

### Do

1. New file `Tasks/TaskEvalContext.cs`, namespace `CorruptedCourt.Tasks`:
   ```csharp
   public enum TaskEvalReason
   {
       Interact, InventoryChanged, ZoneChanged,
       EmotePerformed, StationConditionChanged, MinigameCompleted, PartnerAction
   }

   public class TaskEvalContext
   {
       public PlayerController Player;
       public TaskEvalReason Reason;
       public GameObject Target;
       public TaskLocation Location;
       public PickupItem Item;
       public PlayerController Partner;
       public EmoteDefinition Emote;   // confirm EmoteDefinition's actual namespace/using — Minigames/Emote/EmoteSystem.cs
       public bool SkipMinigame;
   }
   ```
   `EmotePerformed`/`StationConditionChanged`/`MinigameCompleted`/`PartnerAction` have no call site
   yet — that's correct, they arrive in B6/B10/B12. Declare all 7 now; leave the other 4 unwired.
2. `TaskStep`: add the context overload right after the existing 3-arg one —
   `public virtual bool CheckCompletion(TaskEvalContext ctx, TaskStepRuntime runtime) => CheckCompletion(ctx.Player, ctx.Target, runtime);`
   No existing step needs to change.
3. `TaskInstance`: make the context-aware version the real implementation; the old signature
   becomes a thin forwarder (this is the "old signature builds one internally" the doc means):
   ```csharp
   public bool EvaluateCurrentStep(TaskEvalContext ctx)
   {
       if (IsComplete) return true;
       TaskStep activeStep = GetCurrentStep();
       if (activeStep != null && activeStep.CheckCompletion(ctx, CurrentStepRuntime))
       {
           if (!ctx.SkipMinigame && activeStep.minigamePrefab != null)
           {
               ctx.Player.StartMinigame(activeStep.minigamePrefab, this, ctx.Target);
               return false;
           }
           CompleteActiveStep();
       }
       return IsComplete;
   }

   public bool EvaluateCurrentStep(PlayerController player, GameObject targetInteractable = null, bool skipMinigame = false)
       => EvaluateCurrentStep(new TaskEvalContext {
           Player = player, Reason = TaskEvalReason.Interact, Target = targetInteractable, SkipMinigame = skipMinigame
       });
   ```
4. `PlayerTaskBook`: same forwarding shape —
   ```csharp
   public bool EvaluateActiveTasks(TaskEvalContext ctx, bool stopOnMinigame = false)
   {
       bool anyCompleted = false;
       for (int i = activeTasks.Count - 1; i >= 0; i--)
       {
           TaskInstance task = activeTasks[i];
           if (task.EvaluateCurrentStep(ctx))
           {
               if (TaskManager.Instance != null) TaskManager.Instance.CompleteTask(player, task);
               anyCompleted = true;
           }
           if (stopOnMinigame && player.isPlayingMinigame) break;
       }
       return anyCompleted;
   }

   public bool EvaluateActiveTasks(GameObject target, bool stopOnMinigame = false, bool skipMinigame = false)
       => EvaluateActiveTasks(new TaskEvalContext {
           Player = player, Reason = TaskEvalReason.Interact, Target = target, SkipMinigame = skipMinigame
       }, stopOnMinigame);
   ```
5. **New call sites** (the actual point of this phase — the old paths above already work unchanged):
   - `PlayerInventory.cs` (3 sites, ~94/127/152) and `PlayerController.cs:612`: alongside the
     existing `CheckRegressionForAll()` call, also call
     `player.TaskBook.EvaluateActiveTasks(new TaskEvalContext { Player = player, Reason = TaskEvalReason.InventoryChanged, Target = <the item's GameObject in scope at that line>, Item = <the PickupItem in scope> })`.
     Read each call site first — the exact local variable holding the item differs per site.
   - `PlayerTaskBook`: subscribe to the zone-change event so a step can react without `TaskZone`
     knowing anything about tasks (keeps the existing clean separation — `TaskZone` stays as-is).
     Add `OnEnable`/`OnDisable`:
     ```csharp
     void OnEnable()  { GameEvents.PlayerZoneChanged += HandleZoneChanged; }
     void OnDisable() { GameEvents.PlayerZoneChanged -= HandleZoneChanged; }
     private void HandleZoneChanged(PlayerController p)
     {
         if (p != player) return;
         EvaluateActiveTasks(new TaskEvalContext { Player = player, Reason = TaskEvalReason.ZoneChanged });
     }
     ```
     Confirm `GameEvents.PlayerZoneChanged`'s exact delegate shape in `Gameplay/GameEvents.cs` before
     wiring this — the call site in `TaskZone.cs` (`GameEvents.RaisePlayerZoneChanged(player)`)
     strongly implies `Action<PlayerController>`, but verify.

### Don't

- Don't touch the 5 existing `EvaluateActiveTasks` call sites in `PlayerInteractor.cs`/
  `PlayerController.cs:748` — the forwarding overload keeps them compiling and behaving identically.
  Repointing them to build a `TaskEvalContext` directly is unnecessary churn for this phase.
- Don't add `EmotePerformed`/`StationConditionChanged`/`MinigameCompleted` call sites — their owning
  types don't exist yet (B12/B6/B10).

### Verify

Compiles; no behavior change for any existing interaction path (this is the point — it's pure
plumbing). In the Editor: pick up and drop an item on a task that has an `AcquireItemStep`, confirm
regression still works exactly as before (nothing here should change observed behavior yet).

---

## B2 — Step consolidation

**Depends on:** B1. **Risk:** High — the only phase with no compile-time safety net.

### What's actually there today (verified)

- `PlayerInteractStep` (`Tasks/Steps/PlayerInteractStep.cs`) fields: `requiredItem` (ItemDefinition),
  `requiredState` (ItemState, default None), legacy `legacyRequiredHeldItemName`
  (`[FormerlySerializedAs("requiredHeldItemName")]`). `CheckCompletion` supports empty-handed
  (`requiredItem == null` → require `player.GetHeldItem() == null`).
- `MutualPlayerInteractStep` (`Tasks/Steps/MutualPlayerInteractStep.cs`) fields: `requiredItem` +
  `requiredState` (**identical names** to `PlayerInteractStep`'s — no collision to resolve) for the
  initiator, plus `targetRequiredItem` + `targetRequiredState` (ItemDefinition/ItemState) for the
  target, plus legacy `legacyMyRequiredItemName`/`legacyTargetRequiredItemName`. `CheckCompletion`
  requires **both** sides hold a specific item — no empty-handed branch on either side.
- `DataRetrievalStep`, `EquipClothingStep`, `GroupNavigateStep` bodies confirmed short and exactly
  as `ARCHITECTURE.md` §III.5 describes (single-purpose, no other type depends on them).
- `TaskData.stepTemplates` is `[SerializeReference] [FormerlySerializedAs("steps")] public List<TaskStep>` —
  confirmed already migrated to that name; asset YAML records are `{class, ns: CorruptedCourt.Tasks,
  asm: CorruptedCourt.Gameplay}` blocks per step.

### Do

1. **Merge into `PlayerInteractStep`:** add `targetRequiredItem` (ItemDefinition) and
   `targetRequiredState` (ItemState, default `None`) fields, plus
   `[FormerlySerializedAs("targetRequiredItemName")] public string legacyTargetRequiredItemName;`
   Extend `CheckCompletion`: keep the existing my-side logic (empty-handed when `requiredItem ==
   null`, else must match); add a target-side check that only applies **when `targetRequiredItem !=
   null`** (null = no constraint on the target's hands at all — not "target must be empty"; that
   asymmetry is deliberate, since old `PlayerInteractStep` tasks never needed to say anything about
   the target). Update `GetObjectiveText`/`GetConfigurationWarning` to cover both sides.
2. Strip every `MutualPlayerInteractStep` entry from every `TaskData` asset using
   `Editor/TaskStepDefinitionMigration.cs`, rewriting its YAML record to `PlayerInteractStep` and
   mapping `requiredItem/requiredState` → same names (no change needed) and
   `targetRequiredItem/targetRequiredState` → same names (also no change needed — this merge is
   unusually clean because both classes already agree on field names).
3. Delete `Tasks/Steps/MutualPlayerInteractStep.cs` (+ `.meta`), `DataRetrievalStep.cs` (+ `.meta`),
   `EquipClothingStep.cs` (+ `.meta`), `GroupNavigateStep.cs` (+ `.meta`).
4. Strip the corresponding `TaskStepRuntime` fields used only by `DataRetrievalStep`: `HasCode`,
   `GeneratedCode`, `CodeRevealPending`, `DataInputPending` (all four — B17 replaces them with the
   sequence equivalents later; don't add those yet, that's out of scope here).
5. `UI/UIManager.cs`: remove the "Data Retrieval UI" region (~lines 47–56, 650–728) — fields
   `dataPopupPanel`, `dataCodeText`, `dataInputPanel`, `dataInputField`, `currentDataPlayer`,
   `currentDataTask`; methods `ShowDataCodePopup`, `HideDataCodePopup`, `OpenDataInputPanel`,
   `CloseDataInputPanel`, `SubmitDataCode`; and the `DataRetrievalStep`-specific branch inside
   `OnLocalTasksChanged` (~156–184). These are serialized `[SerializeField]` UI references — flag
   the deletion to the user before removing (CLAUDE.md: deleting a serialized field discards
   Inspector-set values with no warning) so they can note which prefab had them wired, even though
   the panels themselves are presumably about to be orphaned anyway.
6. Run `Editor/ReserializeTaskData.cs`. Open the Editor, open every `TaskData` asset that had a
   `MutualPlayerInteractStep`/`DataRetrievalStep`/`EquipClothingStep`/`GroupNavigateStep` entry and
   confirm the Inspector shows the expected step type with fields intact — not just that the YAML
   parses.

### Don't

- Don't touch `AcquireItemStep`, `DepositItemStep`, `StationInteractStep`, `ProcessItemStep`,
  `ConsumeItemStep`, `NavigateStep` — their turns are B3/B6/B9.
- Don't add `EmoteStep` or `OrderRecallStep` yet (B14, B17) — B2 is deletions + one merge, nothing
  new.

### Verify

One commit, nothing else in it (per `ARCHITECTURE.md` §III.7). Push immediately — this is explicitly
called out as the one change with no compile-time safety net. **Tell the user exactly which
`TaskData` assets were touched** so they can spot-check the Inspector themselves before moving on.

---

## B3 — Identity & variants

**Depends on:** B1. **Risk:** Low.

### Assembly correction (read the preamble above first)

`ItemIdentity.Matches(PickupItem)` as written in `ARCHITECTURE.md` §6.1 **will not compile** if
`ItemIdentity` lives in the `Items` assembly — `PickupItem` is a `Gameplay`-assembly type and
`Items` cannot reference `Gameplay`. Split it:
- `Items/ItemIdentity.cs` (namespace `CorruptedCourt.Items`): pure data only —
  `public struct ItemIdentity { public ItemDefinition definition; public ItemState state; }`
- Add the match as an **overload on `PickupItem`** instead, in `Gameplay/PickupItem.cs`, right next
  to the existing `Matches(ItemDefinition, ItemState)`:
  `public bool Matches(ItemIdentity id) => Matches(id.definition, id.state);`

### Do

1. `ItemDefinition` (`Items/ItemDefinition.cs`): add
   ```csharp
   [System.Serializable] public struct VariantName {
       public ItemState whenState;
       public string displayName;
   }
   public List<VariantName> variantNames;
   public string DisplayNameFor(ItemState state)   // most-specific flag match wins (most bits set), else falls back to `displayName`
   ```
2. New `Items/ItemDefinitionSet.cs`: `ScriptableObject`, `List<ItemDefinition> members`,
   `bool Contains(ItemDefinition def)`. Create the one asset the doc calls for later in B19
   (`Instrument = {Guitar, Trumpet, Flute}`) — not now, just build the type.
3. New `Gameplay/ItemPayload.cs` (not `Items/` — it's runtime scene-object state sitting beside
   `PickupItem`, and `ScriptableObject`s must never hold per-player/runtime state per CLAUDE.md,
   which this doesn't violate since it's a plain `MonoBehaviour`, but it belongs organizationally
   and assembly-wise with `PickupItem`):
   `public class ItemPayload : MonoBehaviour { public ItemDefinition contents; public int count; public int capacity = 1; }`
4. `PickupItem.cs`: route every place that currently mutates `itemName`/state via ad-hoc code
   through the existing `ProcessItem()`/`MarkAsDepositedContainer()` pattern if it isn't already —
   verify first, per `ARCHITECTURE.md` §III.1 this is already done; B3 should find nothing left to
   change here beyond the new `Matches(ItemIdentity)` overload above.
5. `AcquireItemStep`: add `public ItemDefinitionSet requiredItemSet;` — when set, `CheckCompletion`
   should accept any member of the set instead of (or in addition to) `requiredItem`. Also read a
   held item's `ItemPayload` when relevant (a quiver with arrows in it) — check the held item's
   `GetComponent<ItemPayload>()` for `contents`/`count` when the step's required identity implies a
   filled container.
6. `ConsumeItemStep`: accept the held container's `ItemPayload.contents` (eat the cake off the
   plate) instead of requiring the plate itself to carry the identity.
7. `RoundRoleSwitch.cs`: `TransferDepositedItems()` (currently `private void`, no params) should
   also write an `ItemPayload` (`contents`/`count`) onto the item it converts from station to
   pickup, alongside the existing `flagAsDepositedContainer` flag logic in `Apply()`.

### Don't

- Don't build `ItemLifecycle` yet (B4) — these changes can still touch `PickupItem`/
  `PlayerInventory`/`TaskDepositStation` directly.
- Don't build `ItemRecipe` yet (B8) — `ItemIdentity` here is pure data with no recipe consumer.

### Verify

Compiles. In the Editor: confirm `ItemDefinition` assets show the new `Variant Names` list in the
Inspector with no data loss on existing assets (it's a new list, so this should be a no-op, but
open one existing `ItemDefinition` asset and check).

---

## B4a — Item lifecycle surface

**Depends on:** B3. **Risk:** Medium (split 1/2 — this half only adds the surface).

### Do

New `Gameplay/ItemLifecycle.cs` (static class, `namespace CorruptedCourt.Gameplay`):
```csharp
public static class ItemLifecycle
{
    public static PickupItem Spawn(PickupItem prefab, ItemIdentity identity, Vector3 pos, Quaternion rot);
    public static void GiveToHand(PlayerController player, PickupItem item, Hand hand);
    public static void SetState(PickupItem item, ItemState add, ItemState remove);
    public static void SetPayload(PickupItem item, ItemDefinition contents, int count);
    public static void PlaceInStation(PickupItem item, TaskDepositStation station, int slot);
    public static void Drop(PlayerController dropper, PickupItem item, DropCause cause);
    public static void Despawn(PickupItem item);
}
```
`DropCause` doesn't exist yet — it's a B5 type (`DroppedItemRegistry` phase). For B4a, define a
minimal `public enum DropCause { Manual, Thrown, Forced }` right here in `ItemLifecycle.cs`; B5 will
either keep it here or move it, your call at that point, but something has to define it now since
`Drop`'s signature needs it.

Implement each method by **finding** the existing scattered logic (per `ARCHITECTURE.md` §9.1,
"everything here exists today, scattered across `PickupItem`, `PlayerInventory` and
`TaskDepositStation`") and having the new method call into it — don't yet delete the original call
sites or change their callers. `Hand` — confirm this enum's actual name/location (likely already
exists on `PlayerInventory` or `PlayerController` for left/right hand selection) before assuming
the signature above; adjust the parameter type to match what's really there.

### Don't

- Don't repoint any existing caller yet — that's B4b. This phase is additive only: the new type
  exists and compiles, nothing is wired to it yet.

### Verify

Compiles. This phase has no observable behavior change (nothing calls the new surface yet), so
verification is just a clean compile plus a self-review that each method's body actually matches
what the corresponding scattered logic currently does.

---

## B4b — Item lifecycle adoption

**Depends on:** B4a. **Risk:** Medium (split 2/2).

### Do

Repoint `PickupItem.cs`, `PlayerInventory.cs`, and `TaskDepositStation.cs` so their internal
mutation logic (drop, pickup, state change, station placement) calls through `ItemLifecycle`
instead of duplicating it inline. `ItemLifecycle` becomes the one real mutation surface (invariant
2); the MonoBehaviours become thin callers.

### Don't

- Don't change any public method signature on `PickupItem`/`PlayerInventory`/`TaskDepositStation`
  that other code depends on — this is an internal refactor, not an API change. If a signature
  change looks necessary, stop and flag it rather than cascading edits into unrelated callers.

### Verify

Compiles; no behavior change. In the Editor: pick up an item, drop it, deposit it at a station,
confirm all three behave identically to before this phase (this is a pure refactor).

---

## B5 — Drop registry

**Depends on:** B4. **Risk:** Low.

### What's actually there today (verified)

`PlayerInventory.cs` has `public void DropHeavyItems()`, `public bool itemSwappedToLeftHand`,
`public void ReturnSwappedItem()` — exact names, all public fields/methods, not private.

### Do

New `Gameplay/DroppedItemRegistry.cs` (likely a `MonoBehaviour` singleton matching the existing
`RoleManager.allPlayers`-style self-registering pattern, or a plain static — pick whichever the
existing singleton convention in this codebase favors, check `RoleManager`/`TaskManager` first):
```csharp
void RegisterDrop(PlayerController dropper, PickupItem item, DropCause cause);
void Clear(PickupItem item);
PickupItem OutstandingFor(PlayerController p);
```
Wire it through `ItemLifecycle.Drop`: on `DropCause.Manual`/`Thrown`, if the dropper already has an
`OutstandingFor` item, despawn it now (via `ItemLifecycle.Despawn`, with a visible fade/puff — not
an instant `Destroy`, or it reads as a bug per §9.2). `DropHeavyItems()` must pass
`DropCause.Forced` so the scramble drop is exempt from the one-drop-slot limit. Anyone picking up a
dropped item calls `Clear(item)`.

### Don't

- Don't touch station placements, consumption, or hand-to-hand item moves — none of those are
  "drops" per R5, and shouldn't consume/clear a drop slot.

### Verify

In the Editor: drop an item manually, drop a second one — confirm the first despawns with a visible
effect, not an instant pop. Trigger the pre-meeting scramble drop (`DropHeavyItems`) while already
holding an outstanding manual drop — confirm the scramble drop does **not** consume/destroy it.

---

## B6a — Station state foundation

**Depends on:** B1. **Risk:** Medium (split 1/2).

### What's actually there today (verified — corrects `ARCHITECTURE.md`'s framing)

`Gameplay/TaskStation.cs` **already exists** — a thin `MonoBehaviour, IInteractable`,
`[RequireComponent(typeof(TaskLocation))]`, one field (`processMinigamePrefab`), `OnInteract` just
logs (comment: the space is for visual/audio effects only, no task-check logic — task evaluation
happens in `PlayerInteractor`, which calls `PlayerTaskBook.EvaluateActiveTasks` regardless of what
the interactable itself does). `TaskStationState` does **not** exist anywhere yet — it's 100%
new. `TaskDepositStation.cs` already has `HasReceivedItem()`.

### Do

1. New `Gameplay/TaskStationState.cs` per `ARCHITECTURE.md` §8.1, verbatim signature. Raise a
   **new** `GameEvents.StationConditionChanged` event from `MarkSatisfied`/`TryRevert` — this event
   does not exist in `GameEvents.cs` today (confirmed against the full current event list: it has
   `StationReceivedDeposit` but nothing named `StationConditionChanged`), so add it there too,
   matching the existing `Raise*` wrapper-method pattern the file already uses for every other
   event.
2. New `Gameplay/StationRegistry.cs` per §8.3, mirroring `TaskLocation.AllLocations`'s existing
   static-list pattern (check that file for the exact idiom used — self-registering OnEnable/
   OnDisable, or a singleton with a list — and match it, don't invent a third pattern).
3. Wire `TaskStation.cs` to require (or reference) a sibling `TaskStationState`. Wire
   `TaskDepositStation` to derive its `TaskStationState.Current` from `HasReceivedItem()` rather
   than storing a second copy of "am I filled" — per §8.1, this method already exists, just call it.
4. Visuals: `pendingVisual`/`satisfiedVisual` are two `GameObject` roots (or an animator parameter)
   toggled only from condition-change callbacks — no minigame or step touches them directly.

### Don't

- Don't touch `StationInteractStep` yet — that's B6b.
- Don't build `StationReversal` yet — that's B7.

### Verify

Compiles. In the Editor: add a `TaskStationState` to one station prefab instance, confirm
`pendingVisual`/`satisfiedVisual` toggle correctly when you manually call `MarkSatisfied`/
`TryRevert` from a debug button or the Inspector's context menu (a temporary test hook is fine here
— B18 builds the real debug panel).

---

## B6b — Station state in StationInteractStep

**Depends on:** B6a. **Risk:** Medium (split 2/2).

### What's actually there today (verified — corrects `ARCHITECTURE.md`'s prose)

`StationInteractStep.CheckCompletion` (`Tasks/Steps/StationInteractStep.cs`) already uses
**`Physics.OverlapSphereNonAlloc`** with a cached `proximityBuffer`, not the plain `Physics.
OverlapSphere` the architecture doc's §8.1 prose describes — it already follows CLAUDE.md's
no-alloc hazard rule correctly. The thing that's actually wrong isn't the allocation, it's the
*semantics*: proximity (anyone standing near the chess board) isn't the same as occupancy (someone
actually seated at it), and there's no notion of station condition at all, so re-lighting an
already-lit candle would complete the step.

### Do

1. Add `public bool requiresStationPending;` to `StationInteractStep`. When true, `CheckCompletion`
   must additionally check the resolved station's `TaskStationState.Current == Pending` before
   succeeding.
2. Replace the `requiredSimultaneousPlayers > 1` proximity count with seat claims: decide (and
   document in the step or the station, whichever owns it more naturally) when a seat is claimed —
   the natural point is interaction-hold-start via the existing `IInteractable` hold-to-interact
   flow, released on hold-cancel or minigame end — and check `TaskStationState.SeatsFull` /
   `TryClaimSeat`/`ReleaseSeat` instead of counting `PlayerController`s in a physics buffer.
3. Keep the `proximityBuffer` overflow warning pattern (`Log.Warn` when the buffer is full) if any
   proximity-style check survives elsewhere in the step — don't silently drop that safety net.

### Don't

- Don't change `requiredSimultaneousPlayers`'s meaning for existing single-player station tasks —
  only the >1 branch changes shape.

### Verify

In the Editor: a station with `requiresStationPending` set should refuse to complete when already
`Satisfied` (re-lighting a lit candle does nothing). A 2-seat station should require two players to
actually claim seats, not just stand nearby.

---

## B7 — Station reversal

**Depends on:** B6. **Risk:** Low.

### Do

New `Gameplay/StationReversal.cs`: an `IInteractable`, active only while
`TaskStationState.Current == Satisfied && playerReversible`. Prompt text built from the authored
`revertVerb` (`"Hold [E] to snuff the candle"`). Hold `revertHoldSeconds` → `TryRevert()` →
`Pending`. No minigame, no tool, uncredited, unrestricted (R1/R4) — never touches a completed
`TaskStepRuntime`/step index.

For a deposit station, wire reversal to the **existing** stage-gated retrieval path already on
`TaskDepositStation` (`retrievableFromStage`, `RetrieveTestMode` — both confirmed present) rather
than building a second retrieval mechanism.

Reversibility per `ARCHITECTURE.md` §8.2: reversible are Candle (snuff), Painting (tilt), Banner
(re-roll), every deposit station (take back). Not reversible: Cake, Turkey, EmptyGlass,
WeddingContract, ChessBoard, ArcheryTarget, Ledger, ConfirmationBox. This phase only builds the
mechanism — actually setting `playerReversible`/`revertVerb` per station is authoring work (B19).

### Verify

In the Editor: mark a test station `Satisfied`, confirm the reversal prompt appears with the right
verb, hold-to-revert returns it to `Pending`, and confirm a completed player's task step does **not**
un-complete when this happens (reversal never revokes — verify against a player who already
finished the task that station belongs to).

---

## B8 — Recipes & availability

**Depends on:** B3, B4. **Risk:** Low.

### Assembly correction (read the preamble above first)

`ItemRecipe` has a `TaskData producingTask` field — `TaskData` is a `Gameplay`-assembly type
(namespace `CorruptedCourt.Tasks`, compiled into `CorruptedCourt.Gameplay`), so `ItemRecipe` and
`RecipeIndex` **must** live in the `Gameplay` assembly, not `Items`, despite `ARCHITECTURE.md` §5
grouping them under "Layer A." Put them at `Gameplay/ItemRecipe.cs` (or a `Gameplay/Items/`
subfolder if you want them visually separated from the MonoBehaviours — either is assembly-safe).

### Do

1. `ItemRecipe` (`ScriptableObject`) exactly per §6.5's fields. `ItemSourceResolver`
   (`Gameplay/ItemSourceResolver.cs`, static or singleton — match the codebase's existing
   convention) per §9.3: `ExistsInWorld` scans `PickupItem.AllItems` (confirmed existing static
   list); `Resolve` walks recipes back to an infinite spawner, depth-capped at 3, cycle-guarded.
2. `RecipeIndex` asset/type making recipes queryable by output identity.
3. Delete the prerequisite auto-spawn block in `TaskManager.AssignTasksForNewStage` and the three
   `TaskData` fields it reads: `autoSpawnItemPrefab` (type `PickupItem`, confirmed), `autoSpawnLocationID`
   (string), `prerequisiteTask` (type `TaskData`) — all three currently sit under
   `[Header("Stage & Prerequisites")]` in `Tasks/TaskData.cs`; once they're gone only `allowedStages`
   remains there, so simplify the header to `[Header("Stage")]`. **These are serialized fields on a
   ScriptableObject asset — flag the deletion to the user first per CLAUDE.md**, even though the
   architecture doc calls for it (§III.5) and it's a clean removal, since any of the 30 `TaskData`
   assets that has one of these three fields set will silently lose that Inspector value.

### Don't

- Don't author any of the 7 recipe assets yet — that's B19a content work, not architecture.
- Don't touch `ItemSourceResolver`'s consumer (`WaypointManager`/`GetObjectiveTarget`) — that's B16.

### Verify

Compiles. Grep all 30 `TaskData` assets for non-empty `autoSpawnItemPrefab`/`autoSpawnLocationID`/
`prerequisiteTask` before deleting the fields, and report to the user which assets (if any) had
real values set, since that data is about to disappear.

---

## B9 — Deposit credit

**Depends on:** B6. **Risk:** Low but wide (touches every `TaskData` asset with a deposit step).

**Ruling confirmed** (`ARCHITECTURE.md` Part VI item 3): `requireOwnDeposit = true` is the correct
default. All three fixes in §III.4 are in scope for this phase.

### What's actually there today (verified)

`Tasks/Steps/DepositItemStep.cs`'s `requireOwnDeposit` field defaults to `false` today, with a
tooltip explicitly saying the default "preserves the old 'any matching item in the slot'
behaviour." Its `CheckCompletion` scans **every** `TaskLocation` with the matching ID via
`TaskLocation.AllLocations`, checking `station.GetSlotDepositor(i) != player` only when
`requireOwnDeposit` is set.

### Do

1. Flip the field default to `true`.
2. Scope the scan: with `TaskStationState` now available (B6), the step should only consider
   stations that were `Pending` when the player's current attempt began, not every station
   carrying the target ID regardless of who filled it when. Work out the exact "when the attempt
   began" bookkeeping against `TaskStepRuntime` if needed — this is the one piece of real design
   judgment left in this phase.
3. `StationInteractStep`'s `requiresStationPending` (added in B6b) is the same fix for the
   station-action shape of this bug — confirm it's wired, don't duplicate the fix.
4. Don't build the `TaskDataValidator` warning yet (that's B18) — but leave a `// TODO(B18):` note
   at the spot where a warning for `requireOwnDeposit == false` should eventually fire.

### Verify

Grep every existing `TaskData` asset for a `DepositItemStep` record and check whether any
explicitly serialize `requireOwnDeposit: 0` (false) — those assets are about to change behavior
the moment the default flips (an explicit `false` survives a default-value change; only assets that
never touched the field will pick up the new default). Report findings to the user.

---

## B10a — Minigame participants

**Depends on:** B1. **Risk:** Medium (split 1/2).

### What's actually there today (verified)

`Minigames/MinigameBase.cs` has **no** `participants` list — only the static `active`
`HashSet<MinigameBase>` registry (`ActiveMinigames`/`IsAnyActive`/`Current`, self-pruning) and a
single per-instance `player`/`activeTask` pair. `SetupMinigame` has two overloads:
`(PlayerController, TaskInstance)` (the real implementation) and `(MinigameContext)` (forwards to
the first). `CompleteMinigame()`/`CancelMinigame()` currently act on the single `player`/`activeTask`.

### Do

1. Add `public struct MinigameParticipant { public PlayerController Player; public TaskInstance Task; }`
   and `protected readonly List<MinigameParticipant> participants = new();` to `MinigameBase`.
2. `SetupMinigame` should seed `participants` with one entry built from the existing `player`/
   `activeTask` fields — so all 20 single-player minigames are unaffected without any change to
   their own code.
3. Add `public void AddParticipant(PlayerController p, TaskInstance task)`.
4. `CompleteMinigame()` should iterate `participants` and call the per-player finish path
   (`PlayerController.FinishMinigame(task)`) for each — a participant with a null `Task` gets
   nothing (R1: any action is open to anyone, only credit is gated); the minigame's own win/lose
   outcome is irrelevant to who gets credited (R7).

### Don't

- Don't touch `PartnerMinigame` yet — that's B10b.

### Verify

Compiles; existing single-player minigames (deposit family) behave identically — confirm by
running one in Play Mode.

---

## B10b — PartnerLink extraction

**Depends on:** B10a. **Risk:** Medium (split 2/2).

### What's actually there today (verified — the full current `PartnerMinigame` body)

`Minigames/Bases/PartnerMinigame.cs` is short: `dummyPartnerPrefab`, `dummyPartnerDistance`,
`dummyAutoAcceptDelay` fields; `Partner`/`PartnerIsDummy` properties; `OnMinigameBegin` calls
`PartnerResolver.Resolve(Context, player, dummyPartnerPrefab, dummyPartnerDistance, out dummy)`,
cancels if no partner, else `FacePartner()` + (`dummy.BeginAutoAccept(...)` if dummy) +
`OnPartnerBegin()`; `OnMinigameEnd` dismisses the dummy; a private `Update()` forwards to
`OnMinigameUpdate()`; `FacePartner()` rotates toward the partner; `MirrorOnPartner(trigger)` calls
`dummy.Play(trigger)` when the partner is a dummy (comment: real remote players need this over the
network later); `PartnerAccepted` reads `dummy.HasAccepted` (or `true` for a real partner).

### Do

1. New `Minigames/PartnerLink.cs` (plain class, no `MonoBehaviour`) — move this body into it almost
   verbatim per §10.2: `Partner`, `PartnerIsDummy`, `PartnerAccepted` properties; `Resolve(ctx,
   initiator, dummyPrefab, distance)` (wraps the `PartnerResolver.Resolve` call);
   `FaceEachOther()` (was `FacePartner`); `Mirror(animTrigger)` (was `MirrorOnPartner`); `Dismiss()`
   (was the dummy-dismiss half of `OnMinigameEnd`).
2. Rewrite `PartnerMinigame` to compose a `PartnerLink` instead of doing the work itself — same
   public surface (`Partner`, `PartnerIsDummy`, `FacePartner`, `MirrorOnPartner`, `PartnerAccepted`),
   just delegating. **Nothing that currently calls into `PartnerMinigame` should need to change.**
3. New `Minigames/PartnerHandMinigame.cs : HandMinigame` — `protected PartnerLink Link;` resolved in
   an `OnHandMinigameBegin` override, using the same `PartnerLink.Resolve(...)` call.
4. `Minigames/Partner/DummyPartner.cs`: add a reach pose and `PerformEmote(EmoteDefinition)` (can be
   a stub that just plays an anim trigger for now — B14 gives it real meaning) per §IV.3 — three
   tasks are untestable solo without the reach pose.

### Verify

Existing `PartnerMinigame` subclasses (if any are instantiated yet) behave identically. This is the
extraction the doc calls "mechanical" — if the diff looks like more than moving code around plus a
thin delegation layer, stop and reconsider.

---

## B11 — Mutual reach

**Depends on:** B10. **Risk:** Medium.

### Do

New `Minigames/MutualReachMinigame.cs : PartnerHandMinigame` per §10.3: `anchorHeight`,
`reachTolerance`, `consentTimeout` serialized fields. Anchor is a **session-owned** object spawned
at the midpoint between both players, not parented to either — despawned on end. Flow: initiator
interacts (hand out), partner may interact back (hand out), both guide their hand to the anchor,
confirm with left click. Refusal is silent and free — a partner who never reaches times out via
`consentTimeout` and the session cancels with nothing changed (no partial credit, no state
mutation).

Two concretes: `HandshakeMinigame` (empty hands) and `CheersMinigame` (glass rims meet instead of
palms) — build the base fully generic enough that both are thin subclasses, but don't build the
concretes themselves in this phase unless trivial; they're really B20 content once `handShake_court`/
`wineGlass_cheers` are implemented. `DuelMinigame : PartnerHandMinigame` is explicitly separate
(mouse-swing arm control, three-hit counter, no shared anchor) — not part of this phase either.

### Verify

Solo test: initiate against a `DummyPartner`, confirm the auto-accept path completes the reach.
Test the timeout path: initiate, don't complete the reach, confirm it cancels cleanly with no
leftover state (no orphaned anchor object, no stuck player controls).

---

## B12 — Emote data

**Depends on:** B1. **Risk:** Low.

### What's actually there today (verified — and a hazard `ARCHITECTURE.md` doesn't flag)

`Minigames/Emote/EmoteSystem.cs`:
```csharp
public enum EmoteCategory { Speech, Dance, Conversation, Gesture, Taunt }   // Speech=0, Dance=1, Conversation=2, Gesture=3, Taunt=4

public class EmoteDefinition : ScriptableObject {
    public string displayName = "Emote";
    public Sprite icon;
    public EmoteCategory category = EmoteCategory.Gesture;
    public string animTrigger = "";
    public bool loops = false;
    public float durationSeconds = 2f;
    public bool partnered = false;
}
```
Target per §11.1: `{ Gesture, Speech, Dance, Music }`.

**Unity serializes enum fields by their underlying int, not by name.** Reordering/removing members
silently remaps every existing `EmoteDefinition` asset's `category` value: old `Speech(0)` becomes
new `Gesture(0)`, old `Dance(1)` becomes new `Speech(1)`, old `Conversation(2)` becomes new
`Dance(2)`, old `Gesture(3)` becomes new `Music(3)`, and old `Taunt(4)` becomes an undefined value
with nothing to display. This is the same class of hazard as CLAUDE.md's "deleting a serialized
field" rule, just for enum members instead of fields — **flag it to the user before changing the
enum**, and first check how many `EmoteDefinition` assets actually exist and what category each is
set to (`ARCHITECTURE.md` §III.8 estimates ~13 total, and Layer F's own framing — "nothing behind
it but a stub" — suggests few if any are seriously authored yet, but verify rather than assume).

### Do

1. Change `EmoteCategory` to `{ Gesture, Speech, Dance, Music }`.
2. `EmoteDefinition`: remove `partnered` (the step owns partner rules now — B14); add
   `public string emoteID;`, `public string broadcastVerb;`,
   `public ItemDefinitionSet requiresHeldItem;` (from B3).
3. New `Minigames/Emote/PlayerEmotes.cs` — sibling `MonoBehaviour` to `PlayerController` (check how
   other siblings like `PlayerTaskBook` declare `[RequireComponent(typeof(PlayerController))]` and
   match that pattern): `Current`, `IsPerforming`, `StartedAt`, `EndsAt` properties;
   `Perform(EmoteDefinition)` plays the clip, holds `IsPerforming` for `durationSeconds` (or until
   `Stop()` when `loops`), raises a **new** `GameEvents.EmotePerformed` event (doesn't exist yet —
   add it, matching the existing `Raise*` pattern), shows the nameplate.
4. New `Minigames/Emote/EmoteNameplate.cs` — world-space billboard, `"<DisplayName> <broadcastVerb>."`.
   Must read `PlayerController.DisplayName` (the public accessor — confirmed the backing field is
   private `displayName`), never the GameObject name, so `StolenHeraldry` carries through.

### Don't

- Don't touch `EmoteWheelController` yet — that's B13.
- Don't build `EmoteStep` yet — that's B14.

### Verify

Before touching the enum: report to the user how many `EmoteDefinition` assets exist and their
current `category` values. After: open each one in the Editor and confirm its category reads as
intended, fixing any that landed on the wrong value by hand.

---

## B13a — Emote wheel script

**Depends on:** B12. **Risk:** Medium (split 1/2).

### What's actually there today (verified)

`Minigames/Emote/EmoteWheelController.cs`: `public KeyCode openKey = KeyCode.B;` (already correct
per the doc), `public void Open(PlayerController player, EmoteCategory? filter, Action<EmoteDefinition> onCommit)`,
`public void Commit(EmoteDefinition choice)` — currently fires the animator trigger directly
(`performer.GetComponentInChildren<Animator>().SetTrigger(choice.animTrigger)`) and invokes the
callback; no radial UI exists yet (`Open` doesn't show anything).

### Do

1. Widen the filter: `public struct EmoteFilter { public EmoteCategory? category; public EmoteDefinition specific; public bool freeChoice; }`,
   `Open(PlayerController player, EmoteFilter filter, Action<EmoteDefinition> onCommit)`. Keep the
   old `EmoteCategory?` overload as a thin forwarder building `new EmoteFilter { category = filter }`
   so nothing currently calling `Open` breaks (nothing does yet, since the radial UI never shipped —
   but keep the same additive-migration discipline anyway).
2. `Commit` should call `PlayerEmotes.Perform(choice)` on the performer instead of poking the
   Animator directly — `PlayerEmotes.Perform` (B12) now owns clip-playing, `IsPerforming`, and the
   `GameEvents.EmotePerformed` broadcast.
3. Gate `Open`: refuse while `MinigameBase.IsAnyActive`, or the player is strangling, or arrested
   (check `PlayerController`/`PlayerStrangle` for the exact flag names).
4. Never filter by the player's task — `freeChoice` is the only mode tasks use; the step (B14)
   judges the result afterward, since filtering would broadcast to observers which category their
   neighbor needs.

### Don't

- Don't build the actual radial UI yet — that's B13b. This phase is the state machine + gating
  logic only; `Open` can keep being a no-op for the visual side until B13b lands.

### Verify

Compiles. Unit-testable without the UI: call `Open` with a `freeChoice` filter and a test callback,
call `Commit` with a specific `EmoteDefinition`, confirm `PlayerEmotes.Perform` actually fires and
the callback receives the choice.

---

## B13b — Emote wheel UI

**Depends on:** B13a. **Risk:** Medium (split 2/2).

### Do

Build the actual two-ring radial Canvas prefab: hold B → outer ring of the four `EmoteCategory`
values; hover a category → inner ring of its `EmoteDefinition`s; release on an emote to commit,
release off the wheel to cancel. Emotes whose `requiresHeldItem` is unmet still appear and still
play (mime a trumpet empty-handed) — they just won't satisfy an `EmoteStep` later (B14 checks that,
not the wheel).

### Verify

Play Mode: hold B, confirm the outer ring appears, hover each category, confirm its inner ring
populates from the real `EmoteDefinition` catalogue, commit one, confirm the nameplate (B12) shows
above the performer's head with the right broadcast line.

---

## B14 — Emote step

**Depends on:** B12. **Risk:** Low.

### Do

New `Tasks/Steps/EmoteStep.cs` per §11.5, exact fields (`requiredCategory`, `requiredEmote`,
`requiredHeldItemSet`, `partner` (`PartnerRule: None | SameCategory | MatchingItemEmote`),
`partnerMaxDistance`, `partnerMustShareZone`, `requiredZoneID`). Evaluated on
`TaskEvalReason.EmotePerformed` (the enum value already exists from B1 — this is its first real
call site: `PlayerEmotes.Perform` should raise the evaluation for the performer **and** every
player within scan range, per §7.1's table, so either dancer completing second satisfies both
assignments). Checks in order: emote matches (`requiredEmote` or category), held-item requirement,
zone requirement (checked **at emote time**, not just via the preceding `NavigateStep` — a player
who leaves the zone after navigating there shouldn't still pass), partner rule (a living player
within `partnerMaxDistance`, same zone if `partnerMustShareZone`, `PlayerEmotes.IsPerforming` true,
emote satisfies the rule — overlap in time, not sequence).

### Verify

Solo test each of the four rows in §11.5's table (`instrument_play`, `podium_speech`, `dance_court`,
`converse_court`) is out of scope here (that's B20 content) — but exercise the step generically:
author one throwaway test `TaskData` with an `EmoteStep`, confirm it completes when the emote/zone/
partner conditions align and doesn't when any one of them doesn't.

---

## B15a — Minigame production capabilities

**Depends on:** B4, B6, B10. **Risk:** Low (split 1/2).

### Do

New `Minigames/MinigameProduct.cs` (not `Items/` — touches `PickupItem`) per §9.4: `Mode` enum
(`FlagHeldItem, SpawnAtStation, SpawnIntoHand, FillContainerStation`), serialized as a **list**, not
a single entry (the pour minigame needs two effects at once). Apply on success through
`ItemLifecycle` (B4). `SpawnAndCarryObjective` (already exists per §III.1) covers the spawn case —
fold it in as one of the modes rather than duplicating spawn logic.

New `Minigames/MinigameToolLoan.cs` per §10.4: `Begin(player)` stashes both hands and spawns/equips
a tool; `End()` despawns the tool and restores the stash. Base this on the existing single-item
version already on `PlayerInventory` (`itemSwappedToLeftHand`/`ReturnSwappedItem`, confirmed real)
rather than building stash logic from scratch.

### Verify

Compiles. No consumer yet in this phase (B20 tasks wire these) — verify by writing a throwaway test
minigame that applies a two-effect `MinigameProduct` list and confirms both effects land.

---

## B15b — SharedStationMinigame + instrument deletion

**Depends on:** B15a. **Risk:** Low (split 2/2).

### Do

1. New `Minigames/SharedStationMinigame.cs` — two seats via `TaskStationState` (B6), one shared
   session, crediting both via `MinigameParticipant` (B10a). This is what `game_play` will use —
   the chess endgame ruling (`ARCHITECTURE.md` Part VI item 1: King+Rook vs. King, checkmate/
   stalemate/resignation/either-leaves all end the session crediting both) only matters when
   `game_play` itself is authored (B20); this phase just needs the base to support an arbitrary
   terminal condition ending the session with all participants credited.
2. Delete `Minigames/Bases/EmoteMinigame.cs`, `Minigames/Bases/InstrumentMinigame.cs`, and its two
   subclasses (Wind/String — confirm their exact file names, not listed in the verified file list
   above, so grep `Minigames/Bases/` for anything referencing `InstrumentMinigame` as a base).
   Confirmed safe per Part VI item 5 (instruments are played via the matching emote, permanently —
   no future physical minigame planned) and §III.1 (neither has any shipped concrete yet).
3. Check `Minigames/Capabilities/` for anything used only by the deleted bases (e.g. a
   note-key/strum helper inside `MinigameInput.cs`) — don't delete a shared capability wholesale if
   `MinigameInput` also serves other minigames; only remove the instrument-specific pieces.

### Verify

Compiles with the three files gone. Grep the whole `Minigames/` tree for any remaining reference to
`EmoteMinigame`/`InstrumentMinigame`/`WindInstrumentMinigame`/`StringInstrumentMinigame` before
declaring this done — a stray reference means something else depended on them that wasn't
accounted for.

---

## B16 — Objectives

**Depends on:** B6, B8. **Risk:** Low.

### Do

New `Tasks/ObjectiveTarget.cs`: `struct { Transform Transform; string Hint; bool IsDirect; }`. Add
`public virtual ObjectiveTarget GetObjectiveTarget(PlayerController player, TaskStepRuntime runtime) => default;`
to `TaskStep`, following the exact stateless/runtime-aware split already established by
`GetObjectiveText`/`GetObjectiveText(runtime)`. Each step overrides it, delegating to:
- `ItemSourceResolver.Resolve` (acquire steps, B8)
- `StationRegistry.FindNearestPending` (station and deposit steps, B6)
- the zone itself (navigate steps)
- nearby-player scans (emote and partner steps)

Rework `UI/WaypointManager.cs` from its current step-type if/else chain into a renderer that just
calls `activeStep.GetObjectiveTarget(player, runtime)` and displays whatever comes back — this is
the phase that actually deletes the long pattern-match, so read the current method in full before
starting; it's likely the single largest method in that file.

### Verify

In the Editor: for at least one task per step family (acquire, deposit, station, navigate), confirm
the waypoint points at the right thing, including the R2 fallback (no item in world → points at a
producing station with the recipe's `objectiveHint`) and the R4 edge case (every candle lit → no
pending candle to point at — confirm this degrades gracefully, e.g. "nothing pending" text, rather
than crashing or pointing at null).

---

## B17 — Order recall

**Depends on:** B2, B6. **Risk:** Low.

### Design reference (DataRetrievalStep is deleted by the time this phase runs — copy this pattern inline, don't go looking for the file)

The two-phase runtime-flagging mechanism `OrderRecallStep` reuses was verified directly against
`DataRetrievalStep` before it was deleted in B2. Its shape: the **stateless** `CheckCompletion`
always returns `false` (a multi-phase step can never complete via the stateless predicate alone);
all real logic lives in the **runtime-aware** 3-arg overload. On reaching the source station, if
the runtime hasn't recorded a value yet, record it, set a one-shot `*Pending` flag, call
`player.TaskBook.RefreshLocalWaypoints()` (which raises `GameEvents.LocalTasksChanged`), and return
`false` — the view (previously `UIManager`) reacts to the pending flag off that signal and clears it
once it has shown its popup. On reaching the input station with a recorded value, set a second
one-shot pending flag the same way and return `false` again — the step only actually completes when
whatever UI reads that second flag calls `TaskInstance.CompleteActiveStep()` directly (previously
`UIManager.SubmitDataCode`), not through `CheckCompletion`'s return value.

### Do

1. New `Tasks/Steps/OrderRecallStep.cs` per §7.3: `sourceStationID` (Ledger), `inputStationID`
   (ConfirmationBox), `sequenceLength = 3`.
2. `TaskStepRuntime`: add `ObservedSequence` (whatever type represents a sequence — likely
   `List<int>` or `string`, match however the Ledger station is going to expose "the fixed sequence
   for this match"), `HasObserved`, `SequenceRevealPending`, `InputPending` — replacing the four
   fields B2 removed, same one-shot mechanism.
3. The sequence itself belongs to the Ledger station (a scene component, fixed for the match) —
   `OrderRecallStep` copies it into the runtime when the player reads it. The `ConfirmationBox`
   result is per-player for free, since `TaskStepRuntime` already is — and per Part VI item 4,
   `ConfirmationBox` takes **no seat cap**, multiple players may use one simultaneously.
4. `UI/UIManager.cs`: build the drag-and-drop order-recall panel that reads `SequenceRevealPending`/
   `InputPending` off the local player's runtime the same way the deleted data-code popup did,
   replacing it (this is the "UIManager: Data-code popup / input panel become the order-recall
   panel" line from §III.3).

### Verify

Solo test: read the sequence at the Ledger, confirm the reveal panel shows it once; walk to the
ConfirmationBox, confirm the input panel opens; submit the correct order, confirm the step
completes; submit an incorrect order, confirm it doesn't and can be retried.

---

## B18a — TaskDataValidator

**Depends on:** B6, B7, B8, B9, B14, B16, B17 (needs every type it inspects to exist). **Risk:** Low (split 1/2).

### Do

New `Editor/TaskDataValidator.cs`. Walk every `TaskData` asset and report, per §IV.3:
- Steps with no matching station in the scene.
- Item identities with no `ItemDefinition`.
- Deposit targets with no `TaskDepositStation`.
- `DepositItemStep` with `requireOwnDeposit == false` (the B9 warning that phase deferred here).
- Emote steps whose category has no authored `EmoteDefinition` assets.
- Emotes with no `broadcastVerb`.
- Recipes with unreachable inputs.
- Station types with no reversible instance.
- Any `locationID`/`taskID` containing invisible/non-printable characters.

Model it as an `EditorWindow` or a menu-triggered report, matching how `Editor/TaskStepDrawer.cs`/
`Editor/ReserializeTaskData.cs` are already invoked (check their menu attributes and mirror the
convention rather than inventing a new one).

### Verify

Run it against the current (B1–B17-migrated) `TaskData` assets and confirm it reports real,
verifiable issues — not false positives against assets that are actually fine.

---

## B18b — StationDebugPanel + force cheats

**Depends on:** B18a. **Risk:** Low (split 2/2).

### Do

New `Editor/StationDebugPanel.cs` (or a runtime debug overlay, whichever this codebase's existing
debug tooling convention favors — check for a precedent before choosing): lists every
`locationID`, its `TaskStationState` instances, and their current condition, sourced from
`StationRegistry`. Add force-satisfy / force-revert cheat buttons per station instance — per §IV.3,
"R4 is untestable without it," since a starved-to-zero-pending station type has no other way to
discover the problem short of manually walking the whole map.

### Verify

Open the panel in Play Mode, confirm it lists every real station instance with a live-updating
condition, and that force-satisfy/force-revert actually flip `TaskStationState.Current` and fire
the same `GameEvents.StationConditionChanged` a real interaction would.

---

## B19a — Item-side authoring

**Depends on:** B18. **Risk:** Low (split 1/2).

### Do

- `variantNames` entries for the four known variants: `Sword+Processed`, `Plate+DepositedContainer`,
  `Platter+DepositedContainer`, `Glass+DepositedContainer`.
- `ItemDefinitionSet` asset: `Instrument = {Guitar, Trumpet, Flute}`.
- 7 `ItemRecipe` assets per §6.5: `Sword→Processed`, `Cake→CakePiece`, `Turkey→TurkeyLeg`,
  `CakePiece→Plate`, `TurkeyLeg→Platter`, `WinePitcher→Glass`, `Arrow→Quiver`, plus a `RecipeIndex`
  asset.
- Rename `quiver_fill` → `arrow_quiver` (update `taskID` too, not just the asset filename).
- Rename `sword_polish` → `sword_armory` (steps already match the spec: `Acquire(Sword) →
  Process(Sword) → Deposit(Sword + Processed → Armory)` — no step changes needed, just the rename).
- Delete the `bow_string` task asset (owner ruling, §III.5).

Use `git mv` for every asset rename (CLAUDE.md: never delete-and-recreate, or every reference to
that asset breaks) and check for any prefab/scene/other-`TaskData` reference to the old
`taskID`/filename before finalizing.

### Verify

Open each renamed/new asset in the Editor and confirm it Inspector-validates cleanly (no
`TaskDataValidator` warnings from B18a).

---

## B19b — World-side authoring

**Depends on:** B18. **Risk:** Low (split 2/2).

### Do

- Add `TaskStationState` to every station prefab that needs one, with the reversibility/verb table
  from §8.2 (Candle/Painting/Banner/every deposit station reversible; Cake/Turkey/EmptyGlass/
  WeddingContract/ChessBoard/ArcheryTarget/Ledger/ConfirmationBox not). Place multiple scene
  instances where R3 wants a pool (so one player filling one vase doesn't starve everyone else).
- New stations: WineCask (Cellar), ConfirmationBox (no seat cap — Part VI item 4), Ledger,
  ChessBoard, ArcheryTarget, and a CourtYard zone.
- ~13 emote assets across the four categories, each with a real `broadcastVerb` (not derived —
  authored per §11.2, since English third-person conjugation breaks on the first irregular verb).
- Set `taskTier` per the specification's §5.2 on all 30 `TaskData` assets (tier spread should land
  10/10/10 per Part I §3 — this is the thing that keeps `TaskManager.TierWeightAverage()`
  calibrated, not approximated).

### Verify

Run `TaskDataValidator` (B18a) and `StationDebugPanel` (B18b) against the fully authored world;
confirm zero warnings and that every station type has at least one reversible instance and a
nonzero pending count reachable in a fresh match.

---

## After B19

All architecture is in place. Hand in the 30 tasks one at a time against Part V's fixed contract —
each task prompt should be scoped to exactly that task's deliverables (item/recipe/station/
TaskData/minigame/emote/objective/solo-test per §V) and nothing that touches the evaluation spine,
adds a base class, or adds a step type — those would mean a gap in this architecture, not a normal
task prompt, per Part V's own scope boundary.

---

## DEFERRED — B13b Editor setup (not yet done)

`EmoteWheelUI`/`EmoteWheelSlice` (`Assets/Scripts/UI/`) are written and compile, but the wheel is not
wired up in the scene/prefabs yet — this is Editor-only work Claude can't do itself. Nothing later
depends on this being done immediately, but the wheel is not testable in Play Mode until it is:

1. Build one slice prefab: an `Image` (background — doubles as the highlight tint) with a child
   `TextMeshProUGUI` label, `EmoteWheelSlice` component on the root wired to both. RectTransform
   anchored/pivoted at center (0.5, 0.5), same as the nameplate text.
2. In a Screen Space - Overlay canvas (the existing HUD canvas is fine — unlike the nameplate, this
   one should NOT be World Space): a root panel GameObject for `wheelRoot` (can start inactive),
   containing two empty `RectTransform` children — `outerRingRoot` and `innerRingRoot` — both
   anchored to screen center. Double-check their scale is (1,1,1), not (0,0,0) - that exact mistake
   bit the nameplate setup in B12.
3. Add `EmoteWheelUI` to that panel (or a manager object) and wire `wheelRoot` / `outerRingRoot` /
   `innerRingRoot` / `slicePrefab`. Leave the radius/deadzone fields at their defaults to start.
4. Create a few actual `EmoteDefinition` assets and add them to `EmoteWheelController`'s `catalogue`
   to see the inner ring populate with anything — none exist yet as of B12.

Once done, Play Mode verify (from B13b): hold B, confirm the outer ring (4 categories) appears, hover
one, confirm its inner ring populates, release on an emote, confirm the nameplate shows the right
line above your head; release off any slice, confirm it cancels cleanly.

---

## DEFERRED — B17 Editor setup (not yet done)

`LedgerStation`, `OrderRecallStep`, and the order-recall UI code in `UIManager.cs` are written and
compile, but nothing is wired up in the scene/prefabs yet — this is Editor-only work Claude can't do
itself. Nothing later depends on this being done immediately, but order-recall is not testable in
Play Mode until it is:

1. Build one reusable numbered-order button prefab: a `Button` with a child `TextMeshProUGUI` label
   (the label text gets overwritten at runtime with each position's number — any placeholder text is
   fine).
2. Build the reveal panel: a `GameObject` (start inactive) with a `TextMeshProUGUI` for the revealed
   order and a `Continue` button. Wire the button's `OnClick` to `UIManager.OnOrderRevealContinuePressed`.
3. Build the input panel: a `GameObject` (start inactive) with a `TextMeshProUGUI` for the in-progress
   submission, an empty child `RectTransform`/`Transform` container for the spawned number buttons,
   and a `Cancel` button. Wire the button's `OnClick` to `UIManager.OnOrderInputCancelPressed`.
4. On the `UIManager` component, wire: `orderRevealPanel`, `orderRevealText`, `orderInputPanel`,
   `orderInputProgressText`, `orderInputButtonPrefab` (from step 1), `orderInputButtonContainer`
   (from step 3).
5. For testing (no real Ledger/Confirmation Box placement exists yet — that's B19b): make one test
   object with `TaskLocation` (a `locationID`, e.g. `"Ledger"`) + `TaskStation` + `LedgerStation`
   (set `Sequence Length`, e.g. `3`), and a second test object with `TaskLocation`
   (`"ConfirmationBox"`) + `TaskStation`. Author one throwaway `TaskData` with a single
   `OrderRecallStep`: `Source Station ID = "Ledger"`, `Input Station ID = "ConfirmationBox"`.

Once done, Play Mode verify (from B17): interact with the Ledger test object, confirm the reveal
panel shows the order once; walk to the Confirmation Box test object and interact, confirm the input
panel opens with that many numbered buttons; click them in the wrong order, confirm the submission
clears and the panel stays open for an immediate retry; click them in the right order, confirm the
step (and task, if it was the last step) completes and both panels close.
