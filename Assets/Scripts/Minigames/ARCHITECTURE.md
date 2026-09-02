# Minigame Architecture

Design for building the ~25 upcoming minigames on a shared, professional foundation, and
folding the 6 existing minigames into it.

> **Status:** scaffolding only. The abstract bases and capability helpers in this folder
> compile and are ready to extend. Nothing in the shipping game is wired to them yet — the
> existing minigames still run on their own code. The migration plan at the bottom moves them
> over in safe, verifiable phases.

---

## 1. Why change anything

The current setup works but doesn't scale to 25 more minigames:

| Problem | Evidence today | Cost at 25 minigames |
|---|---|---|
| **Two launch paths, inconsistent state** | `PlayerController.StartMinigame` sets `isPlayingMinigame`; `TaskDepositStation.LaunchDepositMinigame` does **not** — the hand-grip fix had to test `isPlayingMinigame \|\| hangReachActive` to cover both | Every cross-cutting feature (grip, waypoint hiding, "am I busy" checks) needs the same special-case |
| **Shared physical-hand code is copy-pasted** | `SwordHangMinigame` and `ChestDepositMinigame` each re-implement: freeze player, mouse→world reach, RMB look-around, WASD footwork, settings-menu pause, guided physics drop, downward contact ray, restore control (~200 lines, duplicated) | Pour, polish, banner, picture, candle, vase, gift-box, bow, instrument, handshake, write all need most of that. 12× duplication. |
| **No emote/radial system** | none exists | Speech, Dance, Conversation minigames are blocked |
| **No partner / single-player-test story** | `DummyTestHelper` force-equips an item and stops there | Handshake, cheers, dance, conversation, duel all need "target another player" + a fake partner to test solo |
| **No "produce an item, then carry it somewhere" pattern** | `CakeCuttingMinigame` spawns a `CakePiece` and just drops it | Cake and turkey both need cut → spawn → sub-objective "put it on the plate" |
| **No chained minigames** | none | Cheers must auto-start Drink |

---

## 2. The layer cake

```
                        ┌───────────────────────────────┐
                        │        MinigameBase           │  lifecycle + global "is any minigame running"
                        │  (MonoBehaviour, abstract)    │  registry + MinigameContext payload
                        └───────────────┬───────────────┘
             ┌────────────────┬─────────┴───────────┬────────────────────┐
             │                │                     │                    │
     ┌───────▼──────┐  ┌──────▼───────┐   ┌─────────▼────────┐   ┌───────▼────────┐
     │ PanelMinigame│  │ HandMinigame │   │ PartnerMinigame  │   │ EmoteMinigame  │
     │ pure-UI on a │  │ drives the   │   │ targets another  │   │ pick an emote  │
     │ Canvas       │  │ right hand   │   │ player + dummy   │   │ from a wheel   │
     └───────┬──────┘  └──────┬───────┘   └───────┬──────────┘   └────────────────┘
             │                │                   │
   Dummy, Book-of-names       │            Handshake, Cheers, Duel*,
   memory/order puzzles       │            EmotePartner (Dance/Conversation)
                              │
        ┌─────────────────────┼───────────────────────┬──────────────┬───────────────┐
        │                     │                       │              │               │
┌───────▼────────┐  ┌─────────▼────────┐  ┌───────────▼───┐  ┌───────▼──────┐ ┌──────▼────────┐
│ItemDeposit     │  │ToolOnTarget      │  │DragObject     │  │Pour          │ │Consume        │
│Minigame        │  │Minigame          │  │Minigame       │  │Minigame      │ │Minigame       │
│(carry & place) │  │(use held tool ON │  │(grab & move a │  │(vessel→vessel│ │(item → mouth) │
│                │  │ a target object) │  │ world object) │  │ tilt & fill) │ │               │
└───────┬────────┘  └────────┬─────────┘  └──────┬────────┘  └──────────────┘ └───────┬───────┘
        │                    │                   │                                   │
 Sword rack, dowry     Cake cut, turkey     Unfurl banner,                    Eat cake piece,
 chest, coins→box,     cut, polish sword,   straighten picture                drink wine
 vase→table, gift-     light candle,
 box→table, arrow→     light firework,
 quiver, firework      write on contract
 placement
                        ┌──────────────────────┐   ┌────────────────────────┐
                        │ ChargeReleaseMinigame│   │ InstrumentMinigame     │
                        │  (aim, draw, loose)  │   │ (hand-to-mouth / strum │
                        │   → bow & arrow      │   │  + A/S/D/F/G notes)    │
                        └──────────────────────┘   └────────────────────────┘

*Duel is a PartnerMinigame that also composes MinigameHandRig for mouse-swing arm control.
```

**Inheritance is for "how the player drives it." Everything else is composition** — the
capability components below are `new`'d up and held as fields, never subclassed.

---

## 3. Lifecycle & launch flow

### Canonical lifecycle (all minigames)

```
launcher builds a MinigameContext
      │
      ▼
Instantiate(prefab) ─► MinigameBase.SetupMinigame(context)
                              │  registers in MinigameBase.ActiveMinigames
                              │  stores Context, player, task
                              ▼
                       OnMinigameBegin()          ← your setup
                              │
                       (per-frame: OnMinigameUpdate / LateUpdate / FixedUpdate)
                              │
                    success ──┴── abandon / attacked / Esc
                       │                 │
             CompleteMinigame()   CancelMinigame()
                       │                 │
              OnMinigameEnd(true)  OnMinigameEnd(false)
                       │                 │
        player.FinishMinigame(task) player.CancelMinigame()
                       │                 │
                  Destroy(gameObject) ───┘   (unregisters)
```

`MinigameBase` now owns a **static registry** (`ActiveMinigames`, `IsAnyActive`, `Current`) that
self-prunes destroyed entries. This is the single source of truth for "is the player in a
minigame" — replacing the split between `isPlayingMinigame` and ad-hoc `hangReachActive` checks.

### The two launchers both build a context

- **`PlayerController.StartMinigame(prefab, task, target)`** — camera/body snapping, item→left-hand
  swap for "Item" minigames, `MinigameTargetType` resolution. Keeps doing all that; additionally
  packs it into a `MinigameContext` and calls `SetupMinigame(context)`.
- **`TaskDepositStation.LaunchDepositMinigame(player, heldItem)`** — builds a context with
  `TargetType = Station`, `Station = this`, `HeldItem = heldItem`, and calls `SetupMinigame(context)`
  then `BeginDeposit(...)`.

Legacy `SetupMinigame(player, task)` still exists and still works — `SetupMinigame(context)` just
forwards to it after stashing the context. Migration can be file-by-file.

### `MinigameContext` fields

`Player`, `Task` (null = faked/standalone), `TargetType`, `Target` (GameObject), and the
convenience casts `HeldItem`, `Station`, `PartnerPlayer`.

---

## 4. Base classes

### `MinigameBase` — *(exists, extended)*
Lifecycle, static active-registry, `MinigameContext`, `OnMinigameBegin()` / `OnMinigameEnd(bool won)`
hooks. No behaviour change for existing subclasses.

### `PanelMinigame : MinigameBase` — *new*
Pure UI on a Canvas prefab. No world/hand interaction. Provides `RequireClicks(n, onDone)` and a
`Win()` / `Lose()` shortcut so button-wired minigames don't each re-count. Cursor is already
unlocked by the launcher.
**Uses:** `DummyMinigame`, the **Book-of-names memorize-and-order** puzzle, and the fallback
"just click a button" flavour of any physical minigame (useful for AI / fakers).

### `HandMinigame : MinigameBase` — *new, the workhorse*
Everything the two deposit minigames duplicate, lifted once:
- `OnMinigameBegin()` resolves the camera + builds a `MinigameHandRig`, freezes the player
  (`SetControlsLocked(true)`), then calls `OnHandMinigameBegin()`.
- `void Update()` handles the **settings-menu pause**, **RMB hold-to-look** (+ quick-tap cancel),
  **WASD footwork leash**, then calls `OnMinigameUpdate()`.
- `void LateUpdate()` / `void FixedUpdate()` forward to `OnMinigameLateUpdate()` /
  `OnMinigameFixedUpdate()`.
  > **Convention:** subclasses override `OnMinigameUpdate()` etc. — they must **not** declare their
  > own `Update()` (it would hide the base loop). This is the one rule that keeps the hierarchy sane.
- `MouseWorld()` → the mouse position projected `reachDistance` in front of the camera.
- `RestorePlayer()` — un-freeze, re-lock cursor, drop the hand rig.
- Serialized knobs shared by all: `reachDistance`, `walkRadius`, `rmbLookSensitivity`,
  `rmbTapCancelTime`.

### `PartnerMinigame : MinigameBase` — *new*
For "do something *with* another court member." `OnMinigameBegin()` runs `PartnerResolver`:
- If `Context.PartnerPlayer` is set (aimed at a real player) → use it.
- Else, in a solo test, spawn / borrow a `DummyPartner` in front of the player.
Provides `Partner` (a `PlayerController`), `FacePartner()`, and `MirrorOnPartner(string trigger)` so
the handshake/cheers/dance animation plays on both sides. Success predicate is per-subclass.

### `EmoteMinigame : MinigameBase` — *new*
Opens the `EmoteWheelController` filtered to a required `EmoteCategory`; completes when the player
commits an emote from that category. No partner, no hand rig.
**Uses:** **Speech at the podium** (category `Speech`). `Dance`/`Conversation` reuse the wheel but
also need a partner, so they extend `PartnerMinigame` and *compose* the wheel (see §6).

### Mid-tier bases under `HandMinigame`

| Base | Responsibility | Concrete minigames |
|---|---|---|
| **`ItemDepositMinigame`** *(exists, re-parented onto `HandMinigame`)* | Carry the held item, aim it, release; a `GuidedDrop` fall; a `StationContactProbe` gate; seat into the nearest free `DropSlot`. Keeps `BeginDeposit(heldItem, station)`. | sword rack, dowry chest, **coins → gift box**, **vase → table**, **gift box → table**, **arrow → quiver**, **firework placement** |
| **`ToolOnTargetMinigame`** *new* | Held item is a *tool*; a world object is the *target*. Move the tool over the target and act (click / rub / spark). A `MinigameProgressTracker` drives 0→1 completion (N cuts, wiped area, sparks landed, ink used). Optional `SpawnOnComplete` + `CarryToObjectiveHint`. | **cake cut**, **turkey-leg cut**, **polish sword**, **light candle**, **light firework**, **write on contract** |
| **`DragObjectMinigame`** *new* | Click to grab a world object, then drag it. Success when a tracked value reaches target: a position along an axis (`AxisDrag`) or an angle toward level (`AngleDrag`). | **unfurl banner** (drag down 0→1), **straighten picture** (roll → ~0°) |
| **`PourMinigame`** *new* | Sequential multi-grab (empty vessel, then source vessel), tilt the source over the target, a fill meter rises while aligned + tilted; spill if mis-aimed. | **pour wine** |
| **`ConsumeMinigame`** *new* | Bring the held item (or a child of it) to the player's mouth anchor; a few "bites/sips" then destroy it. `simpleMode` = one button, for fakers. | **eat cake piece**, **drink wine** |
| **`ChargeReleaseMinigame`** *new* | Multi-grab (bow + quiver), nock on primary-down, pull `MinigameInput.DrawPull()` to charge, release to fire a projectile with force ∝ charge. | **bow & arrow** |
| **`InstrumentMinigame`** *new* | Abstract: an *activation gesture* + a *note trigger*. `WindInstrumentMinigame` raises the hand to the mouth anchor then reads `MinigameInput.NoteKeys()`. `StringInstrumentMinigame` requires a `MinigameInput.MouseStrum()` alongside each note key. | **flute**, **trumpet** (wind), **guitar** (string) |

---

## 5. Capability components (composition)

Held as fields, `new`'d in `OnMinigameBegin()`. Never subclassed.

| Component | Replaces / provides | Notes |
|---|---|---|
| **`MinigameHandRig`** | the scattered `player.hangReach*` pokes + the finger-curl grip | `ReachToward(world)`, `AimFromMouse(cam, dist)`, `SetGrip(0..1)`, `AttachItem(item, localPos, localEuler)`, `DetachItem()`, `Begin()` / `End()`. Both deposit minigames and the grip code funnel through this. |
| **`GuidedDrop`** | `ConfigureGuidedDrop(bool)` duplicated in Sword + Chest | `GuidedDrop.Begin(rb, col, settings)` returns a handle that restores constraints / max-ang-vel / physics-material on `End()`. Includes the no-bounce runtime `PhysicsMaterial` and the optional horizontal **funnel-to-slot** from the chest fix. |
| **`StationContactProbe`** | `RaycastTouchingRack()` / `RaycastTouchingChest()` | `static bool Resting(Transform item, Transform stationRoot, float dist = 0.12f)` — downward ray, filtered to the station's collider hierarchy. Works with non-convex mesh colliders. |
| **`MinigameInput`** | every minigame calling `Input.GetMouseButton…` directly | One static surface: `PrimaryDown/Primary/PrimaryUp`, `SecondaryHeld`, `MoveAxis`, `MouseScreenDelta`, plus the specialised reads `NoteKeys()` (A/S/D/F/G), `DrawPull()` (backward drag for the bow), `MouseStrum()` (fast vertical flick), `MouseSwing()` (screen-space velocity for the duel). Also the single place to gate input while the settings menu is open. |
| **`MinigameProgressTracker`** | ad-hoc `cuts` / `bites` counters + future wipe/spark/ink meters | A generic 0→1 progress with a completion threshold and an `OnCompleted` callback. Modes: `Count(n)`, `Accumulate(perTick)`, `Zones(subTargets[])` (each zone must be satisfied — polish, straighten-check), `TowardValue(get, target, tolerance)`. |
| **`SpawnAndCarryObjective`** | `CakeCuttingMinigame`'s loose spawn + manual follow-up | On complete: instantiate the produced `PickupItem`, name it, and push a transient "carry `X` to the `Plate`" objective / waypoint. Used by cake & turkey. |
| **`MinigameChain`** | (none today) | `NextMinigamePrefab` + optional delay. `Cheers` sets it to `DrinkWine`; on `CompleteMinigame()` the base launches the next with a fresh context. |

---

## 6. Emote system

Standalone — also usable outside minigames (general roleplay).

- **`EmoteCategory`** enum: `Speech`, `Dance`, `Conversation`, `Gesture`, `Taunt`, …
- **`EmoteDefinition`** — `ScriptableObject` (`[CreateAssetMenu]`): display name, icon, `EmoteCategory`,
  animator trigger/state, `loops`, `durationSeconds`, `partnered` flag.
- **`EmoteWheelController`** — a persistent UI service:
  `Open(EmoteCategory? filter, Action<EmoteDefinition> onCommit)`. Hold a key → wheel appears →
  mouse picks a slice → release commits. Plays the clip on the owning `PlayerController`.
  Categories tab across the top; each shows its `EmoteDefinition`s.

Minigames that use it:

| Minigame | Base | Wheel filter | Extra |
|---|---|---|---|
| Speech at podium | `EmoteMinigame` | `Speech` | complete on commit |
| Dance-together | `PartnerMinigame` + composes wheel | `Dance` | both players emoting from `Dance` within range, overlapping in time |
| Conversation | `PartnerMinigame` + composes wheel | `Conversation` | alternate N emotes between the two |

---

## 7. Partner minigames & single-player testing

**`PartnerResolver.Resolve(context, spawnPrefab)`** returns a `PlayerController`:
1. `context.PartnerPlayer` if the player aimed at a real court member.
2. Otherwise a **`DummyPartner`** — a stripped `PlayerController`-carrying prefab that:
   - stands where the resolver puts it (in front of the initiator, facing them),
   - exposes `PlayImmediate(trigger)` / `HoldPose(trigger)` so the mini-game can drive its half of
     the handshake / cheers / dance,
   - can be pre-equipped (wine glass, sword) via the existing `DummyTestHelper` pattern,
   - auto-"accepts" after a tunable delay so solo tests always complete.

`DummyTestHelper` stays as the editor convenience for *equipping* a dummy; `DummyPartner` is the
*behavioural* stand-in a `PartnerMinigame` talks to. Every partner minigame therefore runs solo.

---

## 8. Full mapping — every requested minigame

| # | Minigame | Base class | Capabilities | Task step it satisfies |
|---|---|---|---|---|
| 1 | Cut cake → cake piece → to plate | `ToolOnTargetMinigame` | ProgressTracker(count), SpawnAndCarryObjective, HandRig | `ProcessItemStep` (+ follow-up `AcquireItemStep`/`DepositItemStep`) |
| 2 | Cut turkey leg → turkey leg → to plate | `ToolOnTargetMinigame` | same as #1 | same as #1 |
| 3 | Coins bag → gift box | `ItemDepositMinigame` | GuidedDrop, StationContactProbe, HandRig | `DepositItemStep` |
| 4 | Pour wine (glass + pitcher) | `PourMinigame` | HandRig (multi-grab), ProgressTracker(accumulate), fill meter | `ProcessItemStep` producing "GlassOfWine" |
| 5 | Book of names → memorize → order at box | `PanelMinigame` | book-page UI, ordered-input check, station = confirmation box | `StationInteractStep` / new `SequenceRecallStep` |
| 6 | Handshake (RMB aim hand) | `PartnerMinigame` | PartnerResolver, DummyPartner, HandRig (RMB aim) | `PlayerInteractStep` |
| 7 | Polish dirty sword with cloth | `ToolOnTargetMinigame` | ProgressTracker(zones → dirt-mask reveal), HandRig | `ProcessItemStep` |
| 8 | Unfurl rolled banner | `DragObjectMinigame` | AxisDrag(local-down 0→1), HandRig | `ProcessItemStep` / `StationInteractStep` |
| 9 | Light candle with flint & steel | `ToolOnTargetMinigame` | ProgressTracker(sparks near wick), flame enable, HandRig | `ProcessItemStep` |
| 10 | Straighten crooked picture | `DragObjectMinigame` | AngleDrag(roll→0 ± tol), grab-on-primary, HandRig | `ProcessItemStep` / `StationInteractStep` |
| 11 | Vase → table | `ItemDepositMinigame` | GuidedDrop, StationContactProbe, HandRig | `DepositItemStep` |
| 12 | Gift box → table | `ItemDepositMinigame` | same as #11 | `DepositItemStep` |
| 13 | Eat cake piece off a plate | `ConsumeMinigame` | HandRig, mouth anchor, ProgressTracker(count) | `ConsumeItemStep` |
| 14 | Instruments: flute / trumpet / guitar | `InstrumentMinigame` → `WindInstrumentMinigame` / `StringInstrumentMinigame` | HandRig (hand-to-mouth / strum), MinigameInput.NoteKeys / MouseStrum | `StationInteractStep` (per-instrument prefab) |
| 15 | Cheers another member (holding wine) | `PartnerMinigame` | PartnerResolver, HandRig, MinigameChain → #16 | `PlayerInteractStep` (step 1 of a 2-step task) |
| 16 | Drink the wine (glass to lips) | `ConsumeMinigame` | HandRig, mouth anchor | `ConsumeItemStep` (step 2; auto-started by #15) |
| 17 | Write name on contract with pencil | `ToolOnTargetMinigame` | HandRig, ProgressTracker(ink budget = max stroke length), stroke renderer | `ProcessItemStep` / `StationInteractStep` |
| 18 | Speech at a podium | `EmoteMinigame` | EmoteWheelController(`Speech`) | `StationInteractStep` |
| 19 | Dance with another player | `PartnerMinigame` (+ wheel) | PartnerResolver, EmoteWheelController(`Dance`), proximity + time-overlap check | `PlayerInteractStep` / `MutualPlayerInteractStep` |
| 20 | Arrow → quiver | `ItemDepositMinigame` | GuidedDrop, StationContactProbe, HandRig | `DepositItemStep` |
| 21 | Firework → specific spot | `ItemDepositMinigame` | one tight `DropSlot`, small catchRadius | `DepositItemStep` |
| 22 | Light firework with flint & steel | `ToolOnTargetMinigame` | ProgressTracker(sparks), fuse VFX, HandRig | `ProcessItemStep` (often chained after #21) |
| 23 | Conversation via emotes | `PartnerMinigame` (+ wheel) | PartnerResolver, EmoteWheelController(`Conversation`), alternation check | `PlayerInteractStep` |
| 24 | Sword duel (mouse-swing, 3 hits) | `PartnerMinigame` (+ hand rig for arm) | PartnerResolver, DummyPartner, HandRig (mouse→arm), MinigameInput.MouseSwing, hit tracker | `MutualPlayerInteractStep` |
| 25 | Bow & arrow (nock, draw, loose) | `ChargeReleaseMinigame` | HandRig (bow + string, multi-grab), MinigameInput.DrawPull, projectile spawn | `StationInteractStep` / `ProcessItemStep` |

Duplicate in the prompt: "pick up a plate with a cake piece … simulate eating" appears twice →
one minigame (#13). "Gift box into a gift box" (#2, into the box) vs "gift box onto a table"
(#12, box is the carried item) are genuinely different and both map cleanly to
`ItemDepositMinigame` with different station prefabs.

New task-step types implied (small `TaskStep` subclasses, added when needed): `SequenceRecallStep`
(#5), and reuse of `MutualPlayerInteractStep` for dance/duel.

---

## 9. Where the 6 existing minigames land

| Existing | New home | What stays / changes |
|---|---|---|
| `DummyMinigame` | `PanelMinigame` | trivial: swap base, `OnClickWinButton` → `Win()` |
| `CakeCuttingMinigame` | `CakeCutMinigame : ToolOnTargetMinigame` | button `OnCut()` becomes the tracker's `Count` tick; the loose spawn + "go deposit it" becomes `SpawnAndCarryObjective`. Keep a `PanelMinigame` prefab variant for AI fakers. |
| `ConsumeItemMinigame` | `ConsumeMinigame` with `simpleMode = true` | current button behaviour = `simpleMode`; the physical hand-to-mouth version is `simpleMode = false` |
| `SwordHangMinigame` | `SwordHangMinigame : ItemDepositMinigame` (unchanged name) | loses ~120 lines to `HandMinigame` + `GuidedDrop` + `StationContactProbe`; keeps only the notch-slot pick + tip-down pose |
| `ChestDepositMinigame` | same | keeps only the lid open/close phase + the funnel-to-slot tuning |
| `ItemDepositMinigame` | re-parented: `ItemDepositMinigame : HandMinigame` | keeps `BeginDeposit(heldItem, station)`; gains the shared plumbing for free |

---

## 10. Phased migration (each phase compiles & is verifiable on its own)

- **P0 — now:** land this folder. No wiring. Game runs unchanged.
- **P1:** extract `GuidedDrop` + `StationContactProbe` from `SwordHangMinigame` /
  `ChestDepositMinigame`. Pure refactor — behaviour must be identical (test both deposits).
- **P2:** create `HandMinigame`; lift freeze / RMB-look / footwork / settings-pause / `MouseWorld`
  out of the two deposit minigames; re-parent `ItemDepositMinigame` onto it. The
  `isPlayingMinigame || hangReachActive` grip check collapses to `MinigameBase.IsAnyActive`.
- **P3:** route `PlayerController.StartMinigame` **and** `TaskDepositStation.LaunchDepositMinigame`
  through `MinigameContext` + `SetupMinigame(context)`. Make `PlayerController.isPlayingMinigame`
  a read-through of `MinigameBase.IsAnyActive`.
- **P4:** build `EmoteWheelController`, `PartnerResolver`, `DummyPartner` (unblocks 7 minigames).
- **P5:** implement concretes in dependency order:
  1. deposits (#3, 11, 12, 20, 21) — thin subclasses of the already-proven `ItemDepositMinigame`
  2. tool-on-target (#1, 2, 7, 9, 22, 17)
  3. consume / pour (#4, 13, 16)
  4. drag (#8, 10)
  5. partner (#6, 15, 19, 23, 24)
  6. emote (#18) and the wheel-backed partner ones
  7. instrument (#14) and bow (#25)

---

## 11. File map (this folder)

```
Minigames/
  ARCHITECTURE.md              this document
  MinigameContext.cs           launch payload
  Bases/
    PanelMinigame.cs
    HandMinigame.cs
    PartnerMinigame.cs
    EmoteMinigame.cs
    ItemDepositMinigame.cs     ← lives at Scripts/ today; moves here at P2
    ToolOnTargetMinigame.cs
    DragObjectMinigame.cs
    PourMinigame.cs
    ConsumeMinigame.cs
    ChargeReleaseMinigame.cs
    InstrumentMinigame.cs
  Capabilities/
    MinigameHandRig.cs
    GuidedDrop.cs
    StationContactProbe.cs
    MinigameInput.cs
    MinigameProgressTracker.cs
    SpawnAndCarryObjective.cs
  Emote/
    EmoteSystem.cs             EmoteCategory enum + EmoteDefinition SO
    EmoteWheelController.cs
  Partner/
    PartnerResolver.cs
    DummyPartner.cs
```

`MinigameBase.cs` stays at `Assets/Scripts/` (many prefabs reference it by GUID); it is edited in
place, additively.
