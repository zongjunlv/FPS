using System;
using UnityEngine;

public static class PerformanceDisplayBootstrap
{
    public const int DefaultFullscreenPixelBudget = 1920 * 1080;

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ApplyFullscreenPixelBudget()
    {
#if UNITY_STANDALONE
        if (Application.isEditor ||
            !Screen.fullScreen ||
            HasCommandLineArgument("-fps-benchmark") ||
            HasCommandLineArgument("-native-resolution"))
        {
            return;
        }

        int pixelBudget = ReadPixelBudget();
        Vector2Int current =
            new Vector2Int(Screen.width, Screen.height);
        Vector2Int target = CalculateBudgetedResolution(
            current.x,
            current.y,
            pixelBudget);

        if (target == current)
        {
            return;
        }

        Screen.SetResolution(
            target.x,
            target.y,
            Screen.fullScreenMode);
        Debug.Log(
            $"Fullscreen render resolution limited from " +
            $"{current.x}x{current.y} to {target.x}x{target.y}. " +
            "Launch with -native-resolution to opt out.");
#endif
    }

    public static Vector2Int CalculateBudgetedResolution(
        int width,
        int height,
        int maximumPixels)
    {
        width = Mathf.Max(1, width);
        height = Mathf.Max(1, height);
        maximumPixels = Mathf.Max(1, maximumPixels);
        long currentPixels = (long)width * height;

        if (currentPixels <= maximumPixels)
        {
            return new Vector2Int(width, height);
        }

        double scale = Math.Sqrt(
            maximumPixels / (double)currentPixels);
        int targetWidth = Math.Max(
            2,
            (int)Math.Floor(width * scale / 2.0) * 2);
        int targetHeight = Math.Max(
            2,
            (int)Math.Floor(height * scale / 2.0) * 2);
        return new Vector2Int(targetWidth, targetHeight);
    }

    private static int ReadPixelBudget()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        int keyIndex = Array.IndexOf(
            arguments,
            "-fullscreen-pixel-budget");

        if (keyIndex >= 0 &&
            keyIndex + 1 < arguments.Length &&
            int.TryParse(arguments[keyIndex + 1], out int value))
        {
            return Mathf.Max(640 * 360, value);
        }

        return DefaultFullscreenPixelBudget;
    }

    private static bool HasCommandLineArgument(string argument)
    {
        return Array.IndexOf(
            Environment.GetCommandLineArgs(),
            argument) >= 0;
    }
}
