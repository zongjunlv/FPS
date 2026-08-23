using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace FPS.Editor.CI
{
    public static class UnityQualityGate
    {
        private const string MarkerEnvironmentVariable =
            "FPS_QUALITY_GATE_MARKER";

        public static void CompileCheck()
        {
            if (EditorUtility.scriptCompilationFailed)
            {
                throw new BuildFailedException(
                    "Unity reported script compilation errors.");
            }

            string markerPath = Environment.GetEnvironmentVariable(
                MarkerEnvironmentVariable);

            if (string.IsNullOrWhiteSpace(markerPath))
            {
                throw new BuildFailedException(
                    $"Missing {MarkerEnvironmentVariable}.");
            }

            string markerDirectory = Path.GetDirectoryName(markerPath);

            if (string.IsNullOrWhiteSpace(markerDirectory))
            {
                throw new BuildFailedException(
                    $"Invalid compile marker path: {markerPath}");
            }

            Directory.CreateDirectory(markerDirectory);
            File.WriteAllText(
                markerPath,
                $"unity={Application.unityVersion}{Environment.NewLine}" +
                $"utc={DateTime.UtcNow:O}{Environment.NewLine}");
            Debug.Log(
                $"[QUALITY_GATE_COMPILE_OK] Unity {Application.unityVersion}");
        }
    }
}
