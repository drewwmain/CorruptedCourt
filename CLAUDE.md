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

# Project facts — verify before relying on any of these

## Engine / setup
- Unity 6 (6000.0.48f1), URP. Rigidbody API is the Unity 6 spelling: `linearVelocity`,
  `linearDamping`, `angularDamping`, `maxAngularVelocity`; `PhysicsMaterial` (not
  `PhysicMaterial`); `PhysicsMaterialCombine`.
- Input: the new Input System via `PlayerInput` set to **Send Messages** — `PlayerController`
  receives `OnMove`, `OnLook`, `OnInteract`, `OnJump`, `OnStrangle`, `OnDropItem`, `OnPunch`,
  `OnUseItem`, `OnUsePowerUp1..3`, `OnScrollWheel`, `OnLean`, `OnSwapHands`, `OnNominate`,
  `OnPrevious`, `OnNext`, `OnSprint`, `OnCrouch`, `OnThrowItem`.
  Legacy `UnityEngine.Input` is **also** used directly: `KeyCode.Escape` in `UIManager`,
  `KeyCode.LeftControl/RightControl` + mouse in `PlayerController` (lean, finger-grip),
  `Input.mousePosition` in `MenuHeadTracker`, and everything in `Minigames/` — but the minigame
  code funnels it through `Minigames/Capabilities/MinigameInput.cs` (mouse buttons, `"Mouse X/Y"`,
  WASD, A/S/D/F/G) instead of poking `Input` directly.

## Assemblies (`.asmdef`)
Five assemblies — cross-assembly references are compiler-enforced now, not just convention:
```
CorruptedCourt.Core   ──┐          (no project deps)
CorruptedCourt.Items  ──┼─► CorruptedCourt.Gameplay ──► CorruptedCourt.UI
                                    ▲
             CorruptedCourt.EditorTools (Editor-only) ─┘  (refs Core, Items, Gameplay, UI)
```
- `CorruptedCourt.Gameplay.asmdef` sits at `Assets/Scripts/` root, so `Gameplay/`, `Tasks/`,
  `Minigames/`, `PowerUp/` all compile into it. `Core/`, `Items/`, `UI/`, `Editor/` each have
  their own.
- **UI references Gameplay; Gameplay cannot reference UI** — the "gameplay never calls UI" rule is
  now structural. Cross the gap with `Gameplay/GameEvents.cs` (one-way static event bus; UI
  subscribes). Note its domain-reload caveat in the file header.
- Types live in `CorruptedCourt.*` namespaces; `CorruptedCourt.Tasks` and `CorruptedCourt.Minigames`
  types are in the `CorruptedCourt.Gameplay` assembly.

## Folder layout
```
Assets/Scripts/
  Core/       Log, IInteractable, Faction, PlayerRole, CourtTitle    (CorruptedCourt.Core)
  Items/      ItemDefinition, ItemState                              (CorruptedCourt.Items)
  Gameplay/   shipping gameplay MonoBehaviours                       (CorruptedCourt.Gameplay)
    Player/   PlayerController decomposed into siblings: PlayerMotor, PlayerLook, PlayerInventory,
              PlayerIKRig, PlayerInteractor, PlayerTaskBook, PlayerVitals, PlayerStrangle,
              PlayerPowerUps, PlayerGhost
  Tasks/      TaskData, TaskInstance, TaskManager, TaskStep, TaskStepRuntime
    Steps/    one file per TaskStep subclass (Acquire / Consume / Deposit / Process /
              StationInteract / PlayerInteract / MutualPlayerInteract / Navigate / GroupNavigate /
              DataRetrieval / EquipClothing)
  Minigames/  ARCHITECTURE.md is authoritative. Bases/ Capabilities/ Emote/ Partner/ + at root:
              MinigameBase, MinigameContext, and the 6 shipping/legacy concretes
  UI/         UIManager, WaypointManager, MainMenuManager, MenuHeadTracker, UIIconSpawner  (CorruptedCourt.UI)
  Editor/     TaskStepDrawer + migration utilities (ItemDefinitionMigration,
              TaskStepDefinitionMigration, ReserializeTaskData)                 (CorruptedCourt.EditorTools)
  PowerUp/  Animators/   asset folders — NO code
```
`MinigameBase.cs` stays at `Assets/Scripts/Minigames/` (prefabs reference it by GUID);
`ItemDepositMinigame` moved into `Minigames/Bases/` at P2.

## Minigame refactor state (authoritative: `Minigames/ARCHITECTURE.md`; plan: `Minigames/IMPLEMENTATION_PROMPTS.md`)
- **P0–P3 done.** `SwordHangMinigame`, `ChestDepositMinigame`, `VaseDepositMinigame` ship on
  `ItemDepositMinigame : HandMinigame`, launched through `MinigameContext` + `SetupMinigame(context)`.
- Every other base (`HandMinigame`, `ToolOnTargetMinigame`, `DragObjectMinigame`, `PourMinigame`,
  `ConsumeMinigame`, `ChargeReleaseMinigame`, `InstrumentMinigame` +Wind/String, `PanelMinigame`,
  `PartnerMinigame`, `EmoteMinigame`) is **written and compiles but has never been instantiated** —
  the first concrete on each will shake bugs out of the base itself.
- Capabilities are real **except**: `MinigameHandRig.SetGrip()` is a no-op (`PlayerIKRig`
  auto-curls the right fist from LMB during any minigame); `SpawnAndCarryObjective.PushCarryHint()`
  is a no-op; `MinigameChain` (ARCHITECTURE §5) does not exist as a file yet.
- `PartnerResolver` + `DummyPartner` are real (solo testing works). `PartnerMinigame` does **not**
  extend `HandMinigame` — handshake / duel must compose `MinigameHandRig` themselves.
- **Blocked:** `EmoteWheelController.Open()` stops at `// TODO(P4): show the radial UI` — nothing
  calls `Commit()`, so Speech / Dance / Conversation would hang. Book-of-names needs an
  arrival-order data model + a new `SequenceRecallStep`.
- `PlayerController.isPlayingMinigame` is now `=> MinigameBase.IsAnyActive` (computed, not a field).

## Naming conventions (observed — match them)
- PascalCase types + methods; camelCase fields with **no `_` prefix**.
- Inspector-exposed fields are inconsistent: some `[SerializeField] private`, many just `public`
  with `[Header]`/`[Tooltip]`. Match the surrounding file.
- One MonoBehaviour per file, filename == class name. Enums are top-level or nested next to their
  owner. Abstract minigame bases are named for the interaction verb (`HandMinigame`,
  `PanelMinigame`, `ToolOnTargetMinigame`).

## Identity — typed now, migration ~complete
- **Local player = `PlayerController.isLocalPlayer`** (`[SerializeField] private bool`, exposed as
  `IsLocal`). The old `name == "Player"` / `GameObject.Find("Player")` gates are gone. The
  GameObject name is now only a stable lookup / display-name fallback; `StolenHeraldry` overwrites
  `PlayerController.displayName`, **never** the GameObject name.
- Items match by **`ItemDefinition`** typed reference + **`ItemState`** flags via
  `PickupItem.Matches(definition, state)`. Runtime name mutation is now flag ORing
  (`ItemState.Processed`, `.DepositedContainer`, `.Spent`), so one object still satisfies different
  steps over its life.
- Stations match by `TaskLocation.locationID` **exact** match via `TaskStep.ResolveLocation(go)`
  (component on the object or its nearest ancestor). **Lone string holdout:** `EquipClothingStep`
  still uses `targetInteractable.name.Contains(clothingName)`.
- Zones / locations keyed by `zoneID` / `locationID` strings — those are IDs (allowed by the rule).
- Deprecated `legacy*Name` string fields remain on the step classes with `[FormerlySerializedAs]`
  + `[DEPRECATED]` tooltips. Don't rely on them; don't delete one without checking the `.asset`s.

## `[SerializeReference]`
- Still one field: `TaskData.steps` (`List<TaskStep>`). The 11 step subclasses are now **un-nested**
  — one file each under `Tasks/Steps/`, every one `[System.Serializable] public class X : TaskStep`
  in `namespace CorruptedCourt.Tasks`. The old "`DepositItemStep` missing a closing brace so five
  types nest inside it" bug is fixed; `WaypointManager` no longer has `using static DepositItemStep;`.
- The `.asset` SerializeReference records store `{class: X, ns: CorruptedCourt.Tasks, asm:
  CorruptedCourt.Gameplay}`. Renaming / re-namespacing a step type ⇒ rewrite the `.asset` YAML by
  hand (see `Editor/TaskStepDefinitionMigration.cs`, `ReserializeTaskData.cs`); a plain
  `dotnet build` will not catch the break.
- `Editor/TaskStepDrawer.cs` still reflects over all `TaskStep` subclasses to build the "add step" menu.

## Singletons (Awake sets `Instance`, else `Destroy`; none observed to clear `Instance`)
`MatchManager`, `TaskManager`, `RoleManager`, `SabotageManager` (new — the Corrupted team's one
global sabotage), `UIManager`, `WaypointManager`, `VotingManager` (+ scaffolding `EmoteWheelController`).

## Static list registries
`PickupItem.AllItems` and `TaskLocation.AllLocations` (OnEnable add / OnDisable + OnDestroy remove);
`TaskZone.AllZones` (OnEnable / OnDisable only — no OnDestroy remove); `Brazier.AllBraziers` (new).
`MinigameBase.active` (private HashSet, self-prunes null; exposed via
`ActiveMinigames`/`IsAnyActive`/`Current` — `IsAnyActive` now backs `PlayerController.isPlayingMinigame`).
`RoleManager.allPlayers` is now **self-registering** (`Register`/`Unregister`), still null-pruned.

## Minigame launch — both paths unified on `MinigameContext` (ARCHITECTURE §3)
1. `PlayerController.StartMinigame(prefab, task, target)` — camera/body snapping, item→left-hand
   swap for `MinigameTargetType.Item`, then builds a `MinigameContext` and calls
   `SetupMinigame(context)`. Reached from `TaskInstance.EvaluateCurrentStep` when
   `activeStep.minigamePrefab != null`.
2. `TaskDepositStation.LaunchDepositMinigame(player, heldItem)` — builds a context with
   `TargetType = Station`, calls `SetupMinigame(context)` + `BeginDeposit(heldItem, station)`. This
   is where a deposit minigame is wired (`depositMinigamePrefab` on the station prefab), **not** on
   the `DepositItemStep`.
- `PlayerIKHelper` must sit on the Animator GameObject (child `CharacterVisuals`) so `OnAnimatorIK`
  fires; it forwards to `PlayerController` IK methods.

## Biggest files
`UIManager.cs` (823), `PlayerController.cs` (777 — already decomposed into `Gameplay/Player/`, was
~3272), `TaskDepositStation.cs` (566), `WaypointManager.cs` (542), `PickupItem.cs` (403).
`ChestDepositMinigame.cs` (278) and `SwordHangMinigame.cs` (242) shrank onto the shared base.

## Per-frame hotspots (see REFACTOR_INVENTORY.md §5 — some moved with the Player split)
- `MatchManager.Update` runs `CheckWinConditions()` every frame (loops `allPlayers`) — POLL.
- `RoundRoleSwitch.Update` polls the match stage every frame for a once-per-match switch — POLL.
- `WaypointManager.Update` allocates a `List` + `Dictionary` + sorts every frame it has markers.
- `FirstPersonHeadHider.LateUpdate` re-assigns a constant bone scale every frame on every character.
- The interact raycast runs every frame — now in `PlayerInteractor`, not `PlayerController.Update`.

## Repo / git
- `.gitignore` and `.gitattributes` exist; `Library/`, `Temp/`, `Logs/`, `*.csproj`, `*.sln` are
  ignored; `.meta` files are committed. Git LFS is configured for binary asset types.
- `REFACTOR_INVENTORY.md` (repo root) and `Minigames/GRIP_CONSTRAINT_INVENTORY.md` are older
  deep-dive snapshots — cross-check any specific line against the code before trusting it.
