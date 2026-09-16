using System;
using System.Linq;
using UnityEngine;

public sealed class HumanoidCharacterPreviewController : MonoBehaviour
{
    public static readonly int MotionParameter = Animator.StringToHash("Motion");

    [SerializeField] private HumanoidCharacterStandard standard;
    [SerializeField] private GameObject[] characterInstances = Array.Empty<GameObject>();
    [SerializeField] private int selectedCharacter;
    [SerializeField] private int selectedMotion;
    [SerializeField] private bool showRuntimePanel = true;

    public HumanoidCharacterStandard Standard => standard;
    public GameObject[] CharacterInstances => characterInstances;
    public int SelectedCharacter => selectedCharacter;
    public int SelectedMotion => selectedMotion;
    public int ActiveCharacterCount => characterInstances?.Count(
        value => value != null && value.activeSelf) ?? 0;

    public void Configure(
        HumanoidCharacterStandard characterStandard,
        GameObject[] instances,
        bool showPanel = true)
    {
        standard = characterStandard;
        characterInstances = instances ?? Array.Empty<GameObject>();
        showRuntimePanel = showPanel;
        selectedCharacter = 0;
        selectedMotion = 0;
        Refresh();
    }

    private void Awake()
    {
        Refresh();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.LeftArrow))
        {
            PreviousCharacter();
        }
        if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.RightArrow))
        {
            NextCharacter();
        }
        for (int index = 0; index < 10; index++)
        {
            KeyCode key = index == 9
                ? KeyCode.Alpha0
                : (KeyCode)((int)KeyCode.Alpha1 + index);
            if (Input.GetKeyDown(key))
            {
                SelectMotion(index);
            }
        }
    }

    public void PreviousCharacter()
    {
        if (characterInstances == null || characterInstances.Length == 0)
        {
            return;
        }
        SelectCharacter((selectedCharacter - 1 + characterInstances.Length) %
                        characterInstances.Length);
    }

    public void NextCharacter()
    {
        if (characterInstances == null || characterInstances.Length == 0)
        {
            return;
        }
        SelectCharacter((selectedCharacter + 1) % characterInstances.Length);
    }

    public void SelectCharacter(int index)
    {
        if (characterInstances == null || characterInstances.Length == 0)
        {
            selectedCharacter = 0;
            return;
        }
        selectedCharacter = Mathf.Clamp(index, 0, characterInstances.Length - 1);
        Refresh();
    }

    public void SelectMotion(int index)
    {
        selectedMotion = Mathf.Clamp(index, 0, 9);
        Animator active = ActiveAnimator();
        if (active != null)
        {
            active.SetInteger(MotionParameter, selectedMotion);
        }
    }

    private void Refresh()
    {
        if (characterInstances == null || characterInstances.Length == 0)
        {
            return;
        }
        selectedCharacter = Mathf.Clamp(
            selectedCharacter,
            0,
            characterInstances.Length - 1);
        for (int index = 0; index < characterInstances.Length; index++)
        {
            if (characterInstances[index] != null)
            {
                characterInstances[index].SetActive(index == selectedCharacter);
            }
        }
        SelectMotion(selectedMotion);
    }

    private Animator ActiveAnimator()
    {
        if (characterInstances == null || selectedCharacter < 0 ||
            selectedCharacter >= characterInstances.Length ||
            characterInstances[selectedCharacter] == null)
        {
            return null;
        }
        return characterInstances[selectedCharacter].GetComponent<Animator>();
    }

    private void OnGUI()
    {
        if (!showRuntimePanel || standard == null ||
            characterInstances == null || characterInstances.Length == 0)
        {
            return;
        }
        GUILayout.BeginArea(new Rect(18f, 18f, 460f, 128f), GUI.skin.box);
        GUILayout.Label("Humanoid 角色与动画校准预览");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("◀ 上一个角色")) PreviousCharacter();
        string name = selectedCharacter < standard.Characters.Count
            ? standard.Characters[selectedCharacter].DisplayName
            : $"角色 {selectedCharacter + 1}";
        GUILayout.Label(name, GUILayout.Width(160f));
        if (GUILayout.Button("下一个角色 ▶")) NextCharacter();
        GUILayout.EndHorizontal();
        GUILayout.Label("动作：1站立 2移动 3冲刺 4下蹲 5起跳 6滞空 7落地 8瞄准 9射击 0换弹");
        GUILayout.EndArea();
    }
}
