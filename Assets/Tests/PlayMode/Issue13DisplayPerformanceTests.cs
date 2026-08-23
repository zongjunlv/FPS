using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class Issue13DisplayPerformanceTests
{
    [Test]
    public void BudgetedResolution_4KDisplay_Uses1080pPixelBudget()
    {
        Vector2Int result = Calculate(4096, 2304, 1920 * 1080);

        Assert.That(result, Is.EqualTo(new Vector2Int(1920, 1080)));
    }

    [Test]
    public void BudgetedResolution_NonStandardAspect_PreservesAspectRatio()
    {
        Vector2Int result = Calculate(2560, 1664, 1920 * 1080);

        Assert.That(result.x * result.y, Is.LessThanOrEqualTo(1920 * 1080));
        Assert.That(
            (float)result.x / result.y,
            Is.EqualTo(2560f / 1664f).Within(0.002f));
    }

    [Test]
    public void BudgetedResolution_AlreadyBelowBudget_RemainsUnchanged()
    {
        Vector2Int result = Calculate(1600, 900, 1920 * 1080);

        Assert.That(result, Is.EqualTo(new Vector2Int(1600, 900)));
    }

    private static Vector2Int Calculate(
        int width,
        int height,
        int maximumPixels)
    {
        Type type = RuntimeTypeResolver.GetType(
            "PerformanceDisplayBootstrap");
        Assert.That(type, Is.Not.Null);
        MethodInfo method = type.GetMethod(
            "CalculateBudgetedResolution",
            BindingFlags.Public | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);
        return (Vector2Int)method.Invoke(
            null,
            new object[] { width, height, maximumPixels });
    }
}
