using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using CorruptedCourt.Core;
using CorruptedCourt.Tasks;

namespace CorruptedCourt.Gameplay
{
    // Owns the Corrupted team's ONE active global sabotage. Any Corrupted-faction player triggers one by
    // interacting with its physical origin (a Brazier / the PoisonCauldron); the cooldown here is shared
    // across the whole team so several Corrupted cannot chain sabotages. Each sabotage auto-resolves
    // after sabotageDuration if the Court never fixes it, and the Court's counterplay ends it early.
    public class SabotageManager : MonoBehaviour
    {
        public static SabotageManager Instance { get; private set; }

        public enum GlobalSabotage { None, PoisonTheFeast, DouseTheBraziers }

        [Header("Team Sabotage Rules")]
        [Tooltip("Seconds an active global sabotage lasts before it auto-resolves on its own if the " +
                 "Court never fixes it. The trigger alert counts this down.")]
        [SerializeField] private float sabotageDuration = 45f;
        [Tooltip("Shared Corrupted cooldown after a sabotage ends. No Corrupted-faction player can start " +
                 "another global sabotage until it elapses.")]
        [SerializeField] private float teamCooldown = 60f;

        [Header("Poison the Feast")]
        [Tooltip("Court meter points drained per second while Poison the Feast is active.")]
        [SerializeField] private float poisonDrainPerSecond = 0.5f;
        [Tooltip("The antidote item spawned when Poison the Feast triggers. Its ItemDefinition is what " +
                 "the PoisonCauldron accepts as the cure. Leave empty to disable the item cure (only the " +
                 "auto-resolve timer will end it).")]
        [SerializeField] private PickupItem antidotePrefab;
        [Tooltip("Where the antidote appears when Poison the Feast triggers (an apothecary bench, say). " +
                 "Keep it well away from the cauldron so curing needs a real trip. Falls back to this " +
                 "manager's own position when unset.")]
        [SerializeField] private Transform antidoteSpawnPoint;

        [Header("Douse the Braziers")]
        [Tooltip("How many separate braziers the Court must relight to lift the darkness. Clamped down " +
                 "to the number of braziers actually in the scene.")]
        [SerializeField] private int relightsToCure = 2;
        [Tooltip("Scene lights switched off while the braziers are doused (assign the sun / key room " +
                 "lights). Each light's enabled state is restored when the sabotage ends.")]
        [SerializeField] private Light[] mapLights;
        [Tooltip("Flat ambient colour forced while the braziers are doused. The scene's original ambient " +
                 "mode and colour are restored when the sabotage ends.")]
        [SerializeField] private Color dousedAmbient = new Color(0.02f, 0.02f, 0.03f, 1f);

        public GlobalSabotage Active { get; private set; } = GlobalSabotage.None;
        public bool IsPoisonActive => Active == GlobalSabotage.PoisonTheFeast;
        public bool IsDouseActive => Active == GlobalSabotage.DouseTheBraziers;

        /// <summary>Read by the trigger objects' prompts: true when a Corrupted could start a new global
        /// sabotage right now (none running, team cooldown elapsed).</summary>
        public bool SabotageAvailable => Active == GlobalSabotage.None && cooldownTimer <= 0f;

        private float activeTimer;
        private float cooldownTimer;
        private int lastSecondBroadcast = -1;
        private float drainTickTimer;
        private int neededRelights;
        private PickupItem spawnedAntidote;
        private readonly HashSet<Brazier> relitBraziers = new HashSet<Brazier>();

        // Lighting snapshot, taken when darkness is applied and restored when it lifts.
        private bool lightingCached;
        private AmbientMode cachedAmbientMode;
        private Color cachedAmbientLight;
        private bool[] mapLightEnabledCache;

        void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        void OnEnable()
        {
            GameEvents.MatchStateChanged += OnMatchStateChanged;
        }

        void OnDisable()
        {
            GameEvents.MatchStateChanged -= OnMatchStateChanged;
        }

        void OnDestroy()
        {
            // Never leave the scene dark if this manager is torn down mid-sabotage.
            if (lightingCached) RestoreLighting();
        }

        // Copies sabotage-balance tunables from the MatchConfig asset. Called by MatchManager at
        // match start. No-op if cfg is null.
        public void ApplyConfig(MatchConfig cfg)
        {
            if (cfg == null) return;
            MatchConfig.ApplyFloat(nameof(SabotageManager), nameof(sabotageDuration), ref sabotageDuration, cfg.sabotageDuration);
            MatchConfig.ApplyFloat(nameof(SabotageManager), nameof(teamCooldown), ref teamCooldown, cfg.sabotageTeamCooldown);
            MatchConfig.ApplyFloat(nameof(SabotageManager), nameof(poisonDrainPerSecond), ref poisonDrainPerSecond, cfg.poisonDrainPerSecond);
        }

        void Update()
        {
            if (cooldownTimer > 0f) cooldownTimer -= Time.deltaTime;

            if (Active == GlobalSabotage.None) return;

            activeTimer -= Time.deltaTime;

            if (Active == GlobalSabotage.PoisonTheFeast)
            {
                // Drain once per whole second so the win check (which reacts to CourtProgressChanged)
                // runs ~1x/s instead of every frame. The fractional remainder is carried, so the total
                // drained is exactly poisonDrainPerSecond x elapsed seconds.
                drainTickTimer += Time.deltaTime;
                if (drainTickTimer >= 1f)
                {
                    int wholeSeconds = Mathf.FloorToInt(drainTickTimer);
                    drainTickTimer -= wholeSeconds;
                    if (TaskManager.Instance != null)
                        TaskManager.Instance.DrainCourtProgress(poisonDrainPerSecond * wholeSeconds);
                }
            }

            int secondsLeft = Mathf.CeilToInt(Mathf.Max(0f, activeTimer));
            if (secondsLeft != lastSecondBroadcast)
            {
                lastSecondBroadcast = secondsLeft;
                GameEvents.RaiseGlobalSabotageTick(secondsLeft);
            }

            if (activeTimer <= 0f) ResolveActive(true);
        }

        // --- TRIGGERS (called by the physical origin objects: PoisonCauldron / Brazier) ---

        public bool TryTriggerPoison()
        {
            if (!CanTrigger()) return false;

            BeginSabotage(GlobalSabotage.PoisonTheFeast);
            SpawnAntidote();
            return true;
        }

        public bool TryTriggerDouse()
        {
            if (!CanTrigger()) return false;

            if (Brazier.AllBraziers.Count == 0)
            {
                Log.Warn("[SabotageManager] Douse the Braziers triggered but there are no Brazier objects in the scene.");
                return false;
            }

            BeginSabotage(GlobalSabotage.DouseTheBraziers);
            neededRelights = Mathf.Clamp(relightsToCure, 1, Brazier.AllBraziers.Count);
            relitBraziers.Clear();
            ApplyDarkness();
            return true;
        }

        // A global sabotage can only start during live play, when none is running and the team cooldown
        // has elapsed.
        private bool CanTrigger()
        {
            if (Active != GlobalSabotage.None) return false;
            if (cooldownTimer > 0f) return false;
            return MatchManager.Instance != null
                   && MatchManager.Instance.currentState == MatchManager.MatchState.ActionStage;
        }

        private void BeginSabotage(GlobalSabotage kind)
        {
            Active = kind;
            activeTimer = sabotageDuration;
            lastSecondBroadcast = -1;
            drainTickTimer = 0f;

            Log.Game($"<color=#8E44AD>--- GLOBAL SABOTAGE: {Label(kind)} ---</color>");
            GameEvents.RaiseGlobalSabotageStarted(Label(kind), Mathf.CeilToInt(sabotageDuration));
        }

        // --- COURT COUNTERPLAY (called by the counter objects) ---

        // Returns true when the courier was carrying the antidote and it was consumed to cure the poison.
        public bool TryDeliverAntidote(PlayerController courier)
        {
            if (Active != GlobalSabotage.PoisonTheFeast) return false;
            if (antidotePrefab == null || antidotePrefab.definition == null) return false;

            PickupItem held = courier != null ? courier.GetHeldItem() : null;
            if (held == null || held.definition != antidotePrefab.definition) return false;

            courier.ClearHeldItem();
            if (spawnedAntidote == held) spawnedAntidote = null;
            Destroy(held.gameObject);

            Log.Game($"{courier.gameObject.name} administered the antidote - the feast is safe.");
            ResolveActive(false);
            return true;
        }

        public void NotifyBrazierRelit(Brazier brazier)
        {
            if (Active != GlobalSabotage.DouseTheBraziers || brazier == null) return;

            if (relitBraziers.Add(brazier))
                Log.Game($"Brazier relit ({relitBraziers.Count}/{neededRelights}).");

            if (relitBraziers.Count >= neededRelights) ResolveActive(false);
        }

        // --- RESOLUTION ---

        private void ResolveActive(bool auto)
        {
            if (Active == GlobalSabotage.None) return;
            GlobalSabotage ending = Active;

            if (ending == GlobalSabotage.DouseTheBraziers) RestoreLighting();

            if (ending == GlobalSabotage.PoisonTheFeast && spawnedAntidote != null)
            {
                Destroy(spawnedAntidote.gameObject);
                spawnedAntidote = null;
            }

            Active = GlobalSabotage.None;
            activeTimer = 0f;
            cooldownTimer = teamCooldown;
            relitBraziers.Clear();

            Log.Game($"<color=#2ECC71>--- SABOTAGE RESOLVED: {Label(ending)} ({(auto ? "expired" : "fixed by the Court")}) ---</color>");
            GameEvents.RaiseGlobalSabotageEnded(Label(ending), auto);
        }

        private void OnMatchStateChanged(MatchManager.MatchState state)
        {
            // Sabotages don't carry across a meeting or into game over.
            if (state != MatchManager.MatchState.ActionStage && Active != GlobalSabotage.None)
                ResolveActive(true);
        }

        // --- EFFECTS ---

        private void SpawnAntidote()
        {
            if (antidotePrefab == null)
            {
                Log.Warn("[SabotageManager] No antidotePrefab assigned - Poison the Feast has no item cure.");
                return;
            }

            Vector3 pos = antidoteSpawnPoint != null ? antidoteSpawnPoint.position : transform.position;
            Quaternion rot = antidoteSpawnPoint != null ? antidoteSpawnPoint.rotation : Quaternion.identity;

            spawnedAntidote = Instantiate(antidotePrefab, pos, rot);
            spawnedAntidote.isInfiniteSource = false;
        }

        private void ApplyDarkness()
        {
            CacheLighting();

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = dousedAmbient;

            if (mapLights != null)
                for (int i = 0; i < mapLights.Length; i++)
                    if (mapLights[i] != null) mapLights[i].enabled = false;

            for (int i = 0; i < Brazier.AllBraziers.Count; i++)
                if (Brazier.AllBraziers[i] != null) Brazier.AllBraziers[i].SetLit(false);
        }

        private void CacheLighting()
        {
            if (lightingCached) return;

            cachedAmbientMode = RenderSettings.ambientMode;
            cachedAmbientLight = RenderSettings.ambientLight;

            if (mapLights != null)
            {
                mapLightEnabledCache = new bool[mapLights.Length];
                for (int i = 0; i < mapLights.Length; i++)
                    mapLightEnabledCache[i] = mapLights[i] != null && mapLights[i].enabled;
            }

            lightingCached = true;
        }

        private void RestoreLighting()
        {
            if (!lightingCached) return;

            RenderSettings.ambientMode = cachedAmbientMode;
            RenderSettings.ambientLight = cachedAmbientLight;

            if (mapLights != null && mapLightEnabledCache != null)
                for (int i = 0; i < mapLights.Length && i < mapLightEnabledCache.Length; i++)
                    if (mapLights[i] != null) mapLights[i].enabled = mapLightEnabledCache[i];

            for (int i = 0; i < Brazier.AllBraziers.Count; i++)
                if (Brazier.AllBraziers[i] != null) Brazier.AllBraziers[i].SetLit(true);

            lightingCached = false;
        }

        private static string Label(GlobalSabotage kind)
        {
            switch (kind)
            {
                case GlobalSabotage.PoisonTheFeast: return "Poison the Feast";
                case GlobalSabotage.DouseTheBraziers: return "Douse the Braziers";
                default: return "None";
            }
        }
    }
}
