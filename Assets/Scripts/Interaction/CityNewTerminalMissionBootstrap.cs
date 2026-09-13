using System.Collections;
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

        GameObject terminalObject =
            CityNewModularLayoutBootstrap.Active?.TerminalObject ??
            GameObject.Find("controlunit");

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
        StartCoroutine(BootstrapMission());
    }

    private IEnumerator BootstrapMission()
    {
        yield return null;
        Vector3 extractionPosition =
            new Vector3(48.414f, 0.05f, 41.41f);
        if (CityNewModularLayoutBootstrap.Active != null)
            extractionPosition =
                CityNewModularLayoutBootstrap.Active.ExtractionPoint;
        GameObject extractionAnchor = GameObject.Find("Point light (1)");

        if (CityNewModularLayoutBootstrap.Active == null &&
            extractionAnchor != null)
        {
            extractionPosition.x = extractionAnchor.transform.position.x;
            extractionPosition.z = extractionAnchor.transform.position.z;
        }

        if (Physics.Raycast(
                extractionPosition + Vector3.up * 10f,
                Vector3.down,
                out RaycastHit groundHit,
                30f,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore))
        {
            extractionPosition.y = groundHit.point.y + 0.04f;
        }

        CityNewMissionController mission =
            GetComponent<CityNewMissionController>();

        if (mission == null)
        {
            mission = gameObject.AddComponent<CityNewMissionController>();
        }

        if (WaveDirector.Active != null)
        {
            mission.ConfigureWave(
                Terminal,
                WaveDirector.Active,
                extractionPosition);
            if (RunSnapshotSession.HasPendingWorldRestore &&
                !RunSnapshotSession.TryRestoreMission(
                    mission,
                    out string restoreError))
            {
                Debug.LogError(
                    "CityNew mission restore failed: " + restoreError,
                    this);
            }
            yield break;
        }

        EnemyController[] enemies =
            FindObjectsByType<EnemyController>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

        if (enemies.Length == 0)
        {
            yield break;
        }

        EnemyController targetEnemy = enemies[0];

        foreach (EnemyController enemy in enemies)
        {
            if (enemy.name == "SPIDER_BOT")
            {
                targetEnemy = enemy;
                break;
            }
        }

        Health targetHealth = targetEnemy.GetComponent<Health>();

        if (targetHealth == null)
        {
            yield break;
        }

        mission.Configure(Terminal, targetHealth, extractionPosition);
    }
}
