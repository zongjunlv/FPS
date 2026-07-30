using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[RequireComponent(
    typeof(Health),
    typeof(PlayerController),
    typeof(PlayerCombatController))]
public sealed class PlayerFailureFlowController : MonoBehaviour
{
    private Health health;
    private PlayerController player;
    private PlayerCombatController combat;
    private PlayerHudVisualProfile profile;
    private GUIStyle titleStyle;
    private GUIStyle bodyStyle;
    private GUIStyle buttonStyle;

    public bool IsFailed { get; private set; }
    public bool IsRestarting { get; private set; }

    private void Start()
    {
        health = GetComponent<Health>();
        player = GetComponent<PlayerController>();
        combat = GetComponent<PlayerCombatController>();
        profile = Resources.Load<PlayerHudVisualProfile>(
            "PlayerHudVisualProfile");
        health.Died += HandleDeath;
    }

    private void Update()
    {
        if (IsFailed &&
            !IsRestarting &&
            Keyboard.current != null &&
            Keyboard.current.rKey.wasPressedThisFrame)
        {
            RestartLevel();
        }
    }

    private void OnDestroy()
    {
        if (health != null)
        {
            health.Died -= HandleDeath;
        }
    }

    public bool RestartLevel()
    {
        if (!IsFailed || IsRestarting)
        {
            return false;
        }

        IsRestarting = true;
        Time.timeScale = 1f;
        Scene activeScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(activeScene.name);
        return true;
    }

    private void HandleDeath()
    {
        if (IsFailed)
        {
            return;
        }

        IsFailed = true;
        player.SetGameplayInputEnabled(false);
        combat.SetGameplayInputEnabled(false);
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void OnGUI()
    {
        if (!IsFailed)
        {
            return;
        }

        EnsureStyles();
        GUI.depth = -100;
        Color previous = GUI.color;
        GUI.color = new Color(0.02f, 0.025f, 0.03f, 0.9f);
        GUI.DrawTexture(
            new Rect(0f, 0f, Screen.width, Screen.height),
            Texture2D.whiteTexture);
        GUI.color = Color.white;
        float width = Mathf.Min(620f, Screen.width * 0.78f);
        float height = 300f;
        Rect panel = new Rect(
            (Screen.width - width) * 0.5f,
            (Screen.height - height) * 0.5f,
            width,
            height);
        GUI.color = profile != null
            ? profile.PanelColor
            : new Color(0f, 0f, 0f, 0.88f);
        GUI.DrawTexture(panel, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(
            new Rect(panel.x, panel.y + 42f, width, 72f),
            "MISSION FAILED",
            titleStyle);
        GUI.Label(
            new Rect(panel.x, panel.y + 120f, width, 42f),
            "作战人员已失去生命体征",
            bodyStyle);
        Rect button = new Rect(
            panel.center.x - 150f,
            panel.yMax - 92f,
            300f,
            52f);

        if (GUI.Button(button, "重新开始  [R]", buttonStyle))
        {
            RestartLevel();
        }

        GUI.color = previous;
    }

    private void EnsureStyles()
    {
        if (titleStyle != null)
        {
            return;
        }

        Font font = profile != null ? profile.Font : null;
        Color danger = profile != null
            ? profile.HealthColor
            : new Color(0.92f, 0.2f, 0.18f);
        titleStyle = new GUIStyle(GUI.skin.label)
        {
            font = font,
            fontSize = 42,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = danger }
        };
        bodyStyle = new GUIStyle(titleStyle)
        {
            fontSize = 20,
            fontStyle = FontStyle.Normal,
            normal = { textColor = Color.white }
        };
        buttonStyle = new GUIStyle(GUI.skin.button)
        {
            font = font,
            fontSize = 20,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
    }
}
