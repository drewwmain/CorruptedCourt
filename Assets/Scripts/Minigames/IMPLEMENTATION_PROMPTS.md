# Minigame implementation prompts

A ready-to-paste prompt per minigame (or tight group), ordered so shared groundwork and
first-instantiation shakeout of each base class happen once. Companion to `ARCHITECTURE.md`
§8 / §10 (P5). Verified against the code on 2026-09-08.

## How to use this

- **One prompt per session.** Paste the fenced block verbatim. `CLAUDE.md` + `ARCHITECTURE.md`
  are always in context, so the prompts stay terse and don't re-explain the architecture.
- **Respect the order for the dependency arrows.** Anything marked *Depends on: Prompt 0* will
  fight the auto-grip without it. Everything else inside a tier is independent — run them in
  parallel sessions if you want.
- Every prompt ends by asking for the Editor checklist (`CLAUDE.md` working-style rule), and for
  a plan first since each touches >2 files.

## Status legend

| Badge | Meaning |
|---|---|
| ✅ | Base class ships today (deposits). Lowest risk. |
| ⚠️ | Base class is written and compiles but has **never been instantiated** — the first concrete on it will surface bugs *in the base*. Budget for that. |
| 🔴 | Blocked. Needs a system or a data decision that does not exist yet. See the final section. |
| 🎨 | Logic is buildable now; the *visual* it implies (shader / VFX / stroke render / anim clip) is separate art you still owe. |

## Request → prompt map

| Your request | Prompt | Status |
|---|---|---|
| 1 Cut cake → piece → plate | 2 | ⚠️ |
| 2 Coin bag → gift box | 1 | ✅ |
| 3 Cut turkey leg → plate | 2 | ⚠️ |
| 4 Pour wine from a pitcher | 7 | ⚠️ |
| 5 Book of names, memorise, order of arrival | **B5 + B6** | 🔴 data decision |
| 6 Handshake, RMB-aim, + solo test | 12 | ⚠️ 🎨 |
| 7 Polish the dirty sword | 4 | ⚠️ 🎨 |
| 8 Unfurl the rolled banner | 8 | ⚠️ |
| 9 Light the candle with flint & steel | 3 | ⚠️ 🎨 |
| 10 Straighten the crooked picture | 9 | ⚠️ |
| 11 Vase → table | 1 | ✅ |
| 12 / 14 Eat the cake piece off a plate | 6 | ⚠️ |
| 13 Gift box → table | 1 | ✅ |
| 15 Instruments: flute / trumpet / guitar | 11 | ⚠️ 🎨 |
| 16 Cheers → auto-start Drink wine | 13 | ⚠️ (needs `MinigameChain`, Prompt 0) |
| 17 Write your name on the contract | 5 | ⚠️ 🎨 |
| 18 Speech at the podium (emote) | **B1 + B2** | 🔴 wheel UI |
| 19 Dance with another player (emote) | **B1 + B3** | 🔴 wheel UI |
| 20 Arrow → quiver | 1 | ✅ |
| 21 Firework → marked spot | 1 | ✅ |
| 22 Light the firework with flint & steel | 3 | ⚠️ 🎨 |
| 23 Conversation via emotes | **B1 + B4** | 🔴 wheel UI |
| 24 Sword duel, mouse-swing, 3 hits | 14 | ⚠️ 🎨 |
| 25 Bow & arrow: nock, draw, loose | 10 | ⚠️ (Prompt 0 recommended) |

Recommended run order: **0 → 1 → {2, 3, 6, 7, 8, 9} → {4, 5 once art is ready} → 10 → 11 →
12 → 13 → 14 → B1 → {B2, B3, B4} → B5 → B6.**

---

## Prompt 0 — Shared groundwork (do this first)

Two small pieces of shared code that several later prompts assume. Nothing user-facing changes.

```
Claude — two pieces of shared minigame groundwork. Plan first, return complete files, end with
an Editor checklist.

1. Scripted hand grip. MinigameHandRig.SetGrip(float) is a no-op (TODO(P2)) and
   PlayerIKRig.ApplyHandGripPose auto-curls the right hand toward a fist whenever LMB is held
   during ANY active minigame. That is wrong for minigames where LMB means something (bow draw,
   banner/picture drag, pour grab, duel swing).
   - Add to PlayerIKRig: a scripted grip channel — SetScriptedGrip(float amount01) /
     ClearScriptedGrip() — that, while set, overrides the LMB-derived grip target in
     ApplyHandGripPose. Keep the existing click-pulse behaviour as the default when no scripted
     value is set.
   - Add a HandMinigame bool `autoCurlFromPrimary = true`. When false, HandMinigame calls
     ClearScriptedGrip semantics so the LMB auto-curl is suppressed for that minigame.
   - Route MinigameHandRig.SetGrip(amount01) → the player's PlayerIKRig scripted channel, and
     End() → clear it.
   Do not change how deposit minigames feel — they act on mouse-DOWN, so leave autoCurlFromPrimary
   true for ItemDepositMinigame.

2. MinigameChain capability. New Assets/Scripts/Minigames/Capabilities/MinigameChain.cs,
   [Serializable], per ARCHITECTURE.md §5:
   - fields: `MinigameBase nextMinigamePrefab`, `float startDelaySeconds = 0f`.
   - a method the owning minigame calls from its success path that instantiates nextMinigamePrefab
     and runs SetupMinigame(context) with a FRESH MinigameContext carrying the same Player and the
     task's *next* step context (Task may be the same TaskInstance — the first minigame's
     CompleteMinigame already advanced the step via player.FinishMinigame).
   - Wire it as an opt-in: add `protected void ChainNextIfSet()` on MinigameBase that
     CompleteMinigame calls after player.FinishMinigame but before Destroy, guarded on a
     `protected virtual MinigameChain Chain => null;` override. Default null = today's behaviour.
   Only Prompt 13 (Cheers→Drink) will set Chain; make sure an unset Chain is a no-op.

End by telling me what to check in the Editor.
```

---

## Tier 1 — Deposits ✅

### Prompt 1 — Five deposit minigames

`ItemDepositMinigame` already ships on the sword rack and dowry chest. `VaseDepositMinigame` is
already a near-generic "carry pose + dead-straight drop + station pass-through" concrete. One
generic class covers all five of your remaining deposits; five prefab variants differ only in
Inspector values.

```
Claude — implement the deposit minigames for: coin bag → gift box, vase → table, gift box →
table, arrow → quiver, firework → marked spot.

Write ONE generic concrete: Assets/Scripts/Minigames/PropDepositMinigame.cs : ItemDepositMinigame.
Model it on VaseDepositMinigame (carry pose in the right hand, dead-straight GuidedDrop with
rotation + X/Z frozen, no bounce). Expose as serialized fields: carryLocalPos, carryLocalEuler,
GuidedDrop tuning (maxAngularVelocity, freezeRotation, freezeHorizontalPosition, materialName),
and a `bool passThroughStationColliders` (VaseDepositMinigame's SetVasePassable behaviour, off by
default — on for quiver and gift box, off for the firework spot and table). Reuse VaseDeposit
Minigame's LookActive/FootworkActive/AllowTapCancel/WantsFreeCursor gates. Do NOT duplicate any
release/settle/retry/deposit logic — that all lives in ItemDepositMinigame now.

If PropDepositMinigame ends up byte-for-byte VaseDepositMinigame plus one bool, say so and I'll
just retarget VaseDepositMinigame instead of adding a class.

Editor wire-up I will do: one prefab variant per station under Assets/Prefabs/Minigames/, each
assigned to the matching TaskDepositStation's `depositMinigamePrefab` field (same slot the sword
rack / dowry chest use). Stations/locations: GiftBox, {table locationID for vase}, HighTable or
the gift-box table, the quiver station, the firework spot. The DepositItemStep on gift_coin /
gift_move / flower_move_vase / quiver_fill / (firework task) needs NO minigamePrefab — clear the
placeholder GUID if one is set.

Tell me every DropSlot / DepositTarget volume I need to place on each station prefab, and the
Editor checklist.
```

*Art you owe:* nothing. Prefabs for the five stations must exist with a `TaskDepositStation`,
`TaskLocation`, at least one `DropSlot`, and ideally a `DepositTarget` trigger volume per slot.

---

## Tier 2 — Tool-on-target ⚠️ (`ToolOnTargetMinigame`, first instantiation)

### Prompt 2 — Cut the cake / cut the turkey leg

```
Claude — first concrete(s) on ToolOnTargetMinigame: CakeCutMinigame and TurkeyCutMinigame (or one
CutIntoPieceMinigame with the piece prefab serialized — your call, whichever is cleaner).

Behaviour: hold the knife (Context.HeldItem) posed in the right hand; AimFromMouse drives it;
while the knife tip is within contactRadius of the target's working point, a click registers one
cut; ConfigureCount(cutsRequired, default 3). On completion use the `produce`
SpawnAndCarryObjective to spawn the CakePiece / TurkeyLeg PickupItem next to the target and
(ConsumeToolOnComplete stays false — the knife is reusable; the CAKE/TURKEY is the target, not the
tool, so nothing is consumed). The follow-up AcquireItemStep + DepositItemStep to the Plate
already exist on cake_cut / turkey_cut.

This is the first time ToolOnTargetMinigame runs. Exercise it hard: verify OnHandBegin resolves
tool + target, the Progress.Completed → OnProgressComplete → produce.Produce path fires once,
and CompleteMinigame advances the ProcessItemStep. Fix anything broken IN ToolOnTargetMinigame
and call those fixes out separately from the concrete.

Editor wire-up I will do: a prefab per minigame under Assets/Prefabs/Minigames/; assign to the
ProcessItemStep.minigamePrefab on cake_cut (targetStationID Cake) and turkey_cut. Assign
`produce.producedPrefab` = CakePiece.prefab / the turkey-leg prefab. Give me the list of
transforms the target prefab (Cake, Turkey) needs (a "working point" child) and the knife's
grip-pose numbers to fill in, plus the Editor checklist.
```

*Art you owe:* a TurkeyLeg `PickupItem` prefab + `ItemDefinition` (CakePiece already exists).
Optional slice/cut mesh swap on the Cake/Turkey.

### Prompt 3 — Light the candle / light the firework

```
Claude — LightWithFlintMinigame : ToolOnTargetMinigame, used for BOTH the candle and the firework
fuse (one class, differences in the Inspector).

Behaviour: hold the flint-and-steel (Context.HeldItem) in the right hand. AimFromMouse. When the
tool is within contactRadius of the target's wick/fuse point AND the player clicks, emit a
"spark": ConfigureAccumulate — each successful spark near the point adds a random small amount;
enough accumulated = lit. On OnProcessed(): enable the flame / fuse VFX object (serialized
Transform) and, for the firework, kick off its existing launch behaviour if any. No item is
produced or consumed.

First real ToolOnTargetMinigame that uses Accumulate mode — verify the tracker maths and the
single OnProcessed call. Call out base-class fixes separately.

Editor wire-up I will do: prefab per use under Assets/Prefabs/Minigames/; assign to
candle_light's ProcessItemStep and the firework-light task's ProcessItemStep. Tell me the child
transforms the Candle and Firework prefabs each need (spark target point, flame/fuse VFX root)
and the Editor checklist.
```

*Art you owe:* 🎨 a flame particle prefab for the candle and a fuse-spark / lit-fuse VFX for the
firework. The minigame just calls `SetActive(true)` on whatever you give it.

### Prompt 4 — Polish the dirty sword

```
Claude — SwordPolishMinigame : ToolOnTargetMinigame using Progress ZONES.

Behaviour: hold the cloth (Context.HeldItem) in the right hand, AimFromMouse. The dirty sword is
the target. Divide its surface into N dirt zones (serialized array of Transforms, or child
objects tagged on the sword prefab). While the cloth is within contactRadius of a zone and the
player holds LMB and moves the mouse (a rub), accumulate wipe time for that zone; past a
per-zone threshold call Progress.MarkZone(i) and hide/fade that zone's dirt object. All zones
marked = ConfigureZones completion → the sword's state gains the Processed flag (sword_polish's
DepositItemStep already requires requiredState = Processed / 1). No item produced.

Keep the dirt-reveal hook clean: a `virtual void RevealZone(int i, float t01)` I can back with
either N toggled dirt meshes OR a mask-texture shader later — default implementation just
SetActive(false) at t01 >= 1.

Editor wire-up I will do: prefab under Assets/Prefabs/Minigames/, assigned to sword_polish's
ProcessItemStep.minigamePrefab (targetItem = the dirty sword ItemDefinition). Give me the dirt-
zone child layout the sword prefab needs and the Editor checklist.
```

*Art you owe:* 🎨 the dirt visual — decide now: (a) N small dirt-patch meshes parented to the
sword, toggled off per zone (works today, no shader), or (b) a dirt overlay material with a
reveal mask (needs a shader). The prompt above assumes (a) with a hook for (b).

### Prompt 5 — Write your name on the contract

```
Claude — WriteOnContractMinigame : ToolOnTargetMinigame, Accumulate mode as an "ink budget".

Behaviour: hold the pencil (Context.HeldItem) in the right hand, AimFromMouse. The contract is
the target. While the pencil tip is within contactRadius of the contract's writing plane and LMB
is held, consume ink budget proportional to hand movement (so scribbling burns it fast);
ConfigureAccumulate(inkBudget). Completion = budget spent (or a "done" affordance once past a
minimum). Expose the writing plane + tip transforms.

Stroke rendering: put it behind a `virtual void EmitStroke(Vector3 worldFrom, Vector3 worldTo)`
that the minigame calls each frame it's writing. Default implementation: no-op (logic still
completes). I'll back it with a RenderTexture/LineRenderer later.

Editor wire-up I will do: prefab under Assets/Prefabs/Minigames/, assigned to contract_sign's
ProcessItemStep.minigamePrefab. Give me the child transforms the Contract and Pencil prefabs
need, and the Editor checklist.
```

*Art you owe:* 🎨 stroke rendering. Decide: LineRenderer ribbons in world space, or paint into a
RenderTexture on the contract material. Not a blocker — the step completes without it.

---

## Tier 3 — Consume & pour ⚠️

### Prompt 6 — Eat the cake piece off a plate

```
Claude — EatOffPlateMinigame : ConsumeMinigame (first concrete on ConsumeMinigame; the shipping
ConsumeItemMinigame stays untouched as the simpleMode/faker path).

Behaviour: simpleMode = false. The player is holding the plate (Context.HeldItem). The thing that
actually moves to the mouth is the CakePiece child PickupItem on the plate — override so `item`
resolves to that child, attach IT to the hand, and set destroyWholeItem = false so Finish()
destroys the child and marks the plate Spent. servings = 3, mouthRadius default. mouthAnchor from
the Animator Head bone is already handled by the base.

Shake out ConsumeMinigame's OnHandBegin / armed-between-servings / Finish path. Call out base
fixes separately.

Editor wire-up I will do: prefab under Assets/Prefabs/Minigames/, assigned to cake_eat's
ConsumeItemStep.minigamePrefab. Editor checklist at the end.
```

### Prompt 7 — Pour wine from a pitcher

```
Claude — PourWineMinigame : PourMinigame (first concrete on PourMinigame).

Behaviour follows the base phase machine (GrabEmpty → GrabSource → Pour → Done):
- EmptyVesselCandidate(): the empty glass PickupItem near the station (serialized, or nearest
  PickupItem with the empty-glass ItemDefinition).
- SourceVesselCandidate(): the pitcher PickupItem.
- IsAimedAtGlass(): true when the pitcher's serialized spout transform is horizontally within
  aimTolerance of the glass's serialized mouth transform, and the spout is above the mouth.
- OnPourTick: drive a stream particle + a liquid-level renderer on the glass (behind virtual
  hooks; default no-op).
On Fill completion the base completes the minigame; wine_pour's ProcessItemStep produces the
filled-glass item (requiredItem on the following DepositItemStep is the GlassOfWine
ItemDefinition — confirm the produced item ends up with that identity; if the base doesn't spawn
one, add a serialized producedGlassPrefab and swap the empty for it on completion).

Set HandMinigame.autoCurlFromPrimary = false for this one (LMB is the grab, not a fist) — needs
Prompt 0.

Editor wire-up I will do: prefab under Assets/Prefabs/Minigames/, assigned to wine_pour's
ProcessItemStep.minigamePrefab (targetStationID WineBarrel). Give me the spout/mouth child
transforms the pitcher and glass prefabs need, and the Editor checklist.
```

*Art you owe:* 🎨 a pour-stream particle and a glass liquid-level visual (both optional; logic
completes without them). Empty-glass, pitcher, and filled-glass prefabs + `ItemDefinition`s.

---

## Tier 4 — Drag ⚠️ (`DragObjectMinigame`, first instantiation)

### Prompt 8 — Unfurl the rolled banner

```
Claude — UnfurlBannerMinigame : DragObjectMinigame, mode = Axis (first concrete on
DragObjectMinigame).

Behaviour: the rolled banner (Context.Target) is grabbable. localAxis = local down, axisTravel =
the drop length. ApplyDrag: project the per-frame hand delta onto world-down, add to value01 /
axisTravel, and drive the banner's visible unroll from value01 (scale a child on Y, or reveal a
cloth mesh — behind a `virtual void ApplyUnroll(float v01)` with a default that scales
grabbable's localScale.y). value01 >= 1 → complete → the banner's state gains Processed
(fix_banner's step).

Shake out DragObjectMinigame: the grab-radius check, the grabbed/lastHandPos bookkeeping, IsDone.
Call out base fixes separately. Set autoCurlFromPrimary = false (Prompt 0).

Editor wire-up I will do: prefab under Assets/Prefabs/Minigames/, assigned to fix_banner's
ProcessItemStep (or StationInteractStep) minigamePrefab. Tell me the banner prefab's child
layout and the Editor checklist.
```

### Prompt 9 — Straighten the crooked picture

```
Claude — StraightenPictureMinigame : DragObjectMinigame, mode = Angle.

Behaviour: the crooked picture (Context.Target) starts with a non-zero local Z roll. It's
grabbable within grabRadius. ApplyDrag: convert the hand's vertical/horizontal delta into a
change in the picture's local Z rotation (drag one corner). IsLevel() (|roll| <= levelTolerance,
default 3°) → complete → picture state gains Processed (fix_painting's step). Add a small
`snapToLevelOnComplete` that eases the last few degrees to exactly 0.

Set autoCurlFromPrimary = false (Prompt 0). Editor wire-up I will do: prefab assigned to
fix_painting's step. Give me the picture prefab's pivot/child requirements and the Editor
checklist.
```

---

## Tier 5 — Charge/release & instruments ⚠️

### Prompt 10 — Bow & arrow: nock, draw, loose

```
Claude — BowAndArrowMinigame : ChargeReleaseMinigame (first concrete on ChargeReleaseMinigame).

Behaviour on the base phase machine (Equip → Idle → Nocked → Drawing → Loosed):
- EquipComplete(): true once the player has grabbed the bow and the quiver — a two-step grab like
  PourMinigame's (serialized bow + quiver PickupItems, click within grabRadius each). Attach the
  bow to the right hand.
- OnNock: spawn/show a nocked arrow on the bow.
- OnDraw(charge01): pull the string back and (optionally) SetGrip on the string hand; the base
  already reads MinigameInput.DrawPull() (backward mouse-Y).
- SpawnProjectile: override to launch the arrow prefab from the arrow's nock transform along the
  aim, speed lerp'd min→max by charge. shotsToComplete configurable (default 1).
Set HandMinigame.autoCurlFromPrimary = false — LMB is nock/hold/loose (needs Prompt 0).

Shake out ChargeReleaseMinigame: the Nocked/Drawing transition, charge clamp, Loose() dir maths.
Call out base fixes separately.

Editor wire-up I will do: prefab under Assets/Prefabs/Minigames/, assigned to bow_string's step
(StationInteractStep / ProcessItemStep). Give me the bow / arrow / quiver prefab child
requirements (nock point, string bones) and the Editor checklist.
```

*Art you owe:* bow, arrow (with a `Rigidbody`), quiver prefabs. A simple flight/stick behaviour
on the arrow is nice-to-have.

### Prompt 11 — Instruments: flute, trumpet, guitar

```
Claude — three concretes:
  FluteMinigame  : WindInstrumentMinigame
  TrumpetMinigame: WindInstrumentMinigame   (same as flute, different prefab/audio)
  GuitarMinigame : StringInstrumentMinigame

WindInstrument: base already brings the instrument to the mouth anchor (Head bone) and gates
notes on mouthRadius; A/S/D/F/G play notes via MinigameInput.NoteKeysDown(). Guitar: base gates
each note on MinigameInput.MouseStrum(strumThreshold). All three complete at notesToComplete
valid notes. Add OnNotePlayed → play the per-note AudioClip (serialized array indexed by bit) and
an optional finger/valve visual hook (default no-op).

First concretes on the InstrumentMinigame family — verify the mouth-distance gate, the strum
gate, and the bit-count → Progress.Tick(n) path. Call out base fixes separately.

Editor wire-up I will do: one prefab per instrument under Assets/Prefabs/Minigames/; the
instrument_play task points its StationInteractStep at whichever instrument's prefab (or three
task variants). instrument_move handles carrying the instrument to the stand. Give me the grip-
pose numbers per instrument and the Editor checklist.
```

*Art you owe:* 🎨 flute / trumpet / guitar models as `PickupItem` prefabs, and five note
`AudioClip`s per instrument (or a scale's worth).

---

## Tier 6 — Partner ⚠️ (`PartnerMinigame`, first instantiation)

`PartnerMinigame` resolves a real player or spawns a `DummyPartner` and auto-accepts after
`dummyAutoAcceptDelay` — solo testing is built in. It does **not** extend `HandMinigame`, so it
has no hand rig, no freeze, no RMB-look, no settings-pause. Prompts 12 and 14 compose
`MinigameHandRig` directly and must handle their own freeze/cursor.

### Prompt 12 — Handshake (RMB-aim) + solo test

```
Claude — HandshakeMinigame : PartnerMinigame (first concrete on PartnerMinigame).

Behaviour:
- OnPartnerBegin: FacePartner is already called by the base. Freeze the initiator
  (player.SetControlsLocked(true), free the cursor) yourself — PartnerMinigame is not a
  HandMinigame. Compose a MinigameHandRig directly (new MinigameHandRig(player, cam); cam from
  player.PlayerCamera). Hand.Begin().
- OnMinigameUpdate: while RMB is held, Hand.AimFromMouse so the right hand tracks toward the
  partner; when the hand is within a small radius of the partner's right-hand/hand-shake anchor,
  fire MirrorOnPartner("Handshake") (plays on the DummyPartner too) and, once PartnerAccepted,
  CompleteMinigame.
- OnMinigameEnd: Hand.End(), unfreeze, re-lock cursor. Do this in an override — the base's
  OnMinigameEnd only dismisses the dummy.
Set the scripted grip to a soft close during the shake (needs Prompt 0); don't rely on the LMB
auto-curl (LMB is unused here).

Shake out PartnerMinigame + PartnerResolver + DummyPartner end to end in a solo scene. Call out
base fixes separately.

Editor wire-up I will do: prefab under Assets/Prefabs/Minigames/, assigned to talk_greet's
PlayerInteractStep.minigamePrefab; `dummyPartnerPrefab` = a humanoid dummy with PlayerController +
DummyPartner. Give me the handshake anchor transform the character rig needs and the Editor
checklist.
```

*Art you owe:* 🎨 a "Handshake" animation clip + Animator trigger on both the player and the
DummyPartner controller. A `DummyPartner` prefab (spare Synty humanoid).

### Prompt 13 — Cheers, then auto-start Drink wine

```
Claude — two minigames plus the chain between them. Needs Prompt 0 (MinigameChain).

CheersMinigame : PartnerMinigame
- Both participants must be holding a wine glass (the initiator's is Context.HeldItem; for the
  dummy, pre-equip via DummyTestHelper). Freeze + compose MinigameHandRig like Prompt 12.
- OnMinigameUpdate: RMB-aim the glass hand toward the partner's glass; on contact fire
  MirrorOnPartner("Cheers") and, once PartnerAccepted, CompleteMinigame.
- Override Chain => a MinigameChain with nextMinigamePrefab = the DrinkWineMinigame prefab so
  CompleteMinigame launches it. talk task: this is step 1 (PlayerInteractStep); Drink is step 2
  (ConsumeItemStep).

DrinkWineMinigame : ConsumeMinigame
- simpleMode = false, servings = 2-3, the held wine glass goes to the mouth anchor, destroy on
  finish. Standalone-launchable too (its own ConsumeItemStep.minigamePrefab).

Verify the chain hands the SAME player + task through, that the first CompleteMinigame's
FinishMinigame advances to the Drink step before the chain fires, and that the chained minigame
registers in MinigameBase's registry normally.

Editor wire-up I will do: two prefabs under Assets/Prefabs/Minigames/; Cheers → the wine task's
PlayerInteractStep, Drink → the following ConsumeItemStep, and Cheers' MinigameChain.
nextMinigamePrefab → the Drink prefab. Editor checklist at the end.
```

*Art you owe:* 🎨 "Cheers" clink clip + trigger on player and DummyPartner. Wine-glass prefab.

### Prompt 14 — Sword duel (mouse-swing, 3 hits)

```
Claude — SwordDuelMinigame : PartnerMinigame. The hardest partner minigame — plan carefully.

Behaviour:
- Both fighters hold a sword (initiator's = Context.HeldItem; dummy pre-equipped). Freeze the
  initiator, compose MinigameHandRig directly, cursor stays LOCKED (this one reads mouse motion,
  not a screen cursor).
- OnMinigameUpdate: MinigameInput.MouseSwing() (screen-space mouse velocity) drives the right
  arm — map swing direction/speed to the hand's reach offset + SetHandRotation so the blade
  sweeps. Set autoCurlFromPrimary = false and hold a scripted firm grip (Prompt 0).
- Hit detection: when the blade's cutting edge passes through the opponent's hit volume above a
  min swing speed, register a hit (short i-frames after each). First to 3 hits wins; if the
  initiator reaches 3 → CompleteMinigame, if the dummy does → CancelMinigame (or a lose path).
- DummyPartner: give it a simple telegraphed swing loop (extend DummyPartner with a
  `BeginSparring()` that triggers periodic "DummySwing" + drives a coarse blade transform) so
  solo play has something to parry/trade with. Auto-accept still applies as the fallback.
- OnMinigameEnd override: Hand.End(), unfreeze.

This exercises PartnerMinigame + MouseSwing + a hit tracker for the first time. Keep the hit
volume / blade edge references serialized. Call out any base fixes (PartnerMinigame,
MinigameInput.MouseSwing tuning, DummyPartner) separately from the concrete.

Editor wire-up I will do: prefab under Assets/Prefabs/Minigames/, assigned to the duel task's
MutualPlayerInteractStep.minigamePrefab; dummyPartnerPrefab equipped with a sword. Give me the
blade-edge + hit-volume transforms each character/sword needs and the Editor checklist.
```

*Art you owe:* 🎨 swing/hit-react clips + triggers, a duel sword prefab, hit-volume colliders on
the character rig.

---

## 🔴 Not implementable yet

### Emote minigames — Speech (18), Dance (19), Conversation (23)

**Why blocked:** `EmoteMinigame` calls `EmoteWheelController.Open()`, which sets `IsOpen = true`
and stops at `// TODO(P4): show the radial UI`. Nothing ever calls `Commit()` — that's the wheel
UI's job and the wheel UI does not exist. An `EmoteMinigame` launched today opens a wheel that
can never resolve and hangs. This one missing component blocks all three.

**What you must provide before B1 can finish:**
1. A **wheel Canvas prefab** — layout for N radial slices + a category-tab strip. I'll write the
   controller; you supply the prefab shell and slice/tab sprites.
2. **`EmoteDefinition` assets** — at least a few per category (Assets ▸ Create ▸ Corrupted Court ▸
   Emote). Each needs a `displayName`, `category`, and an `animTrigger`.
3. **Animation clips + Animator triggers** on the player (and DummyPartner) matching every
   `EmoteDefinition.animTrigger` you author.
4. A decision: is the wheel a **scene singleton** on the HUD, or spawned per-open? (`Open` is an
   instance method on a `static Instance` — singleton is the intended shape.)

```
[PROMPT B1 — run only once items 1-4 above exist]
Claude — build the EmoteWheelController radial UI (ARCHITECTURE.md §6). Drive it from Open():
show the wheel Canvas, populate slices from CurrentOptions (respecting ActiveFilter), a category-
tab strip across the top, hover-by-mouse-angle selection, hold-openKey / release-to-Commit (and
release-off-any-slice → Cancel). Keep the existing Open/Commit/Cancel API and the catalogue.
Freeze player look while open. Verify a bare EmoteMinigame (Speech) now completes end to end.
Return complete files + an Editor checklist for wiring the wheel prefab and the catalogue.
```

```
[PROMPT B2 — Speech, after B1]
Claude — SpeechAtPodiumMinigame : EmoteMinigame, requiredCategory = Speech. Launch from a
StationInteractStep on talk_speech (targetStationID Podium). OnEmoteAccepted: optional crowd
reaction hook. Wire-up I'll do: prefab → the step; Podium station + Speech-category
EmoteDefinitions. Editor checklist at the end.
```

```
[PROMPT B3 — Dance, after B1]
Claude — DanceTogetherMinigame : PartnerMinigame that COMPOSES EmoteWheelController (per
ARCHITECTURE.md §6, not EmoteMinigame — it needs the partner + proximity + time-overlap check).
Both players must commit a Dance-category emote within `togetherRadius` and overlapping in time.
Solo: the DummyPartner auto-"dances" after its accept delay. Launch from dance_court's
PlayerInteractStep / MutualPlayerInteractStep. Wire-up I'll do: prefab + dummyPartnerPrefab +
Dance EmoteDefinitions. Editor checklist.
```

```
[PROMPT B4 — Conversation, after B1]
Claude — ConversationMinigame : PartnerMinigame composing EmoteWheelController, category
Conversation. Success = the two participants ALTERNATE N committed emotes (initiator, partner,
initiator, ...). Solo: DummyPartner replies with a Conversation emote each turn. Launch from the
conversation task's PlayerInteractStep. Wire-up I'll do: prefab + dummy + Conversation
EmoteDefinitions. Editor checklist.
```

### Book of names — memorise, order by arrival (5)

**Why blocked:** two gaps, one of them a data-model decision only you can make.
1. **No arrival-order data exists.** "Order of arrival at court" implies a per-player arrival
   index or timestamp. Nothing records one. `RoleManager.allPlayers` is Inspector-populated and
   its order is authoring order, not arrival order.
2. The task asset `ledger_check.asset` currently uses `DataRetrievalStep` (a code-entry puzzle:
   `sourceStationID: Ledger`, `inputStationID: ConfirmationBox`). A memorise-then-order puzzle is
   a different step — ARCHITECTURE.md §8 calls for a new `SequenceRecallStep`.
3. The open-book presentation (names laid out on two pages) is a UI build — decide world-space
   Canvas on a book model vs. full-screen panel.

**Decision to make first (Prompt B5):**

```
[PROMPT B5 — decide + scaffold]
Claude — I want the "book of names" puzzle. First propose the arrival-order data model and let me
pick: (a) an `arrivalIndex` int stamped on each PlayerController when it first enters the play
area / spawns, exposed for read; (b) a timestamp list on RoleManager or MatchManager; (c) reuse
allPlayers order as a stand-in for now. Then, for whichever I pick, write:
 - the minimal data change (with [FormerlySerializedAs] / [MovedFrom] where serialized),
 - a new SequenceRecallStep : TaskStep (present a sequence, check the player's ordered input at a
   station), following the existing Steps/ file conventions — remember the SerializeReference
   nesting hazard in ConcreteTaskSteps history and the .asset migration approach in memory.
Return complete files + tell me what to re-author in ledger_check.asset (or a new task asset).
```

```
[PROMPT B6 — the minigame, after B5]
Claude — BookOfNamesMinigame : PanelMinigame. Show the roster (names from the chosen arrival-
order source, displayed SHUFFLED) on an open-book Canvas for a memorise window, then let the
player drag the names into order; submit at the ConfirmationBox. RequireClicks / Win / Lose from
the base. Correct order → Win. Wire-up I'll do: the book Canvas prefab, prefab → the
SequenceRecallStep.minigamePrefab. Editor checklist.
```

*Art you owe:* an open-book model/prefab with two text pages (or a styled panel).

---

## One cross-cutting note

`SpawnAndCarryObjective.PushCarryHint()` is a `// TODO(P5)` no-op — the "now carry it over there"
waypoint nudge. It is **not** a blocker: the follow-up `AcquireItemStep` / `DepositItemStep` on
each producing task drives the real objective text. If the missing nudge bothers you during
testing, fold it into Prompt 2.
