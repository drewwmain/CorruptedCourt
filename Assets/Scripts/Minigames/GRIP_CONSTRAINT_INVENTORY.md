# Grip / Constraint Inventory

Read-only inventory of how the held item, the right-hand reach IK, and the per-frame
callbacks interact while a `HandMinigame` is open. Facts only — no recommendations.

Source files read for this pass:
`Minigames/ARCHITECTURE.md`, `Minigames/MinigameBase.cs`,
`Minigames/Bases/HandMinigame.cs`, `Minigames/Bases/ItemDepositMinigame.cs`,
`Minigames/SwordHangMinigame.cs`, `Minigames/Capabilities/MinigameHandRig.cs`,
`Minigames/Capabilities/GuidedDrop.cs`, `Minigames/Capabilities/StationContactProbe.cs`,
`Minigames/Capabilities/MinigameInput.cs`, `Gameplay/PickupItem.cs`,
`Gameplay/PlayerController.cs`, `Gameplay/PlayerIKHelper.cs`,
`Gameplay/Player/PlayerIKRig.cs`, plus `Gameplay/Player/PlayerInteractor.cs` and
`Gameplay/TaskDepositStation.cs` (for the seat-into-slot and interaction-raycast paths),
and `ProjectSettings/` (`MonoManager.asset`, `DynamicsManager.asset`, `TimeManager.asset`).

`SwordHangMinigame` is the only concrete `HandMinigame` subclass in the project today, so
every concrete example below is from it.

---

## 1. Frame order and IK latency

### 1.0 Fixed facts

- **No `ProjectSettings/MonoManager.asset` exists** (only the compiled
  `Library/MonoManager.asset`). No custom Script Execution Order is configured. Ordering
  between `PlayerController`, `HandMinigame`/`SwordHangMinigame`, `PlayerIKHelper`,
  and `FirstPersonHeadHider` within the same callback phase (`Update`, `LateUpdate`) is
  Unity-default, i.e. **unspecified**.
- `TimeManager.asset`: `Fixed Timestep: 0.02`.
- `PlayerController` declares `Update()` and `LateUpdate()`, no `FixedUpdate()`.
- `PlayerIKRig` declares **no** `Update` / `LateUpdate` / `FixedUpdate`. Its methods are
  pumped by `PlayerController` and `PlayerIKHelper`.
- `HandMinigame` declares **private** `Update()`, `LateUpdate()`, `FixedUpdate()`
  (`HandMinigame.cs:121`, `:142`, `:148`). Subclasses override
  `OnMinigameUpdate` / `OnMinigameLateUpdate` / `OnMinigameFixedUpdate`.
- `PlayerIKHelper` sits on the `CharacterVisuals` child GameObject next to the `Animator`,
  so **only `PlayerIKHelper.OnAnimatorIK` fires** — `PlayerIKRig` / `PlayerController` do
  not receive `OnAnimatorIK` (they are on the root). Animator update mode assumed `Normal`
  (the default), which places `OnAnimatorIK` after all `Update()` and before all
  `LateUpdate()`.
- During a `HandMinigame`, `PlayerController.controlsLocked == true`
  (`HandMinigame.OnMinigameBegin` → `player.SetControlsLocked(true)`,
  `HandMinigame.cs:78`).
- During a `HandMinigame`'s registered window, `PlayerController.isPlayingMinigame` is
  `MinigameBase.IsAnyActive == true` (`PlayerController.cs:132`).

### 1.1 Exact per-frame sequence while SwordHang is in its aiming phase (`!released`)

**Phase A — `FixedUpdate` (0..N times, every 0.02 s):**

1. `HandMinigame.FixedUpdate()` (`HandMinigame.cs:148`) → `OnMinigameFixedUpdate()`.
   `SwordHangMinigame` does not override it → no-op.
2. (No other player/minigame `FixedUpdate`.) PhysX simulation step — no effect in the
   aiming phase (item is kinematic; see §3).

**Phase B — `Update` (once, all MonoBehaviours, order unspecified):**

3. `PlayerController.Update()` (`PlayerController.cs:189`):
   - `motor.UpdateLeanBlend()` — runs (before the gate).
   - `if (!controlsLocked)` → **false**, so the whole block is **skipped**:
     - `ikRig.UpdateMinigameIKTarget()` — **NOT called** ⇒ `PlayerIKRig.ikTargetPosition`
       is never refreshed for the entire minigame (stale value, or whatever a prior
       `StartMinigame`-launched minigame left).
     - `ikRig.UpdateHaulIKTarget()` — **NOT called**.
     - `look.HandleRotation`, `motor.HandleMovement`, `motor.HandleCrouchTransition`,
       `interactor.CheckForInteractable`, `inventory.TickThrowCharge`,
       `animator.SetFloat("Speed", …)` — none called.
   - `look.ApplyLeanCameraArc()` — runs (end of `Update`, unconditional).
4. `HandMinigame.Update()` (`HandMinigame.cs:121`):
   - `if (MinigameInput.Suppressed)` (settings menu) → early return.
   - `HandleLook()` (`HandMinigame.cs:157`) — gated by `LookActive`
     (`SwordHangMinigame.cs:62` → `!released`). On an RMB drag it calls
     `player.MinigameLookYaw(…)` / `player.MinigameLookPitch(…)` →
     `PlayerLook.MinigameLookYaw/Pitch` (turns body / pitches camera **this frame**).
   - `HandleFootwork()` (`HandMinigame.cs:186`) — gated by `FootworkActive`
     (`!released`) and `walkRadius > 0`. On WASD it calls
     `player.MinigameWalk(step, walkAnchor, walkRadius)` → `PlayerMotor.WalkConstrained`.
   - `OnMinigameUpdate()` → **`SwordHangMinigame.OnMinigameUpdate()`**
     (`SwordHangMinigame.cs:96`), aiming branch → `UpdateAiming()`
     (`SwordHangMinigame.cs:177`):
     - `Hand.ReachToward(MouseWorld())` → **writes `player.hangReachPos`**
       (`MinigameHandRig.cs:52` → `player.hangReachPos = worldPos`;
       `PlayerController.cs:109` forwards to `ikRig.hangReachPos`). `MouseWorld()`
       projects the mouse `reachDistance` (1.2 m) in front of `cam`
       (`HandMinigame.cs:194`).
     - `Hand.SetHandRotation(Quaternion.identity, 0f)` → **writes
       `player.hangReachRot = identity` and `player.hangReachRotWeight = 0`**
       (`MinigameHandRig.cs:64-69`).
     - `if (MinigameInput.PrimaryDown) ReleaseSword()`.
   - Order of steps 3 and 4 relative to each other is unspecified; it does not matter for
     the write→consume path because `hangReach*` is consumed in Phase C.

**Phase C — Animator evaluation (Unity internal, after all `Update`, before all `LateUpdate`):**

5. The `Animator` evaluates the state machine / blend trees and writes this frame's muscle
   pose onto every avatar bone, including `Hand_R`.
6. `PlayerIKHelper.OnAnimatorIK(layerIndex)` fires **once per animator layer with "IK Pass"
   enabled** (`PlayerIKHelper.cs:26`). Its body, in this exact order:
   1. `ikRig.ApplyMinigameIK(layerIndex)` (`PlayerIKRig.cs:259`):
      - `isPlayingMinigame == true` ⇒ `targetWeight = 1`;
        `currentIKWeight = Mathf.Lerp(currentIKWeight, 1, Time.deltaTime * ikBlendSpeed)`
        (`ikBlendSpeed = 8`, `PlayerIKRig.cs:85`).
      - if `currentIKWeight > 0.01`:
        `animator.SetIKPositionWeight(RightHand, currentIKWeight)`,
        `animator.SetIKPosition(RightHand, ikTargetPosition)` — **`ikTargetPosition` is
        stale** (step 3). `animator.SetIKRotationWeight(RightHand, currentIKWeight)`,
        `animator.SetIKRotation(RightHand, PlayerCamera.rotation * Euler(rightHandRotationOffset))`.
      - LEFT hand: switches on `player.currentMinigameTargetType`. For the
        `TaskDepositStation.LaunchDepositMinigame` path this is `None` (never set; cleared
        to `None` in `FinishMinigame` / `CancelMinigame`, `PlayerController.cs:693`, `:758`)
        ⇒ falls to the `else` STATION branch ⇒ `player.ActiveMinigameStation` is `null`
        for deposit minigames ⇒ inner `if (station != null)` fails ⇒ **left hand untouched**.
   2. `strangle.ApplyStrangleIK(layerIndex)` — not strangling; its internal weight lerps
      toward 0 and releases the hand goals once decayed.
   3. `mainController.ApplyHangReachIK(layerIndex)` (`PlayerController.cs:441`):
      - `target = hangReachActive ? 1 : 0` → **1** (set by `MinigameHandRig.Begin()`,
        `MinigameHandRig.cs:35`).
      - `ikRig.hangReachWeight = Mathf.Lerp(ikRig.hangReachWeight, 1, Time.deltaTime * ikBlendSpeed)`
        (`ikBlendSpeed = 8`).
      - `if (ikRig.hangReachWeight <= 0.01f) return;` — early-out only for the first frame
        or two after activation.
      - else: `animator.SetIKPositionWeight(RightHand, hangReachWeight)`,
        `animator.SetIKPosition(RightHand, hangReachPos)` — **overwrites the RightHand
        position goal that `ApplyMinigameIK` set, with the `hangReachPos` written this same
        frame in step 4.**
      - `rotW = hangReachWeight * Mathf.Clamp01(hangReachRotWeight)`. In SwordHang
        `hangReachRotWeight == 0` ⇒ `rotW = 0` ⇒
        `animator.SetIKRotationWeight(RightHand, 0)`,
        `animator.SetIKRotation(RightHand, hangReachRot)` — **rotation goal weight is 0, so
        `hangReachRot` does not move the hand bone in SwordHang.**
   4. `ikRig.ApplyHaulIK(layerIndex)` (`PlayerIKRig.cs:368`):
      - `haulActive == false` ⇒ `haulIKWeight` lerps toward 0; once `<= 0.01` it zeroes
        the two elbow-hint weights and `return`s **without touching the hand goals** ⇒
        `ApplyHangReachIK`'s RightHand position goal is the last write of the phase.
7. The Animator's two-bone IK solver runs toward the RightHand goal at weight
   `hangReachWeight` and writes the resulting upper-arm / forearm / hand bone rotations
   into the final pose.

**Phase D — `LateUpdate` (once, all MonoBehaviours, order unspecified):**

8. `PlayerController.LateUpdate()` (`PlayerController.cs:277`):
   - `ApplyLeanSpineBend()` — **early-returns** (`hangReachActive` true,
     `PlayerController.cs:463`).
   - `ikRig.ApplyHandGripPose()` (`PlayerIKRig.cs:204`) — writes curled **finger/thumb
     local rotations** over the animator pose while `isPlayingMinigame && (LMB held ||
     pulse)`. Fingers only; does not move the hand goal.
9. `HandMinigame.LateUpdate()` (`HandMinigame.cs:142`) →
   `if (player == null) return; OnMinigameLateUpdate();` →
   **`SwordHangMinigame.OnMinigameLateUpdate()`** — writes the **item's world-space
   rotation** (see §2 for the verbatim quote). Item position is not written here.
10. `FirstPersonHeadHider.LateUpdate()` — unrelated (re-applies a head-bone scale).

**Phase E — render.**

### 1.2 Frames between writing `hangReachRot` (and `hangReachPos`) and it reaching the hand bone

**Definite count: 0 frames — same frame.**

`hangReachPos` / `hangReachRot` are written in Phase B (`HandMinigame.Update` → step 4).
They are consumed in Phase C of the **same** frame: `ApplyHangReachIK` (step 6.3) feeds
them into `animator.SetIKPosition` / `SetIKRotation`, and the Animator's IK solve (step 7)
poses the hand bone — all before Phase E render. Unity guarantees all `Update()` precede
`OnAnimatorIK` which precedes rendering within one frame, so there is no pipeline delay.

The only lag is a **weight ease-in, not a frame delay**: on activation,
`hangReachWeight` (and `currentIKWeight`) climbs by
`Mathf.Lerp(current, target, Time.deltaTime * 8)` per call. From ~0 to 99% takes about
`n ≈ ln(0.01) / ln(1 − 8·dt)` frames ≈ **32 frames (~0.53 s) at 60 fps**, halved roughly
per extra IK-pass layer (each enabled layer calls `ApplyHangReachIK` again and advances the
lerp). Once the weight is settled, a `hangReachPos` written on frame N is fully applied on
frame N.

**SwordHang rotation caveat:** because `hangReachRotWeight` is pinned to 0 (set by
`MinigameHandRig.Begin()` and again every frame by `Hand.SetHandRotation(_, 0f)` in
`UpdateAiming`), the hand-bone **rotation** goal weight is `hangReachWeight * 0 = 0`.
`hangReachRot` is written every frame but **never appears on the hand bone** in
`SwordHangMinigame`. The tip-down orientation instead lands on the *item* transform one
phase later, in `OnMinigameLateUpdate` (Phase D, step 9). For a `HandMinigame` subclass
that sets `hangReachRotWeight > 0`, the rotation reaches the hand bone the same frame it is
written (0 frames), subject to the same one-time weight ease-in.

---

## 2. Every write to the held item's transform while a HandMinigame is open

`SwordHangMinigame` (the only concrete `HandMinigame`). "Callback" = the Unity message the
write ultimately runs under.

| # | File · method | Callback | Space | What it writes |
|---|---|---|---|---|
| 1 | `SwordHangMinigame.BeginHang` (`SwordHangMinigame.cs:87-90`) | setup — called from `BeginDeposit` right after `SetupMinigame` (not per-frame) | reparent + **local** | `item.transform.SetParent(handBone, false)`; `item.transform.localPosition = carryLocalPos`; `item.transform.localRotation = Quaternion.Euler(carryLocalEuler)` |
| 2 | `SwordHangMinigame.RestartAiming` (`SwordHangMinigame.cs:166`) → calls `BeginHang` again | `Update` (`HandMinigame.Update` → `OnMinigameUpdate`), when the player re-picks-up the sword mid-minigame | reparent + **local** | same three writes as #1 |
| 3 | `SwordHangMinigame.OnMinigameLateUpdate` (`SwordHangMinigame.cs:190-195`) | `LateUpdate` (`HandMinigame.LateUpdate` → `OnMinigameLateUpdate`) | **world** | `item.transform.rotation = …` (verbatim below). While `!released` only. |
| 4 | `PickupItem.DropInPlace` (`PickupItem.cs:245`) via `SwordHangMinigame.ReleaseSword` (`SwordHangMinigame.cs:208`) | `Update` (`OnMinigameUpdate`, on `MinigameInput.PrimaryDown`) | reparent (**world**) | `transform.SetParent(null)` — release phase, not aiming |
| 5 | `PickupItem.AttachToHand` (`PickupItem.cs:192-196`) via `SwordHangMinigame.AbortToHand` (`SwordHangMinigame.cs:262`) | `CancelMinigame` (RMB tap-cancel from `HandMinigame.HandleLook`, or `PlayerController` Esc path) | reparent + **local** | `transform.SetParent(handSocket)`; `transform.localPosition = heldPositionOffset`; `transform.localRotation = Quaternion.Euler(heldRotationOffset)` |
| 6 | `PickupItem.PlaceInStation` (`PickupItem.cs:290-302`) via `TaskDepositStation.DepositIntoSlot` (`TaskDepositStation.cs:307`) ← `SwordHangMinigame.TryHangOnSlot` (`SwordHangMinigame.cs:156`) | `Update` (`OnMinigameUpdate`, on contact/settle success) | reparent + **local** | `transform.SetParent(slot, false)`; `transform.localPosition = station.depositLocalPosition`; `transform.localRotation = Quaternion.Euler(station.depositLocalEuler)`; `transform.localScale = …` |

Not an explicit `transform` write, but relevant: between #1 and release the item is parented
to `handBone` and kinematic with its collider disabled (§3), so its **world position/rotation
follow the hand bone** every frame as the IK solve poses the arm — with #3 overriding the
rotation each `LateUpdate`.

`SwordHangMinigame.OnMinigameLateUpdate` **verbatim** (`SwordHangMinigame.cs:188-195`):

```csharp
        // Runs after the animator/IK have posed the hand: force the sword's orientation so the tip
        // always points straight down, only yawing with the player so it looks natural as they turn.
        protected override void OnMinigameLateUpdate()
        {
            if (!released && item != null && player != null)
                item.transform.rotation = Quaternion.Euler(0f, player.transform.eulerAngles.y, 0f)
                                          * Quaternion.Euler(carryLocalEuler);
        }
```

---

## 3. Held item Collider / Rigidbody state during the aiming phase

### 3.1 SwordHang's own attach path (`BeginHang` → `SetItemPhysics(false)`)

`SwordHangMinigame` **parents the item itself** — it does **not** call
`MinigameHandRig.AttachItem`. `BeginHang` (`SwordHangMinigame.cs:87-93`):

```csharp
            // Glue the sword to the hand BONE, tip-down.
            item.transform.SetParent(handBone, false);
            item.transform.localPosition = carryLocalPos;
            item.transform.localRotation = Quaternion.Euler(carryLocalEuler);
            SetItemPhysics(false);

            Hand.Begin();
```

`SetItemPhysics` (`SwordHangMinigame.cs:245-256`):

```csharp
        private void SetItemPhysics(bool loose)
        {
            Rigidbody rb = item.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = !loose;
                rb.useGravity = loose;
                if (loose) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            }
            Collider col = item.GetComponent<Collider>();
            if (col != null) col.enabled = loose;
        }
```

With `loose == false` (the aiming phase), the resulting state is:

- **Rigidbody:** `isKinematic = true`, `useGravity = false`. Linear/angular velocity are
  **not** zeroed here (the `if (loose)` branch is skipped).
- **Collider:** `enabled = false`. `isTrigger` is not touched by this path.
- `handBone` is `Hand.HandBone` = `player.RightHandBone` (`PlayerIKRig.rightHandBone`, or
  the humanoid `RightHand` bone) falling back to `player.RightHandSocket`
  (`MinigameHandRig.cs:28`).

The item that enters the minigame was already the player's held item, so immediately before
`BeginHang` it is in the state `PickupItem.AttachToHand` left it in (below) — re-parented
from `RightHandSocket` to `handBone`, with `useGravity` now explicitly cleared.

### 3.2 `PickupItem.AttachToHand` (`PickupItem.cs:186-196`)

Not called by SwordHang during aiming (it is the normal pickup path, and the
`AbortToHand` cancel path at `SwordHangMinigame.cs:262`). Lines that set the state:

```csharp
            // 1. Disable physics so it doesn't fall or push the player
            rb.isKinematic = true;
            coll.enabled = false;
            coll.isTrigger = false; // <-- NEW: Reset trigger state just in case

            // 2. Parent it to the socket (the physical hand bone)
            transform.SetParent(handSocket);

            // 3. Snap it to the center of the hand with your predefined offsets
            transform.localPosition = heldPositionOffset;
            transform.localRotation = Quaternion.Euler(heldRotationOffset);
```

State: `rb.isKinematic = true`, `coll.enabled = false`, `coll.isTrigger = false`. (Does not
touch `rb.useGravity`.)

### 3.3 `MinigameHandRig.AttachItem` (`MinigameHandRig.cs:82-95`) — for reference, NOT used by SwordHang

```csharp
            item.transform.SetParent(hand, false);
            item.transform.localPosition = localPos;
            item.transform.localRotation = Quaternion.Euler(localEuler);

            Rigidbody rb = item.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;
            Collider col = item.GetComponent<Collider>();
            if (col != null) col.enabled = false;
```

Same net collider/rigidbody state as SwordHang's path (`isKinematic = true`,
`col.enabled = false`); does not touch `useGravity` or `isTrigger`.

### 3.4 On release (leaves the aiming phase) — for context

`SwordHangMinigame.ReleaseSword` (`SwordHangMinigame.cs:197`) calls
`player.ClearHeldItem()` then `item.DropInPlace()` (`PickupItem.cs:239-255`:
`isKinematic = false`, `useGravity = true`, velocities zeroed, `coll.enabled = true`,
`coll.isTrigger = false`, `SettlePhysics` coroutine started), then
`GuidedDrop.Begin(rb, col, …)` (`GuidedDrop.cs:97`) overrides `rb.constraints`
(`FreezePositionX | FreezePositionZ | FreezeRotation` because
`freezeHorizontalPosition = true`), `rb.maxAngularVelocity = 2.5`,
`rb.maxDepenetrationVelocity = 0.5`, swaps `col.sharedMaterial` for a runtime no-bounce
`PhysicsMaterial "SwordDrop"`, and zeroes velocities. `GuidedDrop.Handle.End()` restores
all of it.

---

## 4. The full `hangReach*` surface

### 4.1 Fields on `PlayerIKRig` (`PlayerIKRig.cs:63-69`)

| Field | Declaration | Init | Read/written by |
|---|---|---|---|
| `hangReachActive` | `[HideInInspector] public bool` | `false` | written: `MinigameHandRig.Begin/End`. read: `PlayerController.ApplyHangReachIK` (target weight), `PlayerController.ApplyLeanSpineBend` (skip-bend guard), `PlayerLook` (reads it — noted in `PlayerIKRig.cs:60-62`). |
| `hangReachPos` | `[HideInInspector] public Vector3` | `(0,0,0)` | written: `MinigameHandRig.ReachToward`, `MinigameHandRig.AimFromMouse`. read: `PlayerController.ApplyHangReachIK`. |
| `hangReachRot` | `[HideInInspector] public Quaternion` | `Quaternion.identity` | written: `MinigameHandRig.SetHandRotation`. read: `PlayerController.ApplyHangReachIK`. |
| `hangReachRotWeight` | `[HideInInspector] public float` | `0` | written: `MinigameHandRig.SetHandRotation` (`Clamp01`), zeroed by `MinigameHandRig.Begin/End`. read: `PlayerController.ApplyHangReachIK` (`Clamp01` again). `0` = keep held pose, `1` = fully align to `hangReachRot`. |
| `hangReachWeight` | `public float` (no `[HideInInspector]`) | `0` | **written and read only by `PlayerController.ApplyHangReachIK`**, via `ikRig.hangReachWeight` directly. The per-frame blend weight for the whole hang-reach IK. |

### 4.2 Forwarding properties on `PlayerController` (`PlayerController.cs:108-113`)

```csharp
public bool hangReachActive       { get => ikRig.hangReachActive;       set => ikRig.hangReachActive = value; }
public Vector3 hangReachPos        { get => ikRig.hangReachPos;          set => ikRig.hangReachPos = value; }
public Quaternion hangReachRot     { get => ikRig.hangReachRot;          set => ikRig.hangReachRot = value; }
public float hangReachRotWeight    { get => ikRig.hangReachRotWeight;    set => ikRig.hangReachRotWeight = value; }
```

- **Forwarded:** `hangReachActive`, `hangReachPos`, `hangReachRot`, `hangReachRotWeight`.
- **Not forwarded:** `hangReachWeight` (comment at `PlayerController.cs:104-107` and
  `PlayerIKRig.cs:67-69`: only `ApplyHangReachIK` touches it, via `ikRig` directly).
- Related, not a `hangReach*` member: `PlayerController.ikBlendSpeed`
  (`PlayerController.cs:113`, read-only `=> ikRig.ikBlendSpeed`, value `8`) — the lerp rate
  used by `ApplyHangReachIK`, `ApplyMinigameIK`, `ApplyHaulIK`.
- `PlayerController.ApplyHangReachIK` (`PlayerController.cs:441`) is the sole consumer:
  lerps `ikRig.hangReachWeight` → `hangReachActive ? 1 : 0`, then
  `SetIKPositionWeight/Position(RightHand, …)` from `hangReachWeight` / `hangReachPos`, and
  `SetIKRotationWeight/Rotation(RightHand, …)` from `hangReachWeight * Clamp01(hangReachRotWeight)`
  / `hangReachRot`.

### 4.3 What `MinigameHandRig` already wraps (`MinigameHandRig.cs`)

| Rig member | Wraps | Effect |
|---|---|---|
| `Begin()` (`:32`) | `hangReachActive`, `hangReachRotWeight` | `player.hangReachActive = true; player.hangReachRotWeight = 0f;` |
| `End()` (`:40`) | `hangReachActive`, `hangReachRotWeight` | `player.hangReachActive = false; player.hangReachRotWeight = 0f;` |
| `ReachToward(Vector3)` (`:48`) | `hangReachPos` | `player.hangReachPos = worldPos;` |
| `AimFromMouse(float)` (`:55`) | `hangReachPos` | `player.hangReachPos = cam.ScreenToWorldPoint(mp)` with `mp.z = distance` |
| `SetHandRotation(Quaternion, float)` (`:64`) | `hangReachRot`, `hangReachRotWeight` | `player.hangReachRot = worldRot; player.hangReachRotWeight = Mathf.Clamp01(weight);` |
| `SetGrip(float)` (`:76`) | — | **no-op** (`TODO(P2)`; comment says the grip is currently auto-driven from LMB by `PlayerIKRig.ApplyHandGripPose`) |

- **Wrapped:** `hangReachActive` (Begin/End only — no standalone setter),
  `hangReachPos` (ReachToward / AimFromMouse), `hangReachRot` + `hangReachRotWeight`
  (SetHandRotation; also zeroed by Begin/End).
- **Not wrapped:** `hangReachWeight` (the rig has no access to it).
- `AttachItem` / `DetachItem` on the rig touch the item transform + its Rigidbody/Collider
  (§3.3), not any `hangReach*` member.

---

## 5. HandMinigame accessors for the rig and the hand bone

- **MinigameHandRig accessor:** `protected MinigameHandRig Hand { get; private set; }`
  (`HandMinigame.cs:49`). Name: **`Hand`**. Protected getter, private setter; assigned in
  `HandMinigame.OnMinigameBegin` as `new MinigameHandRig(player, cam)`
  (`HandMinigame.cs:75`).
- **Hand bone:** `HandMinigame` does **not** expose the hand bone directly. It is reachable
  only transitively as `Hand.HandBone` — a public property on `MinigameHandRig`
  (`MinigameHandRig.cs:28`) returning `player.RightHandBone` (else `player.RightHandSocket`).
  `SwordHangMinigame.BeginHang` reads it as `Hand != null ? Hand.HandBone : null`
  (`SwordHangMinigame.cs:71`).
- Also protected on `HandMinigame`: `protected Camera cam` (`HandMinigame.cs:48`).

---

## 6. Per-frame Physics queries that run while a HandMinigame is open

Base `HandMinigame` itself issues **no** `Physics` query. `PlayerIKRig` /
`PlayerIKHelper` / `PlayerController` IK code issue none.

### 6.1 SwordHang aiming phase (`!released`, in the registry, `controlsLocked == true`)

**No `Physics.*` query runs.** `StationContactProbe.Resting` is only called in the
`released` branch. `PlayerInteractor.CheckForInteractable` is gated off (it is inside
`PlayerController.Update`'s `if (!controlsLocked)` → `if (!isPlayingMinigame …)` block, both
false here).

### 6.2 SwordHang released / settling phase

`SwordHangMinigame.ReleaseSword` calls `HandMinigame.RestorePlayer()`, which calls
`LeaveActiveRegistry()` (`HandMinigame.cs:105`) and `player.SetControlsLocked(false)`. So
`MinigameBase.IsAnyActive` → **false**, `controlsLocked` → **false**, while the minigame
object is still alive and `OnMinigameUpdate` still runs. In that window:

| Query | Site | Layer mask | QueryTriggerInteraction | Notes |
|---|---|---|---|---|
| `Physics.Raycast(item.position + Vector3.up*0.03f, Vector3.down, out hit, distance, ~0, QueryTriggerInteraction.Ignore)` | `StationContactProbe.Resting` (`StationContactProbe.cs:24` and overload `:37`), called from `SwordHangMinigame.OnMinigameUpdate` (`SwordHangMinigame.cs:126`) | `~0` (all layers) | **`Ignore`** | `distance` = default `0.12f`. Runs each frame while `released && !resolving && !awaitingRetry && !touchedRack`. |
| `Physics.RaycastAll(ray, InteractionRange, interactableLayer | characterLayer)` | `PlayerInteractor.CheckForInteractable` (`PlayerInteractor.cs:168`) | `interactableLayer | characterLayer` — both are `[SerializeField] private LayerMask` on `PlayerInteractor` (Inspector-set; not in source). `DynamicsManager.asset` has `m_QueriesHitTriggers: 1`. | default (`UseGlobal` → hits triggers) | Runs again every frame because `isPlayingMinigame` is now false (and player not a ghost). |
| `Physics.Raycast(ray, out plainHit, InteractionRange, Physics.DefaultRaycastLayers)` | `PlayerInteractor.CheckForInteractable` fallback (`PlayerInteractor.cs:214`) | `Physics.DefaultRaycastLayers` (`~Ignore Raycast`) | default (`UseGlobal`) | Only when the masked `RaycastAll` resolved nothing interactable. |
| `Physics.OverlapSphereNonAlloc(transform.position, InteractionRange, royalOverlapBuffer, characterLayer)` | `PlayerInteractor.CheckForInteractable`, prisoner-drag branch (`PlayerInteractor.cs:116`) | `characterLayer` | n/a (overlap) | Only if `player.Vitals.isDraggingPrisoner` — replaces the raycasts above that frame. |

The `PickupItem.SettlePhysics` coroutine polls `rb.linearVelocity.sqrMagnitude` on
`WaitForSeconds` intervals — a property read, not a `Physics` query.

### 6.3 A HandMinigame that never calls `LeaveActiveRegistry()` mid-lifecycle

`CheckForInteractable` never runs (still gated by `isPlayingMinigame`), and the only
per-frame `Physics` queries are whatever that concrete subclass issues itself. Base
`HandMinigame` issues none.

---

## 7. `.asmdef` files under `Assets/Scripts`

Not "none". Five assembly definitions exist (no `.asmref`):

| Path | Assembly |
|---|---|
| `Assets/Scripts/Core/CorruptedCourt.Core.asmdef` | `CorruptedCourt.Core` |
| `Assets/Scripts/CorruptedCourt.Gameplay.asmdef` | `CorruptedCourt.Gameplay` |
| `Assets/Scripts/Items/CorruptedCourt.Items.asmdef` | `CorruptedCourt.Items` |
| `Assets/Scripts/UI/CorruptedCourt.UI.asmdef` | `CorruptedCourt.UI` |
| `Assets/Scripts/Editor/CorruptedCourt.EditorTools.asmdef` | `CorruptedCourt.EditorTools` |

There is no dedicated `Minigames` or `Tasks` assembly — per the session notes those types
compile into `CorruptedCourt.Gameplay` (the `Assets/Scripts/CorruptedCourt.Gameplay.asmdef`
at the `Scripts` root covers `Minigames/`, `Tasks/`, `Gameplay/`).
