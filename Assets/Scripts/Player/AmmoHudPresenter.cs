using System;
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
    private string lastStatusText = string.Empty;

    public event Action ViewChanged;

    public bool LegacyOnGuiEnabled { get; private set; } = true;

    public string DisplayText { get; private set; } = "0 / 0";
    public string WeaponNameText { get; private set; } = string.Empty;
    public string FireModeText { get; private set; } = string.Empty;
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
        emptyFeedbackUntil = 0f;

        if (weapon == null)
        {
            DisplayText = "0 / 0";
            WeaponNameText = string.Empty;
            FireModeText = string.Empty;
            NotifyViewChanged();
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

    private void Update()
    {
        string currentStatus = StatusText;

        if (currentStatus == lastStatusText)
        {
            return;
        }

        lastStatusText = currentStatus;
        ViewChanged?.Invoke();
    }

    public void SetLegacyPresentation(bool enabled)
    {
        LegacyOnGuiEnabled = enabled;
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
        WeaponNameText = weapon.WeaponName;
        FireModeText = weapon.FireModeName;
        NotifyViewChanged();
    }

    private void ShowEmptyFeedback()
    {
        emptyFeedbackUntil = Time.unscaledTime + 0.75f;
        Refresh();
    }

    private void OnGUI()
    {
        if (!LegacyOnGuiEnabled)
        {
            return;
        }

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
        GUI.Label(
            new Rect(right - width, bottom - 98f, width, 24f),
            $"{WeaponNameText}  {FireModeText}",
            statusStyle);

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

    private void NotifyViewChanged()
    {
        lastStatusText = StatusText;
        ViewChanged?.Invoke();
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
