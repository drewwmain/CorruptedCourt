using System.Globalization;
using UnityEngine;
using CorruptedCourt.Core;
using CorruptedCourt.Gameplay;

namespace CorruptedCourt.Minigames
{
    /// <summary>
    /// Keeps a held item from ever penetrating world geometry by rotating it about its grip pivot.
    /// The item's POSITION relative to the hand never changes - only its rotation (plus a translation
    /// that compensates a pivot offset so the pivot itself stays put). This is a PREDICTIVE swept
    /// solve: every candidate pose is cast-tested before it is committed, so penetration is never
    /// visible for even one frame - it is not a depenetration correction that fixes things up after
    /// the fact.
    ///
    /// This is NOT <see cref="StationContactProbe"/>: that is a downward resting ray used as a deposit
    /// gate. Do not reuse or extend it.
    ///
    /// Not a MonoBehaviour - a minigame owns one as a field (like <see cref="MinigameHandRig"/> /
    /// <see cref="GuidedDrop"/>) and calls:
    ///  - <see cref="Begin"/> once, when the item is picked up into the aiming phase,
    ///  - <see cref="Solve"/> every frame from <c>OnMinigameLateUpdate</c>, AFTER it has authored the
    ///    item's rest rotation for the frame (see <c>SwordHangMinigame.OnMinigameLateUpdate</c>).
    ///    Whatever the item's transform holds when Solve runs IS the rest pose; Solve reads it fresh,
    ///    solves, and commits.
    ///  - <see cref="End"/> when the aiming phase is over. End performs no transform write - it leaves
    ///    the item exactly as the owner last set it (GuidedDrop owns the item after release; this
    ///    class must be inert by then).
    ///  - <see cref="DrawDebug"/> / <see cref="DebugSummary"/> any time, to inspect the last solve
    ///    before this is wired into a minigame.
    ///
    /// All tuning comes from the item's authoring data (<see cref="PickupItem.GripConstraint"/> -
    /// <see cref="GripConstraintSettings"/>): the grip pivot, a DISABLED contact-proxy collider whose
    /// dimensions define the swept shape, the wrist + slip angle budget, the contact skin, and the
    /// normal-smoothing window. The proxy collider is measured, never enabled, never simulated.
    /// </summary>
    public class MinigameGripConstraint
    {
        // Max travel of the proxy's farthest point per angular substep. A single linear cast tunnels
        // straight through a thin rack post when the item also rotates during the sweep, so the
        // last-frame -> this-frame motion is marched in steps this small.
        private const float SubstepMeters = 0.02f;
        private const int MaxSubsteps = 24;
        private const int BisectIterations = 6;

        // Casts start this far behind their travel so an already-touching start still reports a real
        // contact point / normal instead of a degenerate distance-0 "initial overlap" hit.
        private const float CastBackoff = 0.01f;

        private const float DegenerateAxisSqr = 1e-8f;
        private const float NegligibleTravel = 1e-5f;

        // DrawDebug only - line sizes in metres, plus the "not blocked" contact colour (orange, since
        // UnityEngine.Color has no orange).
        private const float PivotCrossHalf = 0.03f;
        private const float ContactCrossHalf = 0.02f;
        private const float NormalRayLength = 0.12f;
        private static readonly Color OrangeContact = new Color(1f, 0.55f, 0f);

        private enum ProxyKind { Unsupported, Capsule, Box }

        // --- dependencies -------------------------------------------------------------------------
        // The cosmetic wrist mirror (MirrorWristFollow) drives the hand through 'hand' only - never
        // PlayerController / PlayerIKRig directly. The item-clearance solve uses neither.
        private readonly MinigameHandRig hand;
        private readonly int contactMask;
        private readonly RaycastHit[] hitBuf = new RaycastHit[8];
        private readonly Vector3[] normalBuf = new Vector3[10]; // ring buffer; capacity == max normalSmoothingFrames

        // --- per-Begin cache --------------------------------------------------------------------------
        private PickupItem item;
        private Transform itemTf;
        private GripConstraintSettings settings;
        private Collider proxy;
        private Transform proxyTf;
        private ProxyKind proxyKind;
        private bool inert = true;
        private bool warnedUnsupported;

        // proxy geometry, raw local (read once off the collider - never enabling it)
        private Vector3 proxyCenterLocal;
        private float capRadiusLocal;
        private float capHeightLocal;
        private int capDirection;
        private Vector3 boxSizeLocal;

        // proxy pose relative to the item transform, captured while both are live. Rotation-only
        // offset: runtime item scale is assumed ~1 (matches how PickupItem parents held items).
        private Vector3 proxyPosItemLocal;
        private Quaternion proxyRotItemLocal;

        // --- per-frame state ------------------------------------------------------------------------
        private bool hasLastPose;
        private Vector3 lastPos;
        private Quaternion lastRot;   // committed rotation from the last Solve (also used by DrawDebug)
        private int normalCount;
        private int normalHead;

        // --- per-Solve scratch (Solve is not reentrant) -------------------------------------------
        private Vector3 sLossyScale;
        private Vector3 sAxisLocal;
        private float sRadius;   // capsule: world radius
        private float sHalfSeg;  // capsule: half the distance between the two sphere centres
        private Vector3 sHalfExtents; // box: world half extents

        // --- last-Solve snapshot for DrawDebug / DebugSummary -----------------------------------------
        private bool lastHadCastContact;   // did the last Solve hit geometry with a cast?
        private int lastSubsteps;          // N from the last Solve's angular march
        private Vector3 lastPivot;         // world grip pivot the last Solve used
        private Quaternion lastRestRot = Quaternion.identity; // item rotation the owner authored last Solve
        private Vector3 lastLeverLocal;    // pivot -> contact point in rest-local space (length preserved)
        private Vector3 lastCorrectionAxis; // world axis the last Solve rotated the item about (also the wrist axis)

        // --- public debug surface ----------------------------------------------------------------
        public float LastAppliedDegrees { get; private set; }
        /// <summary>Cosmetic wrist share of the last solve: <c>Min(LastAppliedDegrees, wristLimitDegrees)</c>. Follows one frame late, never subtracted from the item.</summary>
        public float LastWristDegrees { get; private set; }
        public bool IsBlocked { get; private set; }
        public bool HasContact { get; private set; }
        public Vector3 LastContactPoint { get; private set; }
        public Vector3 LastContactNormal { get; private set; }

        /// <summary>Master switch for <see cref="DrawDebug"/>; the owner still calls DrawDebug itself each frame.</summary>
        public bool debugDraw;

        public MinigameGripConstraint(LayerMask contactMask, MinigameHandRig hand)
        {
            this.hand = hand;
            this.contactMask = contactMask; // LayerMask -> int
        }

        /// <summary>Cache the item's settings, proxy dimensions and pivot; reset all solve state.</summary>
        public void Begin(PickupItem item)
        {
            ResetState();

            this.item = item;
            itemTf = item != null ? item.transform : null;
            settings = item != null ? item.GripConstraint : null;
            proxy = null;
            proxyTf = null;
            proxyKind = ProxyKind.Unsupported;
            inert = true;

            if (hand == null)
                Log.Warn("[MinigameGripConstraint] constructed with a null MinigameHandRig - the cosmetic wrist mirror will be unavailable (the item-clearance solve still runs).");

            if (item == null || itemTf == null || settings == null)
            {
                Log.Warn("[MinigameGripConstraint] Begin called with no item / grip settings - solver is inert.");
                return;
            }

            if (!settings.enableGripConstraint)
                return; // master switch off: hold the capability, do nothing. Valid config, no warning.

            proxy = settings.contactProxy;
            if (proxy == null)
            {
                Log.Warn($"[MinigameGripConstraint] {item.DisplayName}: grip constraint enabled but no contactProxy collider is assigned - solver is inert.");
                return;
            }
            proxyTf = proxy.transform;

            if (proxy is CapsuleCollider cap)
            {
                proxyKind = ProxyKind.Capsule;
                proxyCenterLocal = cap.center;
                capRadiusLocal = cap.radius;
                capHeightLocal = cap.height;
                capDirection = cap.direction;
            }
            else if (proxy is BoxCollider box)
            {
                proxyKind = ProxyKind.Box;
                proxyCenterLocal = box.center;
                boxSizeLocal = box.size;
            }
            else
            {
                if (!warnedUnsupported)
                {
                    Log.Warn($"[MinigameGripConstraint] {item.DisplayName}: contactProxy is a {proxy.GetType().Name} - only CapsuleCollider and BoxCollider are supported. Solver is inert.");
                    warnedUnsupported = true;
                }
                return;
            }

            proxyPosItemLocal = Quaternion.Inverse(itemTf.rotation) * (proxyTf.position - itemTf.position);
            proxyRotItemLocal = Quaternion.Inverse(itemTf.rotation) * proxyTf.rotation;

            inert = false;
        }

        /// <summary>Clear solve state. Leaves the item's transform exactly as the owner set it.</summary>
        public void End()
        {
            ResetState();
            item = null;
            itemTf = null;
            settings = null;
            proxy = null;
            proxyTf = null;
            proxyKind = ProxyKind.Unsupported;
            inert = true;
        }

        private void ResetState()
        {
            hasLastPose = false;
            normalCount = 0;
            normalHead = 0;
            LastAppliedDegrees = 0f;
            LastWristDegrees = 0f;
            IsBlocked = false;
            HasContact = false;
            LastContactPoint = Vector3.zero;
            LastContactNormal = Vector3.zero;

            lastHadCastContact = false;
            lastSubsteps = 0;
            lastPivot = Vector3.zero;
            lastRestRot = Quaternion.identity;
            lastLeverLocal = Vector3.zero;
            lastCorrectionAxis = Vector3.zero;
        }

        /// <summary>
        /// One frame of solve: correct the ITEM so it is clear this frame (<see cref="SolveItemPose"/>),
        /// then mirror a capped share of that correction onto the hand, for looks only
        /// (<see cref="MirrorWristFollow"/>). Call from <c>OnMinigameLateUpdate</c>, after the owner has
        /// authored the item's rest rotation for the frame.
        /// </summary>
        public void Solve()
        {
            SolveItemPose();
            MirrorWristFollow();
        }

        /// <summary>The item-clearance solve. Reads the rest pose off the item, solves, commits the pose.</summary>
        private void SolveItemPose()
        {
            if (inert || item == null || itemTf == null || settings == null || proxy == null || proxyTf == null) return;

            // 1. Rest pose, read fresh. Whatever the owner authored this frame IS the rest pose.
            Vector3 restPos = itemTf.position;
            Quaternion restRot = itemTf.rotation;

            // 2. Grip pivot in world space. A null pivot means the item's own origin, so no
            //    translation is ever produced (see step 10).
            Vector3 pivot = settings.gripPivot != null ? settings.gripPivot.position : restPos;

            // Debug snapshot - refined as the solve proceeds; DrawDebug / DebugSummary read it next frame.
            lastPivot = pivot;
            lastRestRot = restRot;
            lastSubsteps = 0;
            lastHadCastContact = false;

            // Proxy world dimensions for this frame (a rescaled proxy keeps working; still no alloc).
            ComputeProxyWorldDims();

            // 3. Sweep origin: the pose committed last frame, or - on the first Solve after Begin -
            //    the rest pose itself (a stationary overlap check).
            Vector3 originPos = hasLastPose ? lastPos : restPos;
            Quaternion originRot = hasLastPose ? lastRot : restRot;

            // 4. Stationary branch: nothing to sweep. A zero-length cast cannot return a usable
            //    contact normal, so use the boolean overlap query and apply no rotation.
            if (!hasLastPose)
            {
                Vector3 sp = ProxyWorldPos(restPos, restRot);
                Quaternion sr = restRot * proxyRotItemLocal;
                HasContact = RawOverlap(sp, sr);
                LastAppliedDegrees = 0f;
                IsBlocked = false;
                CommitOrigin(restPos, restRot);
                return;
            }

            // 5. Angular substepping. The item rotates about the grip, so its far end travels an arc;
            //    pick N so the farthest proxy point moves at most SubstepMeters per substep.
            Vector3 farRest = FarthestProxyPoint(restPos, restRot, pivot);
            Vector3 farLocal = Quaternion.Inverse(restRot) * (farRest - restPos);
            Vector3 farOrigin = originPos + originRot * farLocal;
            float travel = Vector3.Distance(farOrigin, farRest);

            if (travel < NegligibleTravel)
            {
                // The item did not move relative to last frame's committed (clear) pose - keep it.
                LastAppliedDegrees = 0f;
                IsBlocked = false;
                HasContact = false;
                normalCount = 0;
                normalHead = 0;
                CommitOrigin(restPos, restRot);
                return;
            }

            int n = Mathf.Clamp(Mathf.CeilToInt(travel / SubstepMeters), 1, MaxSubsteps);
            lastSubsteps = n;

            // 6. March the origin -> rest motion in N substeps; stop at the first substep that hits.
            bool hit = false;
            RaycastHit contact = default;
            for (int i = 1; i <= n; i++)
            {
                float tA = (i - 1) / (float)n;
                float tB = i / (float)n;

                Vector3 posA = Vector3.Lerp(originPos, restPos, tA);
                Quaternion rotA = Quaternion.Slerp(originRot, restRot, tA);
                Vector3 posB = Vector3.Lerp(originPos, restPos, tB);
                Quaternion rotB = Quaternion.Slerp(originRot, restRot, tB);

                Vector3 pA = ProxyWorldPos(posA, rotA);
                Vector3 pB = ProxyWorldPos(posB, rotB);

                Vector3 delta = pB - pA;
                float segLen = delta.magnitude;
                if (segLen < 1e-6f) continue;
                Vector3 dir = delta / segLen;

                // Fixed orientation (rotA) for the segment cast - substeps are small enough that the
                // rotation within one is negligible, which is the whole point of choosing N above.
                Quaternion segRot = rotA * proxyRotItemLocal;
                Vector3 castStart = pA - dir * CastBackoff;

                if (MarchCast(castStart, segRot, dir, segLen + CastBackoff, out contact))
                {
                    hit = true;
                    break;
                }
            }

            lastHadCastContact = hit;

            // 7. No hit on any substep: the rest pose is reachable. It is already on the transform.
            if (!hit)
            {
                LastAppliedDegrees = 0f;
                IsBlocked = false;
                HasContact = false;
                normalCount = 0;
                normalHead = 0;
                CommitOrigin(restPos, restRot);
                return;
            }

            // 8. Contact point + normal. Smooth the normal over normalSmoothingFrames to de-jitter
            //    the rotation axis. Axis = (lever) x (normal); guard the degenerate near-zero cross.
            LastContactPoint = contact.point;
            Vector3 normal = PushSmoothNormal(contact.normal);
            LastContactNormal = normal;
            lastLeverLocal = Quaternion.Inverse(restRot) * (LastContactPoint - pivot);

            Vector3 axis = Vector3.Cross(LastContactPoint - pivot, normal);
            if (axis.sqrMagnitude < DegenerateAxisSqr)
            {
                // Lever ~parallel to the normal, or contact coincident with the pivot: no rotation
                // about any axis relieves this. Leave the rest pose.
                HasContact = true;
                LastAppliedDegrees = 0f;
                IsBlocked = false;
                CommitOrigin(restPos, restRot);
                return;
            }
            axis.Normalize();
            lastCorrectionAxis = axis; // world axis for both the item correction below and the wrist mirror

            // 9. Bisect the corrective angle in [0, wristLimit + slipLimit]. 0deg == the rest pose,
            //    which step 6 just proved blocked; the clamp is the largest correction allowed. Six
            //    iterations converge on the smallest angle whose clearance cast passes by at least
            //    contactSkin. Only a cast-validated angle is ever committed. If nothing in the range
            //    clears, the clamp binds.
            float limit = Mathf.Max(0f, settings.wristLimitDegrees + settings.slipLimitDegrees);
            float skin = Mathf.Max(0f, settings.contactSkin);
            float lo = 0f;
            float hi = limit;
            float bestClear = -1f;
            for (int i = 0; i < BisectIterations; i++)
            {
                float mid = 0.5f * (lo + hi);
                if (ClearAt(mid, axis, pivot, restPos, restRot, normal, skin))
                {
                    bestClear = mid;
                    hi = mid;
                }
                else
                {
                    lo = mid;
                }
            }

            float applied;
            if (bestClear >= 0f)
            {
                applied = bestClear;
                IsBlocked = false;
            }
            else
            {
                applied = limit;   // even the full wrist + slip budget did not clear
                IsBlocked = true;
            }

            // 10. Commit the full clamped angle to the ITEM, rotating about the grip pivot. The item
            //     takes the WHOLE angle: Phase C adds a cosmetic wrist mirror on top of this, and it
            //     never reduces what the item takes, because the wrist IK lands a frame late and the
            //     item must guarantee clearance THIS frame.
            LastAppliedDegrees = applied;
            HasContact = true;

            if (applied > 0f)
            {
                Quaternion q = Quaternion.AngleAxis(applied, axis);
                Quaternion newRot = q * restRot;

                if (settings.gripPivot != null)
                {
                    // Pivot is offset from the item origin: translate so the pivot stays fixed while
                    // the item rotates about it. This is the ONLY position write this class ever makes.
                    Vector3 newPos = pivot + q * (restPos - pivot);
                    itemTf.SetPositionAndRotation(newPos, newRot);
                    CommitOrigin(newPos, newRot);
                }
                else
                {
                    // Pivot == item origin: rotation only, position untouched.
                    itemTf.rotation = newRot;
                    CommitOrigin(restPos, newRot);
                }
            }
            else
            {
                CommitOrigin(restPos, restRot);
            }
        }

        // COSMETIC ONLY - not part of the clearance guarantee. SolveItemPose() has already committed the
        // WHOLE clamped angle to the ITEM, and the item is already clear THIS frame. This mirrors a capped
        // share of that same angle onto the HAND, about the same axis, so the wrist visibly gives instead
        // of the arm staying rigid. It is written from LateUpdate, but OnAnimatorIK has already run this
        // frame, so it does not reach the hand until the NEXT frame's IK pass - one frame late, by design.
        // That lag must NEVER be compensated by subtracting LastWristDegrees from what the item takes.
        // Routed through MinigameHandRig (never PlayerController) and only while the constraint is bound to
        // an in-hand item; once the item is released 'hand' still exists but RestorePlayer()/Hand.End()
        // has already zeroed the goal, and 'inert' is true here, so this writes nothing.
        private void MirrorWristFollow()
        {
            if (inert || settings == null || hand == null) { LastWristDegrees = 0f; return; }

            float wrist = Mathf.Min(LastAppliedDegrees, Mathf.Max(0f, settings.wristLimitDegrees));
            LastWristDegrees = wrist;

            if (wrist > 0f && lastHadCastContact)
                hand.MirrorHandRotation(wrist, lastCorrectionAxis, 1f);
            else
                hand.MirrorHandRotation(0f, Vector3.up, 0f); // no correction this frame -> hand back to the animated pose
        }

        // --- debug output --------------------------------------------------------------------------

        /// <summary>
        /// Emit one frame of <see cref="Debug"/> lines for the last <see cref="Solve"/>: the grip pivot
        /// (white 3-axis cross), the pivot -> contact direction shown through the authored rest rotation
        /// (grey), through the rotation the solve committed to the item (green) - they fan apart by
        /// <see cref="LastAppliedDegrees"/> - and through the cosmetic wrist share (cyan), which fans
        /// from grey by <see cref="LastWristDegrees"/> and so lies between grey and green. Also the
        /// contact point + normal (red when <see cref="IsBlocked"/>, orange otherwise). Draws nothing
        /// when the last Solve found no contact, before the first Solve, or while <see cref="debugDraw"/>
        /// is false. Uses Debug.DrawLine / Debug.DrawRay only (no Gizmos - this is not a MonoBehaviour).
        /// Call it every frame from the owner's per-frame method.
        /// </summary>
        public void DrawDebug()
        {
            if (!debugDraw || !lastHadCastContact) return;

            DrawCross(lastPivot, PivotCrossHalf, Color.white);

            // "Axis" = the pivot -> contact-point direction, drawn through the authored rest rotation
            // (grey) and the committed rotation (green); the gap between them is the applied correction.
            Vector3 restDir = lastRestRot * lastLeverLocal;
            Debug.DrawLine(lastPivot, lastPivot + restDir, Color.grey);
            Debug.DrawLine(lastPivot, lastPivot + lastRot * lastLeverLocal, Color.green);

            // The cosmetic wrist share: same axis, capped at wristLimitDegrees. Sits between grey and green.
            Debug.DrawLine(lastPivot, lastPivot + Quaternion.AngleAxis(LastWristDegrees, lastCorrectionAxis) * restDir, Color.cyan);

            Color c = IsBlocked ? Color.red : OrangeContact;
            DrawCross(LastContactPoint, ContactCrossHalf, c);
            Debug.DrawRay(LastContactPoint, LastContactNormal * NormalRayLength, c);
        }

        /// <summary>One line describing the last <see cref="Solve"/>, e.g. for an on-screen readout or Log.Game.</summary>
        public string DebugSummary()
        {
            return "grip: theta=" + LastAppliedDegrees.ToString("0.0", CultureInfo.InvariantCulture)
                 + " wrist=" + LastWristDegrees.ToString("0.0", CultureInfo.InvariantCulture)
                 + " blocked=" + IsBlocked.ToString()
                 + " contact=" + HasContact.ToString()
                 + " substeps=" + lastSubsteps.ToString(CultureInfo.InvariantCulture);
        }

        private static void DrawCross(Vector3 p, float half, Color c)
        {
            Debug.DrawLine(p - Vector3.right * half, p + Vector3.right * half, c);
            Debug.DrawLine(p - Vector3.up * half, p + Vector3.up * half, c);
            Debug.DrawLine(p - Vector3.forward * half, p + Vector3.forward * half, c);
        }

        // --- solve helpers ---------------------------------------------------------------------------

        private void CommitOrigin(Vector3 pos, Quaternion rot)
        {
            lastPos = pos;
            lastRot = rot;
            hasLastPose = true;
        }

        // World dimensions of the proxy shape this frame, from its current lossyScale. Unity scales a
        // CapsuleCollider's height by the lossyScale along its direction axis and its radius by the
        // larger of the other two axes; a BoxCollider's size scales component-wise.
        private void ComputeProxyWorldDims()
        {
            sLossyScale = proxyTf.lossyScale;
            Vector3 s = sLossyScale;

            if (proxyKind == ProxyKind.Capsule)
            {
                float radScale, htScale;
                switch (capDirection)
                {
                    case 0: // X
                        sAxisLocal = Vector3.right;
                        radScale = Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z));
                        htScale = Mathf.Abs(s.x);
                        break;
                    case 2: // Z
                        sAxisLocal = Vector3.forward;
                        radScale = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y));
                        htScale = Mathf.Abs(s.z);
                        break;
                    default: // Y
                        sAxisLocal = Vector3.up;
                        radScale = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
                        htScale = Mathf.Abs(s.y);
                        break;
                }
                sRadius = capRadiusLocal * radScale;
                float worldHeight = Mathf.Max(capHeightLocal * htScale, sRadius * 2f);
                sHalfSeg = Mathf.Max(0f, worldHeight * 0.5f - sRadius);
                sHalfExtents = Vector3.zero;
            }
            else
            {
                sAxisLocal = Vector3.up;
                sRadius = 0f;
                sHalfSeg = 0f;
                // component-wise assignment (no Vector3 construction on the solve path)
                sHalfExtents.x = 0.5f * Mathf.Abs(s.x) * boxSizeLocal.x;
                sHalfExtents.y = 0.5f * Mathf.Abs(s.y) * boxSizeLocal.y;
                sHalfExtents.z = 0.5f * Mathf.Abs(s.z) * boxSizeLocal.z;
            }
        }

        // The proxy transform's world position for a hypothetical item pose.
        private Vector3 ProxyWorldPos(Vector3 itemPos, Quaternion itemRot)
        {
            return itemPos + itemRot * proxyPosItemLocal;
        }

        // The proxy shape's world centre (collider.center offset applied) for a proxy world pose.
        private Vector3 ProxyWorldCenter(Vector3 proxyPos, Quaternion proxyRot)
        {
            return proxyPos + proxyRot * Vector3.Scale(proxyCenterLocal, sLossyScale);
        }

        private void CapsuleEnds(Vector3 proxyPos, Quaternion proxyRot, out Vector3 p1, out Vector3 p2)
        {
            Vector3 c = ProxyWorldCenter(proxyPos, proxyRot);
            Vector3 segDir = proxyRot * sAxisLocal;
            p1 = c + segDir * sHalfSeg;
            p2 = c - segDir * sHalfSeg;
        }

        // Fills hitBuf; returns the hit count. Never enables the proxy collider - passes explicit
        // geometry to the sweep.
        private int RawCast(Vector3 proxyPos, Quaternion proxyRot, Vector3 dir, float dist)
        {
            if (proxyKind == ProxyKind.Capsule)
            {
                CapsuleEnds(proxyPos, proxyRot, out Vector3 p1, out Vector3 p2);
                return Physics.CapsuleCastNonAlloc(p1, p2, sRadius, dir, hitBuf, dist, contactMask, QueryTriggerInteraction.Ignore);
            }
            Vector3 center = ProxyWorldCenter(proxyPos, proxyRot);
            return Physics.BoxCastNonAlloc(center, sHalfExtents, dir, hitBuf, proxyRot, dist, contactMask, QueryTriggerInteraction.Ignore);
        }

        private bool RawOverlap(Vector3 proxyPos, Quaternion proxyRot)
        {
            if (proxyKind == ProxyKind.Capsule)
            {
                CapsuleEnds(proxyPos, proxyRot, out Vector3 p1, out Vector3 p2);
                return Physics.CheckCapsule(p1, p2, sRadius, contactMask, QueryTriggerInteraction.Ignore);
            }
            Vector3 center = ProxyWorldCenter(proxyPos, proxyRot);
            return Physics.CheckBox(center, sHalfExtents, proxyRot, contactMask, QueryTriggerInteraction.Ignore);
        }

        // First real forward contact along the segment. Distance-0 "already overlapping at the start"
        // hits carry an unreliable normal (the CastBackoff exists to turn a genuine contact into a
        // small positive distance) so they are skipped here.
        private bool MarchCast(Vector3 proxyPos, Quaternion proxyRot, Vector3 dir, float dist, out RaycastHit hit)
        {
            int count = RawCast(proxyPos, proxyRot, dir, dist);
            hit = default;
            float bestDist = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count && i < hitBuf.Length; i++)
            {
                RaycastHit h = hitBuf[i];
                if (h.collider == null || h.distance <= 0f) continue;
                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    hit = h;
                    found = true;
                }
            }
            return found;
        }

        // The candidate pose: rest rotated by 'degrees' about 'axis' through the pivot. Clear ==
        // the proxy is at least 'skin' from geometry along the contact normal. Backs the sweep off
        // by CastBackoff so an already-overlapping candidate still counts as "not clear".
        private bool ClearAt(float degrees, Vector3 axis, Vector3 pivot, Vector3 restPos, Quaternion restRot, Vector3 normal, float skin)
        {
            Quaternion q = Quaternion.AngleAxis(degrees, axis);
            Vector3 itemPos = pivot + q * (restPos - pivot);
            Quaternion itemRot = q * restRot;

            Vector3 proxyPos = ProxyWorldPos(itemPos, itemRot);
            Quaternion proxyRot = itemRot * proxyRotItemLocal;

            Vector3 castStart = proxyPos + normal * CastBackoff;
            int count = RawCast(castStart, proxyRot, -normal, CastBackoff + skin);
            for (int i = 0; i < count && i < hitBuf.Length; i++)
                if (hitBuf[i].collider != null) return false;
            return true;
        }

        // Farthest point of the proxy shape from the pivot, at the given item pose.
        private Vector3 FarthestProxyPoint(Vector3 itemPos, Quaternion itemRot, Vector3 pivot)
        {
            Vector3 proxyPos = ProxyWorldPos(itemPos, itemRot);
            Quaternion proxyRot = itemRot * proxyRotItemLocal;

            if (proxyKind == ProxyKind.Capsule)
            {
                CapsuleEnds(proxyPos, proxyRot, out Vector3 p1, out Vector3 p2);
                Vector3 far = Vector3.SqrMagnitude(p1 - pivot) >= Vector3.SqrMagnitude(p2 - pivot) ? p1 : p2;
                Vector3 outward = far - pivot;
                float m = outward.magnitude;
                return m > 1e-6f ? far + outward / m * sRadius : far;
            }

            Vector3 center = ProxyWorldCenter(proxyPos, proxyRot);
            Vector3 ex = (proxyRot * Vector3.right) * sHalfExtents.x;
            Vector3 ey = (proxyRot * Vector3.up) * sHalfExtents.y;
            Vector3 ez = (proxyRot * Vector3.forward) * sHalfExtents.z;
            Vector3 best = center;
            float bestSqr = -1f;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        Vector3 corner = center + ex * sx + ey * sy + ez * sz;
                        float d = Vector3.SqrMagnitude(corner - pivot);
                        if (d > bestSqr)
                        {
                            bestSqr = d;
                            best = corner;
                        }
                    }
            return best;
        }

        // Push a raw contact normal into the ring buffer and return the averaged (renormalised)
        // normal over the most recent normalSmoothingFrames entries. Window 1 == passthrough.
        private Vector3 PushSmoothNormal(Vector3 raw)
        {
            int cap = Mathf.Clamp(settings != null ? settings.normalSmoothingFrames : 1, 1, normalBuf.Length);

            normalBuf[normalHead] = raw;
            normalHead = (normalHead + 1) % normalBuf.Length;
            if (normalCount < normalBuf.Length) normalCount++;

            int use = Mathf.Min(normalCount, cap);
            Vector3 sum = Vector3.zero;
            int idx = normalHead;
            for (int i = 0; i < use; i++)
            {
                idx = (idx - 1 + normalBuf.Length) % normalBuf.Length;
                sum += normalBuf[idx];
            }
            return sum.sqrMagnitude > 1e-10f ? sum.normalized : raw;
        }
    }
}
