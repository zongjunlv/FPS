using UnityEngine;

public class AmmoHudPresenter : MonoBehaviour
{
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color emptyColor =
        new Color(1f, 0.25f, 0.2f, 1f);

    private WeaponController weapon;
    private GUIStyle ammoStyle;
    private GUIStyle statusStyle;
    private float emptyFeedbackUntil;

    public string DisplayText { get; private set; } = "0 / 0";
    public string StatusText
    {
        get
        {
            if (weapon != null && weapon.IsReloading)
            {
                return "RELOADING";
            }

            return Time.unscaledTime < emptyFeedbackUntil
                ? "EMPTY"
                : string.Empty;
        }
    }

    public void Bind(WeaponController targetWeapon)
    {
        Unbind();
        weapon = targetWeapon;

        if (weapon == null)
        {
            DisplayText = "0 / 0";
            return;
        }

        weapon.AmmoChanged += Refresh;
        weapon.DryFired += ShowEmptyFeedback;
        weapon.ReloadStateChanged += Refresh;
        Refresh();
    }

    private void OnDestroy()
    {
        Unbind();
    }

    private void Unbind()
    {
        if (weapon == null)
        {
            return;
        }

        weapon.AmmoChanged -= Refresh;
        weapon.DryFired -= ShowEmptyFeedback;
        weapon.ReloadStateChanged -= Refresh;
        weapon = null;
    }

    private void Refresh()
    {
        if (weapon == null)
        {
            return;
        }

        DisplayText = $"{weapon.CurrentAmmo} / {weapon.ReserveAmmo}";
    }

    private void ShowEmptyFeedback()
    {
        emptyFeedbackUntil = Time.unscaledTime + 0.75f;
        Refresh();
    }

    private void OnGUI()
    {
        EnsureStyles();

        float width = 240f;
        float right = Screen.width - 32f;
        float bottom = Screen.height - 28f;
        ammoStyle.normal.textColor =
            weapon != null && weapon.CurrentAmmo == 0
                ? emptyColor
                : normalColor;

        GUI.Label(
            new Rect(right - width, bottom - 44f, width, 40f),
            DisplayText,
            ammoStyle);

        string status = StatusText;

        if (!string.IsNullOrEmpty(status))
        {
            statusStyle.normal.textColor =
                status == "EMPTY" ? emptyColor : normalColor;
            GUI.Label(
                new Rect(right - width, bottom - 72f, width, 28f),
                status,
                statusStyle);
        }
    }

    private void EnsureStyles()
    {
        if (ammoStyle != null)
        {
            return;
        }

        ammoStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleRight,
            fontSize = 28,
            fontStyle = FontStyle.Bold
        };
        statusStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleRight,
            fontSize = 16,
            fontStyle = FontStyle.Bold
        };
    }
}
