using System.Collections;
using FPS.GameplayEffects;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class Issue37GameplayEffectInspectorTests
{
    [UnityTest]
    public IEnumerator InspectorSafelyLosesPooledOrDestroyedTarget()
    {
        GameplayEffectDebugRegistry.ClearForTesting();
        var target = new GameObject("Pooled Enemy Target");
        var runtime = new GameplayEffectRuntime(
            target,
            "Enemy Affix Effects");
        GameplayEffectDefinition effect =
            ScriptableObject.CreateInstance<GameplayEffectDefinition>();
        effect.Configure(
            "enemy.affix.test",
            new GameplayEffectModifier(
                GameplayAttributeId.EnemyMaximumArmor,
                GameplayModifierOperation.Add,
                60f));
        runtime.Apply(
            effect,
            new GameplayEffectContext("test", effect, target));
        runtime.Evaluate(GameplayAttributeId.EnemyMaximumArmor, 0f);

        yield return null;
        GameplayEffectRuntimeInspector inspector =
            GameplayEffectRuntimeInspector.Instance;

        if (inspector == null)
        {
            var inspectorObject = new GameObject("Test Effect Inspector");
            inspector = inspectorObject.AddComponent<
                GameplayEffectRuntimeInspector>();
        }

        inspector.RefreshTargets();
        Assert.That(inspector.SelectTarget(target), Is.True);
        Assert.That(inspector.SelectedTarget, Is.SameAs(target));

        target.SetActive(false);
        inspector.RefreshTargets();
        Assert.That(inspector.SelectedTarget, Is.Null);

        Object.Destroy(target);
        Object.Destroy(effect);
        inspector.SetVisible(false);
        yield return null;
        Assert.That(GameplayEffectDebugRegistry.CaptureActiveTargets(),
            Is.Empty);
        GameplayEffectDebugRegistry.ClearForTesting();
    }
}
