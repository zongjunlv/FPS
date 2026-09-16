using NUnit.Framework;
using UnityEngine;

public sealed class Issue72TutorialStateMachineTests
{
    private TutorialSequenceDefinition definition;

    [TearDown]
    public void TearDown()
    {
        if (definition != null)
        {
            Object.DestroyImmediate(definition);
        }
    }

    [Test]
    public void EvidenceOnlyCountsForTheCurrentlyActiveStep()
    {
        TutorialProgressionStateMachine machine = CreateMachine();

        Assert.That(machine.ReportEvidence(TutorialEvidenceType.Jump), Is.False);
        Assert.That(machine.CurrentStepIndex, Is.Zero);
        Assert.That(machine.CurrentValue, Is.Zero);

        Assert.That(machine.ReportEvidence(
            TutorialEvidenceType.MoveDirection, 1, "W"), Is.True);
        Assert.That(machine.CurrentValue, Is.EqualTo(1f));
        Assert.That(machine.ReportEvidence(
            TutorialEvidenceType.MoveDirection, 1, "A"), Is.True);
        Assert.That(machine.CurrentStepIndex, Is.EqualTo(1));
        Assert.That(machine.CurrentStep.EvidenceType,
            Is.EqualTo(TutorialEvidenceType.Jump));

        Assert.That(machine.ReportEvidence(
            TutorialEvidenceType.MoveDirection, 1, "D"), Is.False);
        Assert.That(machine.CurrentValue, Is.Zero);
    }

    [Test]
    public void RepeatedEvidenceKeyCannotInflateProgress()
    {
        TutorialProgressionStateMachine machine = CreateMachine();

        Assert.That(machine.ReportEvidence(
            TutorialEvidenceType.MoveDirection, 1, "W"), Is.True);
        Assert.That(machine.ReportEvidence(
            TutorialEvidenceType.MoveDirection, 1, "W"), Is.False);
        Assert.That(machine.CurrentValue, Is.EqualTo(1f));
        Assert.That(machine.CurrentStepIndex, Is.Zero);
    }

    [Test]
    public void ResetReturnsToFirstStepAndClearsAcceptedEvidence()
    {
        TutorialProgressionStateMachine machine = CreateMachine();
        machine.ReportEvidence(TutorialEvidenceType.MoveDirection, 1, "W");
        machine.ReportEvidence(TutorialEvidenceType.MoveDirection, 1, "A");
        Assert.That(machine.CurrentStepIndex, Is.EqualTo(1));

        machine.Reset();

        Assert.That(machine.CurrentStepIndex, Is.Zero);
        Assert.That(machine.CurrentValue, Is.Zero);
        Assert.That(machine.IsComplete, Is.False);
        Assert.That(machine.ReportEvidence(
            TutorialEvidenceType.MoveDirection, 1, "W"), Is.True);
    }

    [Test]
    public void FinalCompletionIsOrderedAndPublishedExactlyOnce()
    {
        TutorialProgressionStateMachine machine = CreateMachine();
        int stepCompletionCount = 0;
        int sequenceCompletionCount = 0;
        machine.StepCompleted += _ => stepCompletionCount++;
        machine.SequenceCompleted += _ => sequenceCompletionCount++;

        machine.ReportEvidence(TutorialEvidenceType.MoveDirection, 1, "W");
        machine.ReportEvidence(TutorialEvidenceType.MoveDirection, 1, "A");
        machine.ReportEvidence(TutorialEvidenceType.Jump);

        Assert.That(machine.IsComplete, Is.True);
        Assert.That(machine.CurrentStep, Is.Null);
        Assert.That(machine.Snapshot.CompletedStepCount, Is.EqualTo(2));
        Assert.That(stepCompletionCount, Is.EqualTo(2));
        Assert.That(sequenceCompletionCount, Is.EqualTo(1));
        Assert.That(machine.ReportEvidence(TutorialEvidenceType.Jump), Is.False);
        Assert.That(sequenceCompletionCount, Is.EqualTo(1));
    }

    [Test]
    public void DefinitionRejectsDuplicateStableIds()
    {
        definition = ScriptableObject.CreateInstance<
            TutorialSequenceDefinition>();
        TutorialStepDefinition step = Step(
            "tutorial.test.same",
            TutorialEvidenceType.Manual,
            1);
        definition.Configure(
            "tutorial.test",
            "测试教学",
            1,
            new[] { step, Step("tutorial.test.same", TutorialEvidenceType.Jump, 1) });

        Assert.That(definition.TryValidate(out string error), Is.False);
        StringAssert.Contains("重复", error);
    }

    private TutorialProgressionStateMachine CreateMachine()
    {
        definition = ScriptableObject.CreateInstance<
            TutorialSequenceDefinition>();
        definition.Configure(
            "tutorial.test",
            "测试教学",
            1,
            new[]
            {
                Step("tutorial.test.move", TutorialEvidenceType.MoveDirection, 2),
                Step("tutorial.test.jump", TutorialEvidenceType.Jump, 1)
            });
        Assert.That(definition.TryValidate(out string error), Is.True, error);
        return new TutorialProgressionStateMachine(definition);
    }

    private static TutorialStepDefinition Step(
        string id,
        TutorialEvidenceType evidence,
        int target)
    {
        return new TutorialStepDefinition(
            id,
            "测试章节",
            "测试步骤",
            "执行指定操作",
            evidence,
            target,
            "步骤完成");
    }
}
