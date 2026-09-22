using System.Collections;
using FPS.Core.GameModes;
using UnityEngine;

/// <summary>
/// Scene-owned, phase-aligned music layers for the CityNew battle scene.
/// The object is destroyed with the scene, so returning to a menu cannot
/// leave music playing or create a second copy on the next run.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleMusicController : MonoBehaviour
{
    public const string CalmResourcePath = "Audio/Music/Singularity_calm";
    public const string CombatResourcePath = "Audio/Music/Singularity_action";
    public const string VolumePreferenceKey = "fps.battle_music.volume.v1";
    public const float DefaultVolume = 0.18f;

    private const float FadeSeconds = 2f;
    private AudioSource calmSource;
    private AudioSource combatSource;
    private float combatBlend;

    public static BattleMusicController Active { get; private set; }

    public static float MusicVolume => Mathf.Clamp01(
        PlayerPrefs.GetFloat(VolumePreferenceKey, DefaultVolume));

    public static void SetMusicVolume(float volume)
    {
        PlayerPrefs.SetFloat(VolumePreferenceKey, Mathf.Clamp01(volume));
        Active?.ApplyMix();
    }

    public static bool ShouldInstallForScene(
        GameModeStage stage, bool activated, bool batchMode,
        bool dedicatedServer)
    {
        return activated && stage == GameModeStage.Battle &&
               !batchMode && !dedicatedServer;
    }

    public static bool ShouldUseCombatLayer(
        GameModeId mode, WaveRunPhase phase)
    {
        // The cooperative scene uses its own networked wave runtime rather
        // than the solo WaveDirector, so keep its PvE music on the action layer.
        return mode == GameModeId.Coop ||
               phase == WaveRunPhase.Spawning ||
               phase == WaveRunPhase.Fighting;
    }

    private void Awake()
    {
        if (Active != null && Active != this)
        {
            Destroy(this);
            return;
        }

        Active = this;
    }

    private IEnumerator Start()
    {
        AudioClip calm = Resources.Load<AudioClip>(CalmResourcePath);
        AudioClip combat = Resources.Load<AudioClip>(CombatResourcePath);
        if (calm == null || combat == null)
        {
            Debug.LogWarning("[BattleMusic] Missing Singularity music clips.", this);
            yield break;
        }

        calm.LoadAudioData();
        combat.LoadAudioData();
        float deadline = Time.realtimeSinceStartup + 10f;
        while ((calm.loadState == AudioDataLoadState.Loading ||
                combat.loadState == AudioDataLoadState.Loading) &&
               Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        if (calm.loadState != AudioDataLoadState.Loaded ||
            combat.loadState != AudioDataLoadState.Loaded)
        {
            Debug.LogWarning("[BattleMusic] Music stream failed to load.", this);
            yield break;
        }

        calmSource = CreateLayer(calm);
        combatSource = CreateLayer(combat);
        combatBlend = ShouldUseCombatLayer(
            GameModeContext.CurrentMode,
            WaveDirector.Active != null
                ? WaveDirector.Active.Phase
                : WaveRunPhase.Idle) ? 1f : 0f;
        ApplyMix();

        // Starting both clips on one DSP boundary keeps the two arrangements
        // aligned while their relative volumes change during a wave.
        double startTime = AudioSettings.dspTime + 0.25d;
        calmSource.PlayScheduled(startTime);
        combatSource.PlayScheduled(startTime);
    }

    private void Update()
    {
        if (calmSource == null || combatSource == null)
            return;

        WaveRunPhase phase = WaveDirector.Active != null
            ? WaveDirector.Active.Phase
            : WaveRunPhase.Idle;
        float target = ShouldUseCombatLayer(GameModeContext.CurrentMode, phase)
            ? 1f : 0f;
        combatBlend = Mathf.MoveTowards(
            combatBlend, target, Time.unscaledDeltaTime / FadeSeconds);
        ApplyMix();
    }

    private void OnDestroy()
    {
        if (Active == this)
            Active = null;
    }

    private AudioSource CreateLayer(AudioClip clip)
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
        source.bypassReverbZones = true;
        source.priority = 64;
        source.volume = 0f;
        return source;
    }

    private void ApplyMix()
    {
        if (calmSource == null || combatSource == null)
            return;

        float gain = MusicVolume;
        float angle = combatBlend * Mathf.PI * 0.5f;
        calmSource.volume = gain * Mathf.Cos(angle);
        combatSource.volume = gain * Mathf.Sin(angle);
    }
}
