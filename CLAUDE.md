# Corrupted Court — working rules

## Architecture
- Assets/Scripts/Minigames/ARCHITECTURE.md is authoritative for anything minigame-related.
  Read it before touching Assets/Scripts/Minigames/ or any MinigameBase subclass.
- Gameplay code must never call into UI directly. UI subscribes to gameplay events.
- ScriptableObjects are authoring assets and must never hold per-player runtime state.
- Identity is a typed reference or an ID field, never a GameObject name or a substring match.

## Code
- Return COMPLETE files. Never emit a diff fragment, "// ... rest unchanged", or an ellipsis.
- Null-guard every element in a loop over a public List<T> that the Inspector can populate.
- Every static registry needs a matching unregister in OnDisable or OnDestroy.
- No new Debug.Log in Update/LateUpdate/FixedUpdate or any per-frame path.
- No allocation (new List, new string, LINQ, string interpolation) in a per-frame path.
- Use Physics.OverlapSphereNonAlloc with a cached buffer, never Physics.OverlapSphere.

## Unity hazards — check these every time
- Renaming a serialized field breaks Inspector data. Add [FormerlySerializedAs("oldName")].
- Changing the namespace, assembly, or class name of a [SerializeReference] type destroys
  authored data. Add [MovedFrom] before making the change.
- Deleting a serialized field discards Inspector-set values with no warning. Flag it to me
  before you do it.
- Keep .cs and .cs.meta files together. Move files with git mv, never delete-and-recreate,
  or every prefab and scene reference to that script breaks.
- I have to recompile in the Unity Editor to verify. You cannot. Always end by telling me
  exactly what to check in the Editor.

## Working style
- Plan before writing on any change touching more than two files.
- When you finish, list: files created, files modified, files deleted, and anything I now have
  to re-wire by hand in the Inspector.

---

# Project facts (from REFACTOR_INVENTORY.md — verify before relying on any of these)

## Engine / setup
- Unity 6 (6000.0.48f1), URP. Rigidbody API is the Unity 6 spelling: `linearVelocity`,
  `linearDamping`, `angularDamping`, `maxAngularVelocity`; `PhysicsMaterial` (not
  `PhysicMaterial`); `PhysicsMaterialCombine`.
- Input: the new Input System via `PlayerInput` set to **Send Messages** — `PlayerController`
  receives `OnMove`, `OnLook`, `OnInteract`, `OnJump`, `OnStrangle`, `OnDropItem`, `OnPunch`,
  `OnUseItem`, `OnUsePowerUp1..3`, `OnScrollWheel`, `OnLean`, `OnSwapHands`, `OnNominate`,
  `OnPrevious`, `OnNext`, `OnSprint`, `OnCrouch`, `OnThrowItem`.
  Legacy `UnityEngine.Input` is **also** used directly: mouse buttons + `"Mouse X/Y"` axes + WASD
  in the deposit minigames and all `Minigames/` scaffolding, `KeyCode.Escape` in `UIManager`,
  `KeyCode.LeftControl/RightControl` + mouse in `PlayerController` (lean, finger-grip),
  `Input.mousePosition` in `MenuHeadTracker`.
- No `.asmdef` anywhere — one default assembly (`Assembly-CSharp`). All gameplay types see each
  other. `Editor/` compiles into `Assembly-CSharp-Editor`.

## Folder layout
```
Assets/Scripts/
  *.cs                       all shipping gameplay (flat, one MonoBehaviour per file)
  Editor/TaskStepDrawer.cs   the only editor script; PropertyDrawer for [SerializeReference] steps
  Minigames/                 NEW minigame architecture — scaffolding only, nothing wired
    MinigameContext.cs
    Bases/                   PanelMinigame, HandMinigame, PartnerMinigame, EmoteMinigame,
                             ToolOnTargetMinigame, DragObjectMinigame, PourMinigame,
                             ConsumeMinigame, ChargeReleaseMinigame, InstrumentMinigame
    Capabilities/            MinigameHandRig, GuidedDrop, StationContactProbe, MinigameInput,
                             MinigameProgressTracker, SpawnAndCarryObjective
    Emote/                   EmoteSystem (enum + SO), EmoteWheelController
    Partner/                 PartnerResolver, DummyPartner
  Animators/  PowerUp/  TaskData/   asset folders — NO code
```
`MinigameBase.cs` stays at `Assets/Scripts/` (prefabs reference it by GUID); the rest of the
minigame code lives under `Minigames/`. `ARCHITECTURE.md` §10 has the phased migration plan (P0–P5);
we are at P0 — no shipping code is wired to the scaffolding.

## Naming conventions (observed — match them)
- PascalCase types + methods; camelCase fields with **no `_` prefix**.
- Inspector-exposed fields are inconsistent: some `[SerializeField] private`, many just `public`
  with `[Header]`/`[Tooltip]`. Match the surrounding file.
- One MonoBehaviour per file, filename == class name. Enums are top-level or nested next to their
  owner. Abstract minigame bases are named for the interaction verb (`HandMinigame`,
  `PanelMinigame`, `ToolOnTargetMinigame`).

## Identity is string-based today (the thing the refactor is meant to fix)
- **Local player = the GameObject literally named `"Player"`.** Gated on in `RoleManager`
  (`name == "Player"`), `TaskManager` / `UIManager` (`GameObject.Find("Player")`), and
  `PlayerController.AssignTasks` / `RemoveCompletedTask` / `RefreshLocalWaypoints`
  (`gameObject.name == "Player"`). Dummy players are other GameObjects with `PlayerController` +
  `DummyTestHelper` + `FirstPersonHeadHider.isLocalPlayer = false`.
- The `StolenHeraldry` power-up **renames `gameObject.name`** at runtime, which silently disables
  every `name == "Player"` gate for its duration.
- Task steps match stations/clothing by `targetInteractable.name.Contains(id)`
  (`StationInteractStep`, `DataRetrievalStep`, `ProcessItemStep`, `EquipClothingStep`).
- Items match by `PickupItem.itemName` string everywhere; `ProcessItem()` and
  `MarkAsDepositedContainer()` mutate `itemName` at runtime (`"Processed"` / `"Deposited"`
  prefixes) so one object satisfies different steps over its life.
- Zones/locations match by `zoneID` / `locationID` string.

## `[SerializeReference]`
- Only one field: `TaskData.steps` (`List<TaskStep>`). Occupant types = the 11 classes in
  `ConcreteTaskSteps.cs`.
- **`DepositItemStep` is missing its closing brace**, so `ConsumeItemStep`, `ProcessItemStep`,
  `EquipClothingStep`, `MutualPlayerInteractStep`, and `GroupNavigateStep` are **nested inside
  `DepositItemStep`** (it compiles; brace count balances). Their serialized type names — stored in
  every `TaskData` `.asset` — are the nested form. `WaypointManager.cs` depends on this via
  `using static DepositItemStep;`. Un-nesting them or renaming any step type needs `[MovedFrom]`.
- `Editor/TaskStepDrawer.cs` reflects over all `TaskStep` subclasses to build the "add step" menu.

## Singletons (Awake sets `Instance`, `else Destroy(gameObject)`, none clear `Instance`)
`MatchManager`, `TaskManager`, `RoleManager`, `UIManager`, `VotingManager`, `WaypointManager`
(+ scaffolding `EmoteWheelController`).

## Static list registries (OnEnable add / OnDisable + OnDestroy remove)
`PickupItem.AllItems`, `TaskLocation.AllLocations`, `TaskZone.AllZones` (TaskZone: OnDisable only —
no OnDestroy remove). `MinigameBase.active` (private HashSet, self-prunes null on read; exposed via
`ActiveMinigames`/`IsAnyActive`/`Current`; **no consumers yet**).
`RoleManager.allPlayers` is Inspector-populated, not self-registering — only pruned of nulls.

## Minigame launch — two paths (ARCHITECTURE.md wants these unified)
1. `PlayerController.StartMinigame(prefab, task, target)` — sets `isPlayingMinigame`, snaps
   camera/body, swaps item to left hand for "Item" target type. Used by task steps and
   `TaskStation` / held-item process minigames.
2. `TaskDepositStation.LaunchDepositMinigame(player, heldItem)` → `Instantiate` →
   `ItemDepositMinigame.SetupMinigame` + `BeginDeposit(heldItem, station)`. **Does NOT set
   `isPlayingMinigame`** — the minigame calls `player.SetControlsLocked(true)` and drives
   `player.hangReachActive` itself. `SwordHangMinigame` and `ChestDepositMinigame` use this path.
- `PlayerIKHelper` must sit on the Animator GameObject (child `CharacterVisuals`) so `OnAnimatorIK`
  fires; it forwards to four `PlayerController` IK methods.

## Biggest files / decomposition targets
`PlayerController.cs` (3272 lines, ~10 unrelated responsibilities, referenced by ~20 files),
`ChestDepositMinigame.cs` (588), `WaypointManager.cs` (477), `UIManager.cs` (439),
`SwordHangMinigame.cs` (411), `ConcreteTaskSteps.cs` (385), `TaskDepositStation.cs` (383).

## Per-frame hotspots (see REFACTOR_INVENTORY.md §5 for the full list + PF/POLL classification)
- `MatchManager.Update` runs `CheckWinConditions()` every frame (loops `allPlayers`) — POLL.
- `RoundRoleSwitch.Update` polls the match stage every frame for a once-per-match switch — POLL.
- `WaypointManager.Update` allocates a `List` + `Dictionary` + sorts every frame it has markers.
- `FirstPersonHeadHider.LateUpdate` re-assigns a constant bone scale every frame on every character.
- `PlayerController.Update` raycasts (`CheckForInteractable`) every frame.

## Repo / git
- `.gitignore` and `.gitattributes` exist; `Library/`, `Temp/`, `Logs/`, `*.csproj`, `*.sln` are
  ignored; `.meta` files are committed. Git LFS is configured for binary asset types.
- `ConcreteTaskSteps.cs` has no trailing newline (385 physical lines; `wc -l` says 384).
