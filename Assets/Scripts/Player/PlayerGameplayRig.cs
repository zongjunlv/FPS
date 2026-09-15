using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerGameplayRig : MonoBehaviour
{
    public const string ResourcesPath = "Player/PlayerGameplayRig";

    [Header("Core Gameplay")]
    [SerializeField] private PlayerController player;
    [SerializeField] private PlayerInputReader input;
    [SerializeField] private PlayerAnimatorController animator;
    [SerializeField] private PlayerRecoilController recoil;
    [SerializeField] private PlayerCombatController combat;
    [SerializeField] private WeaponLoadoutController loadout;
    [SerializeField] private ShotTracerPool tracerPool;
    [SerializeField] private PlayerCombatCompositionRoot compositionRoot;

    [Header("Presentation")]
    [SerializeField] private Camera firstPersonCamera;
    [SerializeField] private WeaponController[] weapons =
        Array.Empty<WeaponController>();

    public PlayerController Player => player;
    public PlayerInputReader Input => input;
    public PlayerAnimatorController Animator => animator;
    public PlayerRecoilController Recoil => recoil;
    public PlayerCombatController Combat => combat;
    public WeaponLoadoutController Loadout => loadout;
    public ShotTracerPool TracerPool => tracerPool;
    public PlayerCombatCompositionRoot CompositionRoot => compositionRoot;
    public Camera FirstPersonCamera => firstPersonCamera;
    public IReadOnlyList<WeaponController> Weapons => weapons;

    public static PlayerGameplayRig LoadPrefab()
    {
        return Resources.Load<PlayerGameplayRig>(ResourcesPath);
    }

    public static PlayerGameplayRig Create(
        Vector3 position,
        Quaternion rotation)
    {
        PlayerGameplayRig prefab = LoadPrefab();

        if (prefab == null)
        {
            throw new InvalidOperationException(
                $"Player gameplay rig is missing at Resources/{ResourcesPath}.");
        }

        return Instantiate(prefab, position, rotation);
    }

    public void RefreshReferences()
    {
        player = GetComponent<PlayerController>();
        input = GetComponent<PlayerInputReader>();
        animator = GetComponent<PlayerAnimatorController>();
        recoil = GetComponent<PlayerRecoilController>();
        combat = GetComponent<PlayerCombatController>();
        loadout = GetComponent<WeaponLoadoutController>();
        tracerPool = GetComponent<ShotTracerPool>();
        compositionRoot = GetComponent<PlayerCombatCompositionRoot>();
        firstPersonCamera = GetComponentInChildren<Camera>(true);
        weapons = GetComponentsInChildren<WeaponController>(true);
    }

    public bool TryValidate(out string error)
    {
        var missing = new List<string>();
        AddMissing(player, nameof(PlayerController), missing);
        AddMissing(input, nameof(PlayerInputReader), missing);
        AddMissing(animator, nameof(PlayerAnimatorController), missing);
        AddMissing(recoil, nameof(PlayerRecoilController), missing);
        AddMissing(combat, nameof(PlayerCombatController), missing);
        AddMissing(loadout, nameof(WeaponLoadoutController), missing);
        AddMissing(tracerPool, nameof(ShotTracerPool), missing);
        AddMissing(compositionRoot, nameof(PlayerCombatCompositionRoot), missing);
        AddMissing(firstPersonCamera, nameof(Camera), missing);

        if (missing.Count > 0)
        {
            error = $"Missing required rig dependencies: {string.Join(", ", missing)}.";
            return false;
        }

        if (player.gameObject != gameObject || input.gameObject != gameObject ||
            animator.gameObject != gameObject || recoil.gameObject != gameObject ||
            combat.gameObject != gameObject || loadout.gameObject != gameObject ||
            tracerPool.gameObject != gameObject ||
            compositionRoot.gameObject != gameObject)
        {
            error = "Core gameplay components must live on the rig root.";
            return false;
        }

        if (!firstPersonCamera.transform.IsChildOf(transform))
        {
            error = "The first-person camera must belong to the rig hierarchy.";
            return false;
        }

        if (weapons == null || weapons.Length != 2)
        {
            error = "The current gameplay rig must contain the AR and pistol.";
            return false;
        }

        if (loadout.WeaponCount != weapons.Length)
        {
            error = "The loadout and serialized weapon hierarchy are inconsistent.";
            return false;
        }

        for (int index = 0; index < weapons.Length; index++)
        {
            WeaponController weapon = weapons[index];

            if (weapon == null || !weapon.transform.IsChildOf(transform) ||
                loadout.GetWeapon(index) != weapon)
            {
                error = $"Weapon slot {index} is not bound to the rig hierarchy.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private static void AddMissing(
        UnityEngine.Object dependency,
        string dependencyName,
        ICollection<string> missing)
    {
        if (dependency == null)
        {
            missing.Add(dependencyName);
        }
    }

#if UNITY_EDITOR
    private void Reset()
    {
        RefreshReferences();
    }

    private void OnValidate()
    {
        RefreshReferences();
    }
#endif
}
