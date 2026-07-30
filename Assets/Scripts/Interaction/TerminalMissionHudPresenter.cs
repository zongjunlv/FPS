using UnityEngine;

public sealed class TerminalMissionHudPresenter : MonoBehaviour
{
    private TerminalInteractable terminal;
    private PlayerHudVisualProfile profile;
    private GUIStyle titleStyle;
    private GUIStyle objectiveStyle;
    private GUIStyle markerStyle;

    public bool ObjectiveCompleted =>
        terminal != null &&
        terminal.State == TerminalInteractionState.Completed;
    public float DistanceToObjective =>
        terminal != null
            ? Vector3.Distance(
                transform.position,
                terminal.transform.position)
            : 0f;

    public void Bind(TerminalInteractable configuredTerminal)
    {
        terminal = configuredTerminal;
        profile = Resources.Load<PlayerHudVisualProfile>(
            "PlayerHudVisualProfile");
    }

    private void OnGUI()
    {
        if (terminal == null)
        {
            return;
        }

        EnsureStyles();
        float scale = Mathf.Clamp(
            Screen.height / 1080f,
            0.75f,
            1.25f);
        Rect panel = new Rect(
            24f * scale,
            24f * scale,
            330f * scale,
            86f * scale);
        Color previous = GUI.color;
        GUI.color = profile != null
            ? profile.PanelColor
            : new Color(0.02f, 0.03f, 0.04f, 0.85f);
        GUI.DrawTexture(panel, Texture2D.whiteTexture);
        GUI.color = ObjectiveCompleted
            ? new Color(0.2f, 1f, 0.45f)
            : new Color(1f, 0.68f, 0.16f);
        GUI.Label(
            new Rect(
                panel.x + 15f * scale,
                panel.y + 8f * scale,
                panel.width - 30f * scale,
                24f * scale),
            "OBJECTIVE",
            titleStyle);
        GUI.color = Color.white;
        string status = ObjectiveCompleted
            ? "控制终端已接入  1/1"
            : $"接入城市控制终端  0/1   {DistanceToObjective:F0}m";
        GUI.Label(
            new Rect(
                panel.x + 15f * scale,
                panel.y + 34f * scale,
                panel.width - 30f * scale,
                38f * scale),
            status,
            objectiveStyle);
        DrawWorldMarker(scale);
        GUI.color = previous;
    }

    private void DrawWorldMarker(float scale)
    {
        if (ObjectiveCompleted || Camera.main == null)
        {
            return;
        }

        Vector3 worldPosition =
            terminal.transform.position + Vector3.up * 1.4f;
        Vector3 screenPoint =
            Camera.main.WorldToScreenPoint(worldPosition);

        if (screenPoint.z <= 0f)
        {
            return;
        }

        GUI.color = new Color(1f, 0.68f, 0.16f);
        GUI.Label(
            new Rect(
                screenPoint.x - 55f * scale,
                Screen.height - screenPoint.y - 16f * scale,
                110f * scale,
                32f * scale),
            $"◆ {DistanceToObjective:F0}m",
            markerStyle);
    }

    private void EnsureStyles()
    {
        if (titleStyle != null)
        {
            return;
        }

        float scale = Mathf.Clamp(
            Screen.height / 1080f,
            0.75f,
            1.25f);
        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(13f * scale),
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
        objectiveStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(18f * scale),
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
        markerStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.RoundToInt(15f * scale),
            fontStyle = FontStyle.Bold,
            normal =
            {
                textColor = new Color(1f, 0.68f, 0.16f)
            }
        };

        if (profile != null && profile.Font != null)
        {
            titleStyle.font = profile.Font;
            objectiveStyle.font = profile.Font;
            markerStyle.font = profile.Font;
        }
    }
}
