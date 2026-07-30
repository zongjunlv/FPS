using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class CityNewTerminalMissionBootstrap : MonoBehaviour
{
    [SerializeField, Min(0.05f)] private float terminalHoldDuration =
        2.5f;
    [SerializeField] private TerminalInterruptionProgressMode
        interruptionProgressMode =
            TerminalInterruptionProgressMode.Reset;
    [SerializeField] private TerminalCompletionMode completionMode =
        TerminalCompletionMode.Silent;
    [SerializeField, Min(1f)] private float alarmRadius = 32f;
    [SerializeField, Range(0.05f, 2f)]
    private float alarmIntensity = 1f;

    public TerminalInteractable Terminal { get; private set; }

    private void Start()
    {
        if (SceneManager.GetActiveScene().name != "CityNew")
        {
            return;
        }

        GameObject terminalObject = GameObject.Find("controlunit");

        if (terminalObject == null)
        {
            return;
        }

        Terminal = terminalObject.GetComponent<TerminalInteractable>();

        if (Terminal == null)
        {
            Terminal =
                terminalObject.AddComponent<TerminalInteractable>();
            Terminal.Configure(
                terminalHoldDuration,
                interruptionProgressMode,
                completionMode,
                alarmRadius,
                alarmIntensity);
        }

        TerminalMissionHudPresenter missionHud =
            GetComponent<TerminalMissionHudPresenter>();

        if (missionHud == null)
        {
            missionHud =
                gameObject.AddComponent<TerminalMissionHudPresenter>();
        }

        missionHud.Bind(Terminal);
    }
}
