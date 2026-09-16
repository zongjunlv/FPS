using System;
using System.Collections.Generic;
using UnityEngine;

public enum TutorialEvidenceType
{
    Manual = 0,
    MoveDirection = 1,
    Jump = 2,
    Sprint = 3,
    Crouch = 4,
    HipFireHit = 5,
    AimFireHit = 6,
    SemiAutomaticShot = 7,
    AutomaticBurst = 8,
    WeaponSwitch = 9,
    Reload = 10,
    BodyHit = 11,
    HeadHit = 12
}

[Serializable]
public sealed class TutorialStepDefinition
{
    [SerializeField] private string stableId = string.Empty;
    [SerializeField] private string chapter = string.Empty;
    [SerializeField] private string title = string.Empty;
    [SerializeField, TextArea] private string instruction = string.Empty;
    [SerializeField] private TutorialEvidenceType evidenceType;
    [SerializeField, Min(1)] private int targetValue = 1;
    [SerializeField] private string completionFeedback = string.Empty;

    public TutorialStepDefinition()
    {
    }

    public TutorialStepDefinition(
        string configuredStableId,
        string configuredChapter,
        string configuredTitle,
        string configuredInstruction,
        TutorialEvidenceType configuredEvidenceType,
        int configuredTargetValue,
        string configuredCompletionFeedback)
    {
        stableId = configuredStableId;
        chapter = configuredChapter;
        title = configuredTitle;
        instruction = configuredInstruction;
        evidenceType = configuredEvidenceType;
        targetValue = configuredTargetValue;
        completionFeedback = configuredCompletionFeedback;
    }

    public string StableId => stableId;
    public string Chapter => chapter;
    public string Title => title;
    public string Instruction => instruction;
    public TutorialEvidenceType EvidenceType => evidenceType;
    public int TargetValue => targetValue;
    public string CompletionFeedback => string.IsNullOrWhiteSpace(
        completionFeedback)
        ? $"已完成：{title}"
        : completionFeedback;

    public bool TryValidate(out string error)
    {
        if (!TutorialSequenceDefinition.IsStableId(stableId))
        {
            error = $"教学步骤 ID '{stableId}' 不是稳定的小写标识。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(chapter) ||
            string.IsNullOrWhiteSpace(title) ||
            string.IsNullOrWhiteSpace(instruction))
        {
            error = $"教学步骤 '{stableId}' 缺少章节、中文标题或操作说明。";
            return false;
        }

        if (targetValue <= 0)
        {
            error = $"教学步骤 '{stableId}' 的目标数值必须大于零。";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

[CreateAssetMenu(
    fileName = "TutorialSequence",
    menuName = "FPS/Tutorial/Sequence Definition")]
public sealed class TutorialSequenceDefinition : ScriptableObject
{
    [SerializeField] private string stableId = "tutorial.main";
    [SerializeField] private string displayName = "新手训练";
    [SerializeField, Min(1)] private int version = 1;
    [SerializeField] private TutorialStepDefinition[] steps =
        Array.Empty<TutorialStepDefinition>();

    public string StableId => stableId;
    public string DisplayName => displayName;
    public int Version => version;
    public IReadOnlyList<TutorialStepDefinition> Steps => steps;

    public void Configure(
        string configuredStableId,
        string configuredDisplayName,
        int configuredVersion,
        TutorialStepDefinition[] configuredSteps)
    {
        stableId = configuredStableId;
        displayName = configuredDisplayName;
        version = Mathf.Max(1, configuredVersion);
        steps = configuredSteps ?? Array.Empty<TutorialStepDefinition>();
    }

    public bool TryValidate(out string error)
    {
        if (!IsStableId(stableId))
        {
            error = $"教学流程 ID '{stableId}' 不是稳定的小写标识。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            error = "教学流程缺少显示名称。";
            return false;
        }

        if (steps == null || steps.Length == 0)
        {
            error = "教学流程至少需要一个步骤。";
            return false;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < steps.Length; index++)
        {
            TutorialStepDefinition step = steps[index];
            if (step == null)
            {
                error = $"教学步骤 {index + 1} 为空。";
                return false;
            }

            if (!step.TryValidate(out string stepError))
            {
                error = stepError;
                return false;
            }

            if (!ids.Add(step.StableId))
            {
                error = $"教学步骤 ID '{step.StableId}' 重复。";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    public static bool IsStableId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !char.IsLetter(value[0]) ||
            char.IsUpper(value[0]))
        {
            return false;
        }

        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            bool allowed = character is >= 'a' and <= 'z' or >= '0' and <= '9' ||
                           character == '.' || character == '-' ||
                           character == '_';
            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }
}
