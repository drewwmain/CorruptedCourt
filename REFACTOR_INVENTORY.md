# REFACTOR_INVENTORY

Inventory of `Assets/Scripts/` (flat files + `Minigames/` scaffolding) as of the pre-refactor
snapshot. `Assets/Scripts/Editor/` and any auto-generated `InputSystem_Actions.cs` are out of scope
and only referenced where another file depends on them.

**Observation only. No fixes proposed.** Line counts are `wc -l` (newline count); files without a
trailing newline are one physical line longer — noted where it matters.

---

## 1. Type inventory

### 1a. Shipping gameplay — `Assets/Scripts/*.cs`

| Type | File | Lines | Kind | Responsibility (one line) |
|---|---|---:|---|---|
| `BearTrap` | `Assets/Scripts/BearTrap.cs` | 31 | MonoBehaviour (`[RequireComponent(Collider)]`) | Trigger volume: stuns a living non-Corrupted player who steps on it, then destroys itself. |
| `CakeCuttingMinigame` | `Assets/Scripts/CakeCuttingMinigame.cs` | 105 | MonoBehaviour : `MinigameBase` | Button-driven "cut the cake" minigame; on win spawns a loose `CakePiece` PickupItem in the world and destroys the held cake. |
| `ChestDepositMinigame` | `Assets/Scripts/ChestDepositMinigame.cs` | 588 | MonoBehaviour : `ItemDepositMinigame` | Physical dowry-chest deposit: reach + grab lid, mouse-drag it open, aim the held item, release → guided/funnelled fall → contact-gated seat into a `DropSlot`, auto-close lid. |
| `TaskStep` | `Assets/Scripts/TaskStep.cs` | 30 | abstract `[Serializable]` plain class | Base for a single task step: carries an optional `minigamePrefab`; abstract `GetObjectiveText()` / `CheckCompletion()`; virtual `ResetStep()`. |
| `AcquireItemStep` | `Assets/Scripts/ConcreteTaskSteps.cs` | (~7–22) | `[Serializable]` : `TaskStep` | Complete when the player holds an item named `requiredItemName` (either hand). |
| `NavigateStep` | `Assets/Scripts/ConcreteTaskSteps.cs` | (~28–42) | `[Serializable]` : `TaskStep` | Complete when `player.currentZoneID == targetZoneID`. |
| `StationInteractStep` | `Assets/Scripts/ConcreteTaskSteps.cs` | (~48–89) | `[Serializable]` : `TaskStep` | Complete when the interacted object's **name contains** `targetStationID`; optional N-players-nearby check via `Physics.OverlapSphere`. |
| `PlayerInteractStep` | `Assets/Scripts/ConcreteTaskSteps.cs` | (~95–128) | `[Serializable]` : `TaskStep` | Complete when the target is another `PlayerController` and the initiator holds `requiredHeldItemName` (or is empty-handed if blank). |
| `DataRetrievalStep` | `Assets/Scripts/ConcreteTaskSteps.cs` | (~134–200) | `[Serializable]` : `TaskStep` | Two-phase: interact with source station (name-contains) to generate `generatedCode`, then interact with input station; stores `hasCode`/`generatedCode` on the step. |
| `DepositItemStep` | `Assets/Scripts/ConcreteTaskSteps.cs` | (~206–end) | `[Serializable]` : `TaskStep` | Complete when a station with `locationID == targetStationID` holds an item matching its `acceptedItemName` or `requiredItemName`. **NOTE: its closing brace is at EOF — the five step types below are nested inside it (see §6.1).** |
| `DepositItemStep.ConsumeItemStep` | `Assets/Scripts/ConcreteTaskSteps.cs` | (~247–271) | `[Serializable]` : `TaskStep`, **nested in `DepositItemStep`** | Complete when the player holds `requiredItemName`; consumes it immediately if no minigame is attached. |
| `DepositItemStep.ProcessItemStep` | `Assets/Scripts/ConcreteTaskSteps.cs` | (~277–291) | `[Serializable]` : `TaskStep`, **nested** | Complete when the interacted object's **name contains** `targetStationOrItemName`. |
| `DepositItemStep.EquipClothingStep` | `Assets/Scripts/ConcreteTaskSteps.cs` | (~297–310) | `[Serializable]` : `TaskStep`, **nested** | Complete when the interacted object's **name contains** `clothingName`. |
| `DepositItemStep.MutualPlayerInteractStep` | `Assets/Scripts/ConcreteTaskSteps.cs` | (~316–346) | `[Serializable]` : `TaskStep`, **nested** | Complete when the target player holds `targetRequiredItemName` and the initiator holds `myRequiredItemName`. |
| `DepositItemStep.GroupNavigateStep` | `Assets/Scripts/ConcreteTaskSteps.cs` | (~352–EOF) | `[Serializable]` : `TaskStep`, **nested** | Complete when the player is in `targetZoneID` and `≥ requiredPlayerCount` non-ghost players (from `RoleManager.allPlayers`) are also in it. |
| `ConsumeItemMinigame` | `Assets/Scripts/ConsumeItemMinigame.cs` | 69 | MonoBehaviour : `MinigameBase` | Button-driven "eat/drink the held item" minigame; on win destroys the whole held item or its child PickupItems and marks the container `isSpent`. |
| `DummyMinigame` | `Assets/Scripts/DummyMinigame.cs` | 10 | MonoBehaviour : `MinigameBase` | Placeholder: one UI button calls `CompleteMinigame()`. |
| `DummyTestHelper` | `Assets/Scripts/DummyTestHelper.cs` | 28 | MonoBehaviour (`[RequireComponent(PlayerController)]`) | Editor test aid: on `Start` instantiates a configured item prefab and force-equips it (dresses dummy players). |
| `FirstPersonHeadHider` | `Assets/Scripts/FirstPersonHeadHider.cs` | 30 | MonoBehaviour | Each `LateUpdate`, sets head + neck bone `localScale` to zero when `isLocalPlayer`, to one otherwise (so the FPS camera never sees its own head). |
| `Gallows` | `Assets/Scripts/Gallows.cs` | 6 | MonoBehaviour | Data holder: an `executionSpot` Transform for the condemned prisoner. |
| `IInteractable` | `Assets/Scripts/IInteractable.cs` | 9 | interface | `OnInteract(GameObject)` + `GetInteractionPrompt()` — contract for anything the interaction raycast resolves. |
| `ItemDepositMinigame` | `Assets/Scripts/ItemDepositMinigame.cs` | 15 | abstract MonoBehaviour : `MinigameBase` | Base for "carry a held item to a station and place it" minigames; adds abstract `BeginDeposit(PickupItem, TaskDepositStation)`. |
| `MainMenuManager` | `Assets/Scripts/MainMenuManager.cs` | 57 | MonoBehaviour | Main-menu scene: panel switching, Play (scene load), Quit. |
| `MatchManager` | `Assets/Scripts/MatchManager.cs` | 283 | MonoBehaviour, **singleton** | Match state machine (`Initialization → ActionStage → TransitionToMeeting → MeetingPhase → GameOver`), phase timers, win-condition checks, gallows-meeting trigger. Nested `enum MatchState`. |
| `MenuHeadTracker` | `Assets/Scripts/MenuHeadTracker.cs` | 46 | MonoBehaviour | Menu scene: each `LateUpdate`, rotates a head bone toward the mouse (slerped). |
| `MinigameBase` | `Assets/Scripts/MinigameBase.cs` | 92 | abstract MonoBehaviour | Root of all minigames: `SetupMinigame`(player,task) / `SetupMinigame`(context) / `CompleteMinigame` / `CancelMinigame`, `MinigameContext`, static active-minigame registry (`ActiveMinigames`/`IsAnyActive`/`Current`), `OnMinigameBegin/End` hooks. |
| `PlayerController` | `Assets/Scripts/PlayerController.cs` | 3272 | MonoBehaviour (`[RequireComponent(CharacterController, PlayerInput)]`) | The player god-object: FPS move/look/crouch/hip-lean, dual-hand inventory + two-handed haul, throw/drop charge, punch, Corrupted strangle + 8 power-ups, Royal arrest/custody/gallows/leash, health→ghost, task evaluation + waypoint refresh, minigame launch/finish/cancel + camera snapping, and **all** hand IK (minigame-mouse, hang-reach, haul, strangle, finger-grip). Top-level `enum PlayerRole`, `enum MinigameTargetType`. |
| `PlayerIKHelper` | `Assets/Scripts/PlayerIKHelper.cs` | 21 | MonoBehaviour (`[RequireComponent(Animator)]`) | Sits on the Animator GO so `OnAnimatorIK` fires; forwards to `PlayerController.ApplyMinigameIK / ApplyStrangleIK / ApplyHangReachIK / ApplyHaulIK`. |
| `PowerUpPickup` | `Assets/Scripts/PowerUpPickup.cs` | 19 | MonoBehaviour : `IInteractable` (`[RequireComponent(Collider)]`) | World marker carrying a `PowerUpData`; the actual pickup / role gate lives in `PlayerController`. `OnInteract` is intentionally empty. |
| `PowerUpType` (enum) | `Assets/Scripts/PowerUpType.cs` | 29 | `enum` | 8 Corrupted power-up kinds. |
| `PowerUpData` | `Assets/Scripts/PowerUpType.cs` | 29 | `ScriptableObject` (`[CreateAssetMenu]`) | Authoring asset for a power-up: name, description, icon prefab, `PowerUpType`. |
| `RoleManager` | `Assets/Scripts/RoleManager.cs` | 175 | MonoBehaviour, **singleton** | Holds `allPlayers` (Inspector-filled), assigns King / Corrupted (`corruptedPercentage`) / Court at match start (Fisher-Yates), tracks `currentKing`/`currentKingsguard`, runs the King's-curse timer in `Update`. `forceTestRole` override. |
| `RoundRoleSwitch` | `Assets/Scripts/RoundRoleSwitch.cs` | 160 | MonoBehaviour | One-way toggle of a parent object between a `depositStationObject` child and a `pickupItemObject` child based on match stage / test mode; re-parents deposited items onto the pickup and optionally prefixes its name `"Deposited"`. Nested `enum TestMode`. |
| `RoyalWeapon` | `Assets/Scripts/RoyalWeapon.cs` | 10 | MonoBehaviour : `PickupItem` | Adds `restrictedRole` + `blockDuration` to a pickup (King/Kingsguard weapon that can raise a block). |
| `SwordHangMinigame` | `Assets/Scripts/SwordHangMinigame.cs` | 411 | MonoBehaviour : `ItemDepositMinigame` | Physical "hang the sword on the weapon rack": parent to hand tip-down, mouse-aim + WASD shuffle + RMB look, release → guided drop (freeze X/Z + rotation) → contact ray → seat into nearest free notch `DropSlot`; miss leaves it loose to retry. |
| `TaskData` | `Assets/Scripts/TaskData.cs` | 152 | `ScriptableObject` (`[CreateAssetMenu]`) | A task authoring asset: `[SerializeReference] List<TaskStep> steps`, `allowedStages`, `prerequisiteTask` + `autoSpawnItemPrefab`/`autoSpawnLocationID`, and the step engine (`EvaluateCurrentStep` with minigame interception, `CompleteActiveStep`, `CheckForTaskRegression`). |
| `TaskDepositStation` | `Assets/Scripts/TaskDepositStation.cs` | 383 | MonoBehaviour : `IInteractable` (`[RequireComponent(BoxCollider, TaskLocation)]`) | Accepts name-matched items into drop slots (auto-grid or hand-placed `customDropSlots`); stage-gated free retrieval; deposits instantly or launches `depositMinigamePrefab` (an `ItemDepositMinigame`). Nested `enum RetrieveTestMode`. |
| `TaskLocation` | `Assets/Scripts/TaskLocation.cs` | 30 | MonoBehaviour | Tags an object with `locationID` + `acceptedItemName`; **static `AllLocations` registry** (OnEnable/OnDisable/OnDestroy). |
| `TaskManager` | `Assets/Scripts/TaskManager.cs` | 188 | MonoBehaviour, **singleton** | Per-stage random task assignment (filters by `isSabotage` + `allowedStages`), prerequisite auto-spawn into stations, global Court progress meter, `completedTasksHistory`. |
| `TaskStation` | `Assets/Scripts/TaskStation.cs` | 36 | MonoBehaviour : `IInteractable` (`[RequireComponent(TaskLocation)]`) | Fixed interact point that can carry its own `processMinigamePrefab`; logs the interaction (task eval done by `PlayerController`). |
| `TaskZone` | `Assets/Scripts/TaskZone.cs` | 58 | MonoBehaviour (`[RequireComponent(Collider)]`) | Trigger room volume; writes `player.currentZoneID` on enter, clears on exit. **static `AllZones` registry** (OnEnable/OnDisable only). |
| `ThrownProjectile` | `Assets/Scripts/ThrownProjectile.cs` | 62 | MonoBehaviour | Runtime-added to a thrown item by `PlayerController.ExecuteThrow`; on first player collision applies distance-decayed pushback then `Destroy(this)`. |
| `UIIconSpawner` | `Assets/Scripts/UIIconSpawner.cs` | 42 | MonoBehaviour | Instantiates/replaces a 3D prefab inside a UI slot, forced onto the `UI` layer and scaled up (Corrupted power-up icons). |
| `UIManager` | `Assets/Scripts/UIManager.cs` | 439 | MonoBehaviour, **singleton** | All HUD/menus: task-list text (`StringBuilder`), court meter slider, transition/meeting/voting/game-over panels, data-code popup + input, Corrupted inventory display, Esc → in-game settings (freezes the local player, tracks `controlsWereLockedBeforeMenu`). Holds `localPlayer` (found by name). |
| `VotingManager` | `Assets/Scripts/VotingManager.cs` | 112 | MonoBehaviour, **singleton** | Meeting vote collection (`Dictionary<PlayerController,string>` — Confirm/Deny/Skip), tally, execute-or-free `condemnedPlayer`, King's-curse evaluation. |
| `WaypointManager` | `Assets/Scripts/WaypointManager.cs` | 477 | MonoBehaviour, **singleton** | Builds on-screen task waypoint markers per active step type, plus a meeting marker and Spymaster VIP markers; each `Update` projects every target to screen space, distance-sorts, stacks overlaps, repositions `RectTransform`s. Nested `struct MarkerDrawData`. Has a stray `using static DepositItemStep;` (see §6.1). |

### 1b. Minigame architecture scaffolding — `Assets/Scripts/Minigames/**` (NOT wired to the game)

| Type | File | Lines | Kind | Responsibility (one line) |
|---|---|---:|---|---|
| `MinigameContext` | `Minigames/MinigameContext.cs` | 50 | plain class | Launch payload: `Player`, `Task`, `TargetType`, `Target`, + convenience casts `HeldItem`/`Station`/`PartnerPlayer`; `Resolve()` fills the casts. |
| `PanelMinigame` | `Minigames/Bases/PanelMinigame.cs` | 53 | abstract MonoBehaviour : `MinigameBase` | Pure-UI minigames; `RequireClicks(n, onDone)` / `RegisterClick()` / `Win()` / `Lose()`. |
| `HandMinigame` | `Minigames/Bases/HandMinigame.cs` | 174 | abstract MonoBehaviour : `MinigameBase` | Right-hand-driven minigames: freezes/restores player, settings-menu pause, RMB hold-to-look (+ tap cancel), WASD footwork leash, `MouseWorld()`, owns a `MinigameHandRig`; template hooks `OnHandBegin/OnMinigameUpdate/OnMinigameLateUpdate/OnMinigameFixedUpdate`. |
| `PartnerMinigame` | `Minigames/Bases/PartnerMinigame.cs` | 80 | abstract MonoBehaviour : `MinigameBase` | Player-to-player minigames: resolves a real partner via `PartnerResolver` or spawns a `DummyPartner`; `FacePartner()`, `MirrorOnPartner(trigger)`, `PartnerAccepted`. |
| `EmoteMinigame` | `Minigames/Bases/EmoteMinigame.cs` | 44 | abstract MonoBehaviour : `MinigameBase` | Completes when the player commits an emote of `requiredCategory` from `EmoteWheelController`. |
| `ToolOnTargetMinigame` | `Minigames/Bases/ToolOnTargetMinigame.cs` | 76 | abstract : `HandMinigame` | Hold a tool, work it over a target; `MinigameProgressTracker` drives completion; optional `SpawnAndCarryObjective`. Abstract `ConfigureProgress()` / `OnToolUpdate()`. |
| `DragObjectMinigame` | `Minigames/Bases/DragObjectMinigame.cs` | 83 | abstract : `HandMinigame` | Click-grab a world object, drag along a local axis (0→1) or toward level (roll → 0). Abstract `ApplyDrag()`. |
| `PourMinigame` | `Minigames/Bases/PourMinigame.cs` | 82 | abstract : `HandMinigame` | Phase machine: grab empty vessel → grab source → tilt + aim to fill an `Accumulate` meter. Abstract vessel-candidate + `IsAimedAtGlass()`. |
| `ConsumeMinigame` | `Minigames/Bases/ConsumeMinigame.cs` | 92 | abstract : `HandMinigame` | Bring the held item (or a child) to a mouth anchor N times, then destroy it; `simpleMode` = one click. |
| `ChargeReleaseMinigame` | `Minigames/Bases/ChargeReleaseMinigame.cs` | 83 | abstract : `HandMinigame` | Equip → nock (primary down) → draw (`MinigameInput.DrawPull()`) → release a projectile with charge-scaled speed. Abstract `EquipComplete()`. |
| `InstrumentMinigame` | `Minigames/Bases/InstrumentMinigame.cs` | 99 | abstract : `HandMinigame` | Activation gesture + A/S/D/F/G note input → `Count` tracker. Abstract `UpdateActivationGesture()`. |
| `WindInstrumentMinigame` | `Minigames/Bases/InstrumentMinigame.cs` | (same file) | abstract : `InstrumentMinigame` | Activation = hand within `mouthRadius` of the head bone. |
| `StringInstrumentMinigame` | `Minigames/Bases/InstrumentMinigame.cs` | (same file) | abstract : `InstrumentMinigame` | Activation always true; each note gated by `MinigameInput.MouseStrum()`. |
| `MinigameHandRig` | `Minigames/Capabilities/MinigameHandRig.cs` | 99 | plain class | Wraps `PlayerController` hang-reach IK (`Begin/End/ReachToward/AimFromMouse/SetHandRotation`), item attach/detach; `SetGrip()` is a documented TODO stub. |
| `GuidedDrop` | `Minigames/Capabilities/GuidedDrop.cs` | 118 | static class + `Settings` struct + `Handle` class | No-tumble / no-bounce falling rigidbody with optional per-FixedUpdate `Funnel(slotPos)`; `Begin()` returns a `Handle` that restores constraints / max-ang-vel / physics material on `End()`. |
| `StationContactProbe` | `Minigames/Capabilities/StationContactProbe.cs` | 48 | static class | `Resting(item, stationRoot, dist)` — short downward `Physics.Raycast` filtered to the station's collider hierarchy; overload returns the `RaycastHit`. |
| `MinigameInput` | `Minigames/Capabilities/MinigameInput.cs` | 75 | static class | One legacy-`Input` surface: `Suppressed` (settings menu), `Primary*/Secondary*`, `MouseDelta`, `MoveAxis`, plus `NoteKeysDown()` / `DrawPull()` / `MouseStrum()` / `MouseSwing()`. |
| `MinigameProgressTracker` | `Minigames/Capabilities/MinigameProgressTracker.cs` | 86 | `[Serializable]` plain class | Generic 0→1 progress in `Count` / `Accumulate` / `Zones` modes; `Progress01`, `IsComplete`, `event Completed`. |
| `SpawnAndCarryObjective` | `Minigames/Capabilities/SpawnAndCarryObjective.cs` | 59 | `[Serializable]` plain class | `Produce(player, source)` instantiates + names a produced `PickupItem`; `PushCarryHint()` is a TODO stub. |
| `EmoteCategory` (enum) | `Minigames/Emote/EmoteSystem.cs` | 39 | `enum` | `Speech, Dance, Conversation, Gesture, Taunt`. |
| `EmoteDefinition` | `Minigames/Emote/EmoteSystem.cs` | 39 | `ScriptableObject` (`[CreateAssetMenu]`) | One selectable emote: display name, icon, category, animator trigger, `loops`/`durationSeconds`/`partnered`. |
| `EmoteWheelController` | `Minigames/Emote/EmoteWheelController.cs` | 88 | MonoBehaviour, **singleton** | Radial emote picker service: `Open(player, category?, onCommit)` / `Commit(choice)` / `Cancel()`; plays the trigger on the performer. Wheel UI itself is a TODO. |
| `PartnerResolver` | `Minigames/Partner/PartnerResolver.cs` | 30 | static class | Returns the aimed-at real `PlayerController` (from `MinigameContext.PartnerPlayer`), else instantiates the dummy-partner prefab and returns its `PlayerController`. |
| `DummyPartner` | `Minigames/Partner/DummyPartner.cs` | 53 | MonoBehaviour | Solo-test stand-in: `Play(trigger)`, `HoldPose(param,on)`, `BeginAutoAccept(delay)` → flips `HasAccepted`, `Dismiss()`. |

### 1c. Out of scope but depended-on

| Type | File | Lines | Note |
|---|---|---:|---|
| `TaskStepDrawer` | `Assets/Scripts/Editor/TaskStepDrawer.cs` | 120 | `PropertyDrawer` for `[SerializeReference] List<TaskStep>`; reflects over **all** non-abstract `TaskStep` subclasses in the assembly to build the "add step" dropdown. Any change to the `TaskStep` type set is felt here. |

---

## 2. Where a "local player" (or an object identity) is resolved by name / Find / substring

### 2a. Local player = the GameObject literally named `"Player"`

| Site | Mechanism | Purpose |
|---|---|---|
| `RoleManager.cs:50` | `remainingPlayers.Find(p => p.gameObject.name == "Player")` | Picks the local player for the `forceTestRole` override and King assignment. |
| `TaskManager.cs:106` | `GameObject.Find("Player")` → `GetComponent<PlayerController>()` | After stage task assignment, refresh the local player's waypoints. |
| `UIManager.cs:72` (`Start`) | `GameObject.Find("Player")` → `localPlayer` | Cache the local player for HUD updates + vote casting. |
| `UIManager.cs:402` (`ToggleInGameSettings`) | `GameObject.Find("Player")` → `localPlayer` (lazy re-fetch) | Freeze/unfreeze the local player when the settings menu toggles. |
| `PlayerController.cs:2871` (`AssignTasks`) | `if (gameObject.name == "Player" && UIManager.Instance != null)` | Only the local player pushes its task list to the UI. |
| `PlayerController.cs:2887` (`RemoveCompletedTask`) | `if (gameObject.name == "Player" && UIManager.Instance != null)` | Same gate on task-completion UI refresh. |
| `PlayerController.cs:2899` (`RefreshLocalWaypoints`) | `if (gameObject.name == "Player")` | Only the local player drives `UIManager` + `WaypointManager`. |
| `FirstPersonHeadHider.cs:7` | `public bool isLocalPlayer = true` (Inspector flag, **not** name-based) | Head-hide only for the local player's own model; dummies set this false. |

**Interaction with `StolenHeraldry`** (`PlayerController.cs:1605–1632`): the power-up sets
`gameObject.name = stolenIdentity.gameObject.name` for its duration and restores it afterward. While
active, a disguised Corrupted player's GameObject is **not** named `"Player"`, so every
`name == "Player"` gate above silently no-ops for them until the disguise wears off.

### 2b. Object identity by GameObject-name substring (`name.Contains`) or child-name (`transform.Find`)

| Site | Expression | Matches against |
|---|---|---|
| `ConcreteTaskSteps.cs:69` (`StationInteractStep`) | `targetInteractable.name.Contains(targetStationID)` | station GameObject name |
| `ConcreteTaskSteps.cs:158` (`DataRetrievalStep`) | `targetInteractable.name.Contains(sourceStationID)` | station name |
| `ConcreteTaskSteps.cs:180` (`DataRetrievalStep`) | `targetInteractable.name.Contains(inputStationID)` | station name |
| `ConcreteTaskSteps.cs:289` (`ProcessItemStep`) | `targetInteractable.name.Contains(targetStationOrItemName)` | station/item name |
| `ConcreteTaskSteps.cs:308` (`EquipClothingStep`) | `targetInteractable.name.Contains(clothingName)` | clothing GameObject name |
| `ChestDepositMinigame.cs:169` (`FindChild`) | `t.name.ToLower().Contains(keyword)` for `"hinge"`, `"lid"`, `"grab"` | chest sub-part transforms |
| `PlayerController.cs:3049` (`StartMinigame`) | `targetInteractable.transform.Find("StandPoint")` | literal child name for camera/body snap point |

### 2c. Object identity by string ID field (not `name`, but the same "typed reference vs string" concern)

- **Item identity by `PickupItem.itemName`** — compared in `AcquireItemStep`, `DepositItemStep`,
  `ConsumeItemStep`, `PlayerInteractStep`, `MutualPlayerInteractStep`,
  `TaskDepositStation.OnInteract` / `FindMatchingDepositTask`,
  `PlayerController.IsHoldingItemNamed` / `TryProximityDeposit`, `PickupItem.OnInteract`
  (anti-spam), `WaypointManager.FindItemByName`, `TaskManager` auto-spawn.
  `PickupItem.ProcessItem()` (`"Processed"` prefix) and `MarkAsDepositedContainer()`
  (`"Deposited"` prefix) **mutate `itemName` at runtime** so one object matches different steps
  over its lifetime. `Instantiate` "(Clone)" suffixes are manually stripped in several places
  (`PickupItem.cs:135`, `TaskManager.cs:78`, `CakeCuttingMinigame.cs:75`).
- **Location identity by `TaskLocation.locationID`** — `DepositItemStep`,
  `TaskDepositStation.FindMatchingDepositTask`, `TaskManager` auto-spawn,
  `WaypointManager.FindLocationByID`, `PlayerController.TryProximityDeposit`.
- **Zone identity by `TaskZone.zoneID` / `player.currentZoneID`** string compare — `NavigateStep`,
  `GroupNavigateStep`, `MatchManager` (`!= meetingZoneID`), `WaypointManager.FindLocationByID`.
- **Team identity by display string** — `MatchManager.winningTeam` / `winReason` are raw strings
  (`"Court"`, `"Corrupted"`); `VotingManager.TallyVotes` compares vote strings `"Confirm"` /
  `"Deny"` / `"Skip"`.

---

## 3. `[SerializeReference]` fields and their occupant types

### The only `[SerializeReference]` field in gameplay code

`Assets/Scripts/TaskData.cs:28`

```csharp
[SerializeReference]
public List<TaskStep> steps = new List<TaskStep>();
```

**Types that can occupy it** — every non-abstract subclass of `TaskStep` in the assembly. All 11
currently live in `Assets/Scripts/ConcreteTaskSteps.cs`:

| Managed-reference type (as stored in `.asset` YAML) | Declared as |
|---|---|
| `AcquireItemStep` | top-level |
| `NavigateStep` | top-level |
| `StationInteractStep` | top-level |
| `PlayerInteractStep` | top-level |
| `DataRetrievalStep` | top-level |
| `DepositItemStep` | top-level |
| `ConsumeItemStep` | **nested in `DepositItemStep`** — serialized type name is the nested form |
| `ProcessItemStep` | **nested in `DepositItemStep`** |
| `EquipClothingStep` | **nested in `DepositItemStep`** |
| `MutualPlayerInteractStep` | **nested in `DepositItemStep`** |
| `GroupNavigateStep` | **nested in `DepositItemStep`** |

Notes for the refactor (observation, not a fix):
- `SerializeReference` stores each element's type as `{class, ns, asm}`. Renaming any of these
  classes, moving them to another file's namespace, un-nesting the five nested ones, or moving the
  whole project to a new assembly definition **orphans that step in every `TaskData` `.asset`** with
  no Inspector warning. `[MovedFrom]` (`UnityEngine.Scripting.APIUpdating`) is the mitigation.
- `Editor/TaskStepDrawer.cs` enumerates these by reflection, so a new subclass appears in the
  Inspector "add step" menu automatically — and a removed/renamed one disappears from it.
- The scaffolding introduces **no** new `[SerializeReference]` fields. `MinigameProgressTracker` and
  `SpawnAndCarryObjective` are plain `[Serializable]` classes held by value (safe to rename with
  `[FormerlySerializedAs]` on the *field*, not the type).

---

## 4. Static registries and singletons

### 4a. Singletons — `public static T Instance` set in `Awake`, `else Destroy(gameObject)` dup-guard, **no `Instance` clear on destroy**

| Type | Set at | Cleared at | Consumers (sample) |
|---|---|---|---|
| `MatchManager.Instance` | `MatchManager.cs:42` (`Awake`) | — never | `TaskManager`, `UIManager`, `TaskDepositStation`, `RoundRoleSwitch`, `PlayerController` |
| `TaskManager.Instance` | `TaskManager.cs:24` (`Awake`) | — never | `PlayerController` (task completion), `MatchManager`, `UIManager` |
| `RoleManager.Instance` | `RoleManager.cs:30` (`Awake`) | — never | `PlayerController` (power-ups, nominate), `TaskManager`, `MatchManager`, `WaypointManager`, `VotingManager`, `GroupNavigateStep` |
| `UIManager.Instance` | `UIManager.cs:66` (`Awake`) | — never | `PlayerController`, `TaskManager`, `MatchManager`, `VotingManager`, `TaskData` refs |
| `VotingManager.Instance` | `VotingManager.cs:15` (`Awake`) | — never | `PlayerController` (`LockPrisonerToGallows`), `MatchManager`, `UIManager` |
| `WaypointManager.Instance` | `WaypointManager.cs:45` (`Awake`) | — never | `PlayerController` (`RefreshLocalWaypoints`, Spymaster), `MatchManager` |
| `EmoteWheelController.Instance` | `Emote/EmoteWheelController.cs:31` (`Awake`), guarded `if (Instance != null && Instance != this) Destroy` | — never | `EmoteMinigame` (scaffolding — no scene instance yet) |

### 4b. Static collection registries — self-register in `OnEnable`, self-unregister in `OnDisable` (+ `OnDestroy`)

| Registry | Add | Remove | Consumers |
|---|---|---|---|
| `PickupItem.AllItems` (`List<PickupItem>`) | `PickupItem.cs:89` `OnEnable` | `PickupItem.cs:94` `OnDisable`, `PickupItem.cs:99` `OnDestroy` | `WaypointManager.FindItemByName` |
| `TaskLocation.AllLocations` (`List<TaskLocation>`) | `TaskLocation.cs:19` `OnEnable` | `TaskLocation.cs:24` `OnDisable`, `TaskLocation.cs:29` `OnDestroy` | `WaypointManager`, `TaskManager` (auto-spawn), `PlayerController.TryProximityDeposit`, `DepositItemStep.CheckCompletion` |
| `TaskZone.AllZones` (`List<TaskZone>`) | `TaskZone.cs:22` `OnEnable` | `TaskZone.cs:28` `OnDisable` — **no `OnDestroy` remove** (relies on `OnDisable` always preceding `OnDestroy` for an active object) | `WaypointManager.FindLocationByID` |
| `MinigameBase.active` (`HashSet<MinigameBase>`, private) | `MinigameBase.SetupMinigame` (both overloads) | `CompleteMinigame` / `CancelMinigame`; **also self-prunes `== null` entries on every read** via `Prune()` — deliberate, because `SwordHangMinigame.FinishFail` / `ChestDepositMinigame.FinishFail` call `Destroy(gameObject)` without a base call | Exposed via `ActiveMinigames` / `IsAnyActive` / `Current` — **no consumers yet** (scaffolding) |

### 4c. Registry-like but not `static`

- `RoleManager.allPlayers` (`public List<PlayerController>`) — **Inspector-populated, never
  `Add`ed from code**; only pruned (`RemoveAll(p => p == null)` in `AssignAllRoles`,
  `RoleManager.cs:38`). Iterated by `TaskManager`, `MatchManager`, `WaypointManager`,
  `PlayerController` (Spymaster / Stolen Heraldry / Blinding Ash), `GroupNavigateStep`.
- Pairwise runtime references cleared by hand (not registries, but cross-object state the refactor
  will touch): `PlayerController.currentPrisoner` / `currentCaptor` / `strangleVictim` /
  `targetPlayer` / `currentTarget` / `sceneGallows`; `VotingManager.condemnedPlayer`;
  `PickupItem.currentStation`.

---

## 5. `Update()` / `LateUpdate()` / `FixedUpdate()` inventory

Legend for the per-frame column:
**PF** = genuinely per-frame (moving camera / physics / input / animation);
**PF-scoped** = per-frame but only for the seconds a minigame/strangle is open, and early-outs otherwise;
**POLL** = re-checks state that changes rarely (kill, execution, stage change, task completion) — event-drivable.

### `Update()`

| Site | Per-frame? | What it does / notes |
|---|---|---|
| `PlayerController.cs:345` | **PF** | Ctrl-lean lerp (pre-gate), then IK-target updates, movement, `HandleRotation`, crouch lerp, **`CheckForInteractable()` = one `Physics.RaycastAll` + `RaycastAll` fallback every frame**, throw-charge timer, UI-alpha lerp, animator `Speed` param. Also `FindAnyObjectByType<Gallows>()` the first frame of a prisoner drag (one-time cache into `sceneGallows`). Reads legacy `Input.GetKey(Ctrl)`. |
| `MatchManager.cs:51` | **POLL** | `HandleStateTimers()` (timers only tick in Meeting/Transition) **+ `CheckWinConditions()` every frame in every non-init/non-gameover state**, which loops `RoleManager.allPlayers` counting alive-by-role. Win state only changes on a kill / execution / task completion. |
| `RoleManager.cs:143` | **PF** (cheap) | One `kingCurseTimer -= Time.deltaTime` + threshold check while a living, un-cursed King exists. Time-based; trivial. |
| `UIManager.cs:76` | **PF** (cheap) | Single `Input.GetKeyDown(KeyCode.Escape)` → `ToggleInGameSettings()`. Legacy input poll. |
| `WaypointManager.cs:227` | **PF** | Only runs when `activeWaypoints.Count > 0`: projects every marker + the meeting marker to screen space, **`new List<MarkerDrawData>()` + `drawList.Sort(lambda)` + `new Dictionary<Transform,int>()` every frame**, stacks overlaps, repositions `RectTransform`s. Must track a moving camera → PF, but allocates each frame. |
| `RoundRoleSwitch.cs:69` | **POLL** | Calls `Apply()` → reads `MatchManager.Instance.currentStage`, `DepositStationSatisfied()` (a `GetComponent<TaskDepositStation>()` + slot loop), toggles two `GameObject.SetActive`. The station→pickup switch is **one-way, at most once per match**. Pure poll of a stage/deposit flag. |
| `ChestDepositMinigame.cs:206` | **PF-scoped** | While the minigame is open: settings-pause check, `HandleLook()` (RMB), `HandleFootwork()` (WASD), phase switch. After release: `RaycastTouchingChest()` each frame until seated/miss. Reads legacy `Input.*`. |
| `SwordHangMinigame.cs:97` | **PF-scoped** | Same shape as Chest: RMB look, WASD, `UpdateAiming()`, release, settle timer, `RaycastTouchingRack()` per frame after release. Legacy `Input.*`. |
| `PartnerMinigame.cs:52` *(scaffold)* | **PF-scoped** | Guard, then `OnMinigameUpdate()`. Not wired. |
| `HandMinigame.cs:83` *(scaffold)* | **PF-scoped** | Settings-pause, `HandleLook()`, `HandleFootwork()`, `OnMinigameUpdate()`. Not wired. |
| `DummyPartner.cs:24` *(scaffold)* | **PF** (trivial) | One `Time.time >= acceptAt` check until `HasAccepted` flips; effectively a one-shot timer. Not wired. |

### `LateUpdate()`

| Site | Per-frame? | What it does / notes |
|---|---|---|
| `PlayerController.cs:1937` | **PF-scoped** | `ApplyLeanCameraArc()` + `ApplyLeanSpineBend()` + `ApplyHandGripPose()`. All three early-out when `leanBlend`/grip ≈ 0, so ~zero cost when not leaning and not in a minigame. Must run after the animator. `ApplyHandGripPose` reads legacy mouse every active frame and calls `CollectHandGripBones()` once (lazy). |
| `FirstPersonHeadHider.cs:16` | **PF (constant work)** | Assigns `headBone.localScale` + `neckBone.localScale` **every frame** (0 for local, 1 for others). The values never change frame-to-frame — it re-asserts a fixed scale each frame to override the animator. Runs on every character. |
| `MenuHeadTracker.cs:21` | **PF** | Menu scene only: mouse→head-bone rotation with a `Slerp` each frame; tracks a moving cursor. |
| `SwordHangMinigame.cs:250` | **PF-scoped** | While `!released`: forces the item's world rotation tip-down each frame (after IK poses the hand). Only during the aiming phase. |
| `HandMinigame.cs:101` *(scaffold)* | **PF-scoped** | Forwards to `OnMinigameLateUpdate()`. Not wired. |

### `FixedUpdate()`

| Site | Per-frame? | What it does / notes |
|---|---|---|
| `ChestDepositMinigame.cs:454` | **PF-scoped** | Only after item release and while `dropFunnelSpeed > 0`: steers the falling item's X/Z `linearVelocity` toward the target slot column. Early-outs every other physics step. Active for ~1 s per deposit. |
| `HandMinigame.cs:107` *(scaffold)* | **PF-scoped** | Forwards to `OnMinigameFixedUpdate()`. Not wired. |

### Per-frame-equivalent loops that are **not** named `Update*` (for completeness)

- `PlayerIKHelper.OnAnimatorIK(int)` — fires every animator IK pass; forwards to
  `PlayerController.ApplyMinigameIK / ApplyStrangleIK / ApplyHangReachIK / ApplyHaulIK`, each of
  which Lerps a weight toward 0 and early-returns near 0. **PF-scoped.**
- `PlayerController` coroutines with `while (...) { ... yield return null; }`: `StrangleRoutine`
  (phase-2 struggle loop → `UpdateStrangleLock()` each frame), `CustodyFollowRoutine`
  (`while (isArrested && currentCaptor != null)` — drags the prisoner every frame),
  `PushbackRoutine`, `BlindnessRoutine`/`StunRoutine`/`HandleInvisibility`/`DaggerStrikeRoutine`
  (single-`WaitForSeconds`, not per-frame).
- `WaypointManager.SpymasterRoutine` — repositions temporary markers every frame for `duration`
  seconds.
- `RoundRoleSwitch` also runs `Apply()` from `Start()` once, in addition to its `Update` poll.

---

## 6. Structural notes surfaced while taking the inventory (observation only)

### 6.1 `ConcreteTaskSteps.cs` — `DepositItemStep` is never closed before the next class

`DepositItemStep` opens at line ~206. Its `CheckCompletion` method closes at line ~241, but **no
`}` closes the class body there.** `ConsumeItemStep`, `ProcessItemStep`, `EquipClothingStep`,
`MutualPlayerInteractStep`, and `GroupNavigateStep` are therefore declared **inside** `DepositItemStep`
as public nested types. The file's final two `}` (EOF) close `GroupNavigateStep` and then
`DepositItemStep`. Brace count balances (68/68) and it compiles.

Consequences a refactor must account for:
- The real C# names are `DepositItemStep.ConsumeItemStep`, `DepositItemStep.ProcessItemStep`, etc.
- `WaypointManager.cs:4` has `using static DepositItemStep;` — that is what lets `WaypointManager`
  write `activeStep is ConsumeItemStep` / `is ProcessItemStep` / `is MutualPlayerInteractStep`
  unqualified. `ConcreteTaskSteps.cs` itself references them unqualified because it is inside the
  same class scope.
- The `[SerializeReference]` type strings baked into every `TaskData` `.asset` for those five step
  types are the nested form. Un-nesting them changes the fully-qualified name.

### 6.2 Legacy `UnityEngine.Input` used alongside the new Input System

`PlayerInput` (Send Messages) drives `PlayerController` via `On*` callbacks (`OnMove`, `OnLook`,
`OnInteract`, `OnJump`, `OnStrangle`, `OnDropItem`, `OnPunch`, `OnUseItem`, `OnUsePowerUp1..3`,
`OnScrollWheel`, `OnLean`, `OnSwapHands`, `OnNominate`, `OnPrevious`, `OnNext`, `OnSprint`,
`OnCrouch`, `OnThrowItem`). Legacy `Input.*` is read directly in:
`SwordHangMinigame`, `ChestDepositMinigame` (mouse buttons + `"Mouse X/Y"` axes + WASD keys),
`UIManager` (`KeyCode.Escape`), `PlayerController` (`KeyCode.LeftControl/RightControl` for lean,
mouse buttons for the finger-grip, `Input.mousePosition` for minigame IK),
`MenuHeadTracker` (`Input.mousePosition`), and the entire `Minigames/` scaffolding
(`MinigameInput`, `HandMinigame`, `PartnerMinigame`, `DummyPartner`).

### 6.3 `PlayerController.cs` is 3272 lines

It owns FPS locomotion, camera, crouch, hip-lean, dual-hand inventory, two-handed haul, throwing,
punch, the Corrupted strangle state machine, 8 power-up executions + coroutines, Royal
arrest/custody/leash/gallows, health→ghost, task evaluation + waypoint refresh, minigame
launch/finish/cancel + camera snapping, and five separate hand-IK systems (minigame-mouse,
hang-reach, haul, strangle, finger-grip). It is referenced by ~20 other files.

### 6.4 `MinigameBase.active` registry has no consumer

`ARCHITECTURE.md` (§3, §10 P2/P3) designates `MinigameBase.IsAnyActive` as the future single source
of truth for "player is in a minigame", replacing the current split between
`PlayerController.isPlayingMinigame` (set only by `StartMinigame`) and `hangReachActive` (set by the
deposit minigames, which bypass `StartMinigame` via `TaskDepositStation.LaunchDepositMinigame` →
`ItemDepositMinigame.BeginDeposit`). Today `PlayerController.ApplyHandGripPose` tests
`isPlayingMinigame || hangReachActive` to cover both paths. Nothing reads the new registry yet.

### 6.5 `.cs` / `.cs.meta` and line-ending notes

`ConcreteTaskSteps.cs` has no trailing newline (385 physical lines; `wc -l` reports 384). Several
files use CRLF. `Assets/Scripts/Minigames/**` files were newly created and have no `.meta` yet
(Unity generates them on next import).
