using System;
using UnityEngine;

public class WeaponLoadoutController : MonoBehaviour
{
    [SerializeField] private WeaponController[] weapons;
    [SerializeField, Min(0)] private int initialWeaponIndex;
    [SerializeField, Min(0.1f)] private float switchDuration = 0.56f;

    public event Action SwitchStarted;
    public event Action SwitchCompleted;
    public event Action SwitchInterrupted;
    public event Action<WeaponController> WeaponPresentationChanged;
    public event Action<WeaponController> EquippedWeaponChanged;

    public WeaponController CurrentWeapon =>
        weapons[switchState.CurrentIndex];
    public WeaponController DisplayedWeapon =>
        weapons[displayedWeaponIndex];
    public int CurrentIndex => switchState.CurrentIndex;
    public int WeaponCount => weapons.Length;
    public bool IsSwitching => switchState.IsSwitching;
    public float SwitchDuration => switchDuration;

    private WeaponSwitchState switchState;
    private int displayedWeaponIndex;

    private void Awake()
    {
        if (weapons == null || weapons.Length == 0)
        {
            throw new InvalidOperationException(
                "Weapon loadout requires at least one weapon.");
        }

        switchState = new WeaponSwitchState(
            weapons.Length,
            initialWeaponIndex);
        displayedWeaponIndex = switchState.CurrentIndex;

        for (int index = 0; index < weapons.Length; index++)
        {
            weapons[index].gameObject.SetActive(
                index == displayedWeaponIndex);
        }
    }

    private void Update()
    {
        if (!switchState.IsSwitching)
        {
            return;
        }

        int pendingIndex = switchState.PendingIndex;
        bool shouldSwapPresentation =
            displayedWeaponIndex != pendingIndex &&
            switchState.Elapsed + Time.deltaTime >= switchDuration * 0.5f;
        bool completed = switchState.Advance(
            Time.deltaTime,
            switchDuration);

        if (shouldSwapPresentation || completed)
        {
            ShowWeapon(pendingIndex);
        }

        if (!completed)
        {
            return;
        }

        SwitchCompleted?.Invoke();
        EquippedWeaponChanged?.Invoke(CurrentWeapon);
    }

    public bool TrySelect(int targetIndex)
    {
        if (switchState.IsSwitching)
        {
            if (targetIndex == switchState.PendingIndex)
            {
                return false;
            }

            Interrupt();
        }

        if (!switchState.TryBeginSwitch(targetIndex))
        {
            return false;
        }

        CurrentWeapon.CancelReload();
        CurrentWeapon.PlayHolsterFeedback();
        SwitchStarted?.Invoke();
        return true;
    }

    public bool TryCycle(int direction)
    {
        if (direction == 0)
        {
            return false;
        }

        int step = direction > 0 ? 1 : -1;
        int targetIndex =
            (CurrentIndex + step + weapons.Length) % weapons.Length;
        return TrySelect(targetIndex);
    }

    public bool Interrupt()
    {
        int currentIndex = switchState.CurrentIndex;
        bool needsPresentationRestore =
            displayedWeaponIndex != currentIndex;

        if (!switchState.Interrupt())
        {
            return false;
        }

        ShowWeapon(currentIndex);
        if (!needsPresentationRestore)
        {
            CurrentWeapon.PlayUnholsterFeedback();
        }

        SwitchInterrupted?.Invoke();
        return true;
    }

    private void OnDisable()
    {
        if (switchState != null)
        {
            Interrupt();
        }
    }

    private void ShowWeapon(int weaponIndex)
    {
        if (displayedWeaponIndex == weaponIndex &&
            weapons[weaponIndex].gameObject.activeSelf)
        {
            return;
        }

        for (int index = 0; index < weapons.Length; index++)
        {
            weapons[index].gameObject.SetActive(index == weaponIndex);
        }

        displayedWeaponIndex = weaponIndex;
        DisplayedWeapon.PlayUnholsterFeedback();
        WeaponPresentationChanged?.Invoke(DisplayedWeapon);
    }
}
