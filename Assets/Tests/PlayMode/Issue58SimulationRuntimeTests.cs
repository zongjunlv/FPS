using System.Collections;
using FPS.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class Issue58SimulationRuntimeTests
{
    [UnityTest]
    public IEnumerator CityNewWaveMissionAndDiagnosticsShareSimulationState()
    {
        yield return SceneManager.LoadSceneAsync(
            "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity");
        yield return WaitReady();

        WaveDirector director = WaveDirector.Active;
        CityNewMissionController mission =
            Object.FindAnyObjectByType<CityNewMissionController>();
        RunSimulationKernel simulation = director.Simulation;

        Assert.That(simulation, Is.Not.Null);
        Assert.That(simulation.Configuration.FixedTickRate,
            Is.EqualTo(WaveDirector.SimulationTickRate));
        Assert.That(director.CurrentProgress.CurrentWave,
            Is.EqualTo(simulation.Wave.CurrentWave));
        Assert.That(director.CurrentProgress.Phase,
            Is.EqualTo(simulation.Wave.Phase));
        Assert.That(mission.State, Is.EqualTo(simulation.Mission.State));
        Assert.That(director.CaptureRuntimeState().Simulation, Is.Not.Null);
        WaveRuntimeDiagnostics diagnostics = director.CaptureDiagnostics();
        Assert.That(diagnostics.SimulationTick, Is.EqualTo(simulation.Tick));
        Assert.That(diagnostics.FixedTickRate,
            Is.EqualTo(simulation.Configuration.FixedTickRate));
        Assert.That(diagnostics.NextEventSequence,
            Is.EqualTo(simulation.NextEventSequence));

        long before = simulation.Tick;
        yield return new WaitForSeconds(0.12f);
        Assert.That(simulation.Tick, Is.GreaterThan(before));
    }

    private static IEnumerator WaitReady()
    {
        float timeout = Time.realtimeSinceStartup + 35f;
        while (Time.realtimeSinceStartup < timeout)
        {
            WaveDirector director = WaveDirector.Active;
            CityNewMissionController mission =
                Object.FindAnyObjectByType<CityNewMissionController>();
            if (director != null && director.Simulation != null &&
                director.IsRunning && mission != null &&
                mission.Terminal != null)
            {
                yield break;
            }
            yield return null;
        }
        Assert.Fail("等待 CityNew 权威战局仿真初始化超时。");
    }
}
