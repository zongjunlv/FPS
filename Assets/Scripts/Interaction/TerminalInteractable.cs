using System;
using UnityEngine;

public enum TerminalInterruptionProgressMode
{
    Reset,
    Preserve
}

public enum TerminalCompletionMode
{
    Silent,
    AreaAlarm
}

public sealed class TerminalInteractable :
    MonoBehaviour,
    IInteractable
{
    [SerializeField, Min(0.05f)] private float holdDuration = 2.5f;
    [SerializeField] private TerminalInterruptionProgressMode
        interruptionProgressMode =
            TerminalInterruptionProgressMode.Reset;
    [SerializeField] private TerminalCompletionMode completionMode =
        TerminalCompletionMode.Silent;
    [SerializeField, Min(1f)] private float alarmRadius = 32f;
    [SerializeField, Range(0.05f, 2f)]
    private float alarmIntensity = 1f;

    private readonly TerminalInteractionStateMachine stateMachine =
        new();
    private CombatSoundEventChannel soundEvents;
    private GameObject activeActor;
    private AudioSource audioSource;
    private AudioClip completionClip;
    private Renderer statusLight;
    private Material statusMaterial;

    public event Action<TerminalInteractable> Completed;

    public TerminalInteractionState State => stateMachine.State;
    public float ProgressNormalized =>
        stateMachine.ProgressNormalized;
    public int CompletionCount { get; private set; }
    public InteractionView View => new(
        State == TerminalInteractionState.Completed
            ? "终端接入完成"
            : "[E] 长按接入终端",
        ProgressNormalized,
        State != TerminalInteractionState.Completed,
        State == TerminalInteractionState.Completed);

    private void Awake()
    {
        soundEvents =
            Resources.Load<CombatSoundEventChannel>(
                "CombatSoundEvents");
        completionClip = Resources.Load<AudioClip>("check");
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;
        audioSource.minDistance = 1f;
        audioSource.maxDistance = 18f;
        ConfigureStateMachine();
    }

    private void Start()
    {
        CreateStatusLight();
        UpdateStatusLight();
    }

    private void Update()
    {
        UpdateStatusLight();
    }

    private void OnDestroy()
    {
        if (statusMaterial != null)
        {
            Destroy(statusMaterial);
        }
    }

    public void Configure(
        float configuredHoldDuration,
        TerminalInterruptionProgressMode configuredProgressMode,
        TerminalCompletionMode configuredCompletionMode,
        float configuredAlarmRadius,
        float configuredAlarmIntensity)
    {
        holdDuration = Mathf.Max(
            0.05f,
            configuredHoldDuration);
        interruptionProgressMode = configuredProgressMode;
        completionMode = configuredCompletionMode;
        alarmRadius = Mathf.Max(1f, configuredAlarmRadius);
        alarmIntensity = Mathf.Clamp(
            configuredAlarmIntensity,
            0.05f,
            2f);
        activeActor = null;
        CompletionCount = 0;
        ConfigureStateMachine();
        UpdateStatusLight();
    }

    public bool TryBegin(GameObject actor)
    {
        if (actor == null ||
            State == TerminalInteractionState.Completed)
        {
            return false;
        }

        if (activeActor != null && activeActor != actor)
        {
            return false;
        }

        activeActor = actor;
        return stateMachine.TryBegin();
    }

    public bool Advance(GameObject actor, float deltaTime)
    {
        if (actor == null ||
            activeActor != actor ||
            !stateMachine.Advance(deltaTime))
        {
            return false;
        }

        activeActor = null;
        CompletionCount++;
        PublishCompletion();
        Completed?.Invoke(this);
        return true;
    }

    public bool Cancel(
        GameObject actor,
        InteractionCancelReason reason)
    {
        if (actor == null || activeActor != actor)
        {
            return false;
        }

        bool cancelled = stateMachine.Cancel();

        if (cancelled)
        {
            activeActor = null;
        }

        return cancelled;
    }

    private void ConfigureStateMachine()
    {
        stateMachine.Configure(
            holdDuration,
            interruptionProgressMode ==
                TerminalInterruptionProgressMode.Preserve);
    }

    private void PublishCompletion()
    {
        if (completionMode == TerminalCompletionMode.AreaAlarm &&
            soundEvents != null)
        {
            soundEvents.Publish(
                new SoundStimulus(
                    transform.position,
                    alarmRadius,
                    alarmIntensity,
                    gameObject));
        }

        if (completionClip != null)
        {
            audioSource.PlayOneShot(completionClip);
        }
    }

    private void CreateStatusLight()
    {
        Renderer terminalRenderer = GetComponent<Renderer>();

        if (terminalRenderer == null || statusLight != null)
        {
            return;
        }

        GameObject lightObject =
            GameObject.CreatePrimitive(PrimitiveType.Sphere);
        lightObject.name = "Terminal Status Light";
        lightObject.transform.SetParent(transform, true);
        lightObject.transform.position =
            terminalRenderer.bounds.center +
            Vector3.up * (terminalRenderer.bounds.extents.y + 0.18f);
        Vector3 parentScale = transform.lossyScale;
        lightObject.transform.localScale = new Vector3(
            0.16f / Mathf.Max(0.001f, Mathf.Abs(parentScale.x)),
            0.16f / Mathf.Max(0.001f, Mathf.Abs(parentScale.y)),
            0.16f / Mathf.Max(0.001f, Mathf.Abs(parentScale.z)));
        Collider lightCollider = lightObject.GetComponent<Collider>();

        if (lightCollider != null)
        {
            Destroy(lightCollider);
        }

        statusLight = lightObject.GetComponent<Renderer>();
        Shader shader = Shader.Find(
            "Universal Render Pipeline/Unlit");
        shader ??= Shader.Find("Unlit/Color");

        if (shader != null)
        {
            statusMaterial = new Material(shader);
            statusLight.material = statusMaterial;
        }
    }

    private void UpdateStatusLight()
    {
        if (statusLight == null)
        {
            return;
        }

        Color color = State switch
        {
            TerminalInteractionState.Interacting =>
                Color.Lerp(
                    new Color(0.1f, 0.45f, 1f),
                    Color.cyan,
                    ProgressNormalized),
            TerminalInteractionState.Completed =>
                new Color(0.15f, 1f, 0.35f),
            _ => new Color(1f, 0.55f, 0.08f)
        };
        statusLight.material.color = color;
    }
}
