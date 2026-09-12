using System;
using System.Collections.Generic;
using System.Linq;
using FPS.SaveGame;
using FPS.Simulation;
using NUnit.Framework;

namespace FPS.Tests.Architecture
{
    public sealed class Issue58AuthoritativeSimulationTests
    {
        [Test]
        public void ThreeWavesTerminalAndExtractionFollowOneAuthoritativeState()
        {
            RunSimulationKernel run = CreateRun();

            Assert.That(run.Submit(Command(run, SimulationCommandType.StartRun)), Is.True);
            CompleteWave(run, 1, 2);
            Assert.That(run.Wave.Phase, Is.EqualTo(WaveRunPhase.Intermission));

            run.FastForward(2);
            Assert.That(run.Wave.CurrentWave, Is.EqualTo(2));
            CompleteWave(run, 3, 3);
            Assert.That(run.Wave.CurrentWave, Is.EqualTo(3));
            CompleteWave(run, 4, 4);

            Assert.That(run.Wave.IsCompleted, Is.True);
            Assert.That(run.Mission.State, Is.EqualTo(MissionFlowState.ActivateTerminal));
            Assert.That(run.Submit(Command(run, SimulationCommandType.TerminalCompleted)), Is.True);
            Assert.That(run.Mission.State, Is.EqualTo(MissionFlowState.ExtractionAvailable));
            Assert.That(run.Submit(Command(run, SimulationCommandType.EnterExtraction)), Is.True);
            Assert.That(run.Mission.State, Is.EqualTo(MissionFlowState.Victory));
        }

        [Test]
        public void PauseStepAndHeadlessFastForwardUseExplicitFixedTicks()
        {
            RunSimulationKernel run = CreateRun();
            run.Submit(Command(run, SimulationCommandType.StartRun));
            CompleteWave(run, 1, 2);
            run.Submit(Command(run, SimulationCommandType.Pause));

            Assert.That(run.FastForward(10), Is.Zero);
            Assert.That(run.Tick, Is.Zero);
            run.Step();
            Assert.That(run.Tick, Is.EqualTo(1));
            Assert.That(run.Wave.Phase, Is.EqualTo(WaveRunPhase.Intermission));
            run.Submit(Command(run, SimulationCommandType.Resume));
            Assert.That(run.FastForward(1), Is.EqualTo(1));
            Assert.That(run.Wave.CurrentWave, Is.EqualTo(2));
        }

        [Test]
        public void MidRunSnapshotRestoresTickWaveMissionAndEventSequence()
        {
            RunSimulationKernel source = CreateRun();
            source.Submit(Command(source, SimulationCommandType.StartRun));
            source.Submit(Command(source, SimulationCommandType.EnemySpawned, 1));
            source.Submit(Command(source, SimulationCommandType.PlayerVitalsChanged,
                primary: 74f, secondary: 23f));
            source.FastForward(7);
            RunSimulationSnapshot snapshot = source.CaptureState();

            RunSimulationKernel restored = CreateRun();
            Assert.That(restored.TryRestore(snapshot, out string error), Is.True, error);
            Assert.That(restored.Tick, Is.EqualTo(7));
            Assert.That(restored.Wave.AliveCount, Is.EqualTo(1));
            Assert.That(restored.PlayerHealth, Is.EqualTo(74f));
            Assert.That(restored.PlayerArmor, Is.EqualTo(23f));

            restored.Submit(Command(restored, SimulationCommandType.EnemySpawned, 2));
            Assert.That(restored.Events.Single().Sequence,
                Is.EqualTo(snapshot.NextEventSequence));
        }

        [Test]
        public void SerializedCommandsReplayWithExactlyTheSameOrderedEvents()
        {
            RunSimulationKernel source = CreateRun();
            source.Submit(Command(source, SimulationCommandType.StartRun));
            CompleteWave(source, 1, 2);
            source.FastForward(2);
            source.Submit(Command(source, SimulationCommandType.EnemySpawned, 3));

            string json = SimulationCommandCodec.Serialize(source.AcceptedCommands);
            RunSimulationKernel replay = RunSimulationKernel.Replay(
                CreateConfiguration(),
                SimulationCommandCodec.Deserialize(json));
            string eventJson = SimulationEventCodec.Serialize(source.Events);
            var serializedEvents = SimulationEventCodec.Deserialize(eventJson);

            AssertEventsEqual(source.Events, replay.Events);
            AssertEventsEqual(source.Events, serializedEvents);
            Assert.That(replay.CaptureState().Wave.CurrentWave,
                Is.EqualTo(source.CaptureState().Wave.CurrentWave));
        }

        [Test]
        public void PausedSingleStepsAreReconstructedFromCommandTicks()
        {
            RunSimulationKernel source = CreateRun();
            source.Submit(Command(source, SimulationCommandType.StartRun));
            source.Submit(Command(source, SimulationCommandType.Pause));
            source.Step();
            source.Step();
            source.Submit(Command(source, SimulationCommandType.Resume));

            RunSimulationKernel replay = RunSimulationKernel.Replay(
                CreateConfiguration(),
                source.AcceptedCommands);

            Assert.That(replay.Tick, Is.EqualTo(source.Tick));
            AssertEventsEqual(source.Events, replay.Events);
        }

        [Test]
        public void InvalidCommandsAreRejectedWithoutChangingCoreState()
        {
            RunSimulationKernel run = CreateRun();
            Assert.That(run.Submit(Command(run, SimulationCommandType.StartRun)), Is.True);
            RunSimulationSnapshot before = run.CaptureState();

            Assert.That(run.Submit(SimulationCommand.Create(
                run.Tick + 1,
                SimulationCommandType.EnemySpawned,
                99), out string error), Is.False);
            Assert.That(error, Does.Contain("tick"));
            Assert.That(run.Wave.SpawnedCount,
                Is.EqualTo(before.Wave.CurrentWaveState.SpawnedIds.Count));
            Assert.That(run.Events.Last().Type, Is.EqualTo(SimulationEventType.CommandRejected));
        }

        [Test]
        public void PlayerDefeatAndRestartRemainExplicitLifecycleCommands()
        {
            RunSimulationKernel run = CreateRun();
            run.Submit(Command(run, SimulationCommandType.StartRun));
            run.Submit(Command(
                run,
                SimulationCommandType.PlayerVitalsChanged,
                primary: 0f));
            Assert.That(run.Mission.IsOutcome, Is.False);

            Assert.That(run.Submit(Command(
                run,
                SimulationCommandType.PlayerDefeated)), Is.True);
            Assert.That(run.Mission.State, Is.EqualTo(MissionFlowState.Defeat));
            Assert.That(run.Submit(Command(
                run,
                SimulationCommandType.RestartRun)), Is.True);
            Assert.That(run.Wave.CurrentWave, Is.EqualTo(1));
            Assert.That(run.Wave.IsRunning, Is.True);
            Assert.That(run.Mission.State,
                Is.EqualTo(MissionFlowState.EliminateTargets));
        }

        [Test]
        public void SaveClockRoundTripsWhilePreSimulationSchemaTwoStillLoads()
        {
            RunSnapshot current = ValidSave();
            current.Simulation = new SimulationClockSnapshot
            {
                Tick = 123,
                NextEventSequence = 19,
                FixedTickRate = 30,
                PlayerHealth = 76f,
                PlayerArmor = 12f
            };
            string currentJson = RunSnapshotCodec.Serialize(current);
            Assert.That(RunSnapshotCodec.TryDeserialize(
                currentJson,
                out RunSnapshot loaded,
                out string error), Is.True, error);
            Assert.That(loaded.Simulation.Tick, Is.EqualTo(123));
            Assert.That(RunSnapshotCodec.TryDeserialize(
                currentJson.Replace("\"Tick\":123", "\"Tick\":124"),
                out _,
                out error), Is.False);
            Assert.That(error, Does.Contain("校验"));

            RunSnapshot legacy = ValidSave();
            string legacyJson = RunSnapshotCodec.Serialize(legacy);
            Assert.That(RunSnapshotCodec.TryDeserialize(
                legacyJson,
                out loaded,
                out error), Is.True, error);
            Assert.That(loaded.Simulation, Is.Null);
        }

        [Test]
        public void SimulationAssemblyHasNoUnityEngineDependency()
        {
            string[] references = typeof(RunSimulationKernel).Assembly
                .GetReferencedAssemblies()
                .Select(value => value.Name)
                .ToArray();

            Assert.That(references, Does.Not.Contain("UnityEngine"));
            Assert.That(references.Any(value => value.StartsWith(
                "UnityEngine.", StringComparison.Ordinal)), Is.False);
        }

        private static RunSimulationKernel CreateRun()
        {
            return new RunSimulationKernel(CreateConfiguration());
        }

        private static RunSnapshot ValidSave()
        {
            return new RunSnapshot
            {
                Seed = 18018,
                Health = 100f,
                Armor = 0f,
                CurrentWeaponId = "rifle",
                Weapons = new List<WeaponAmmoSnapshot>
                {
                    new WeaponAmmoSnapshot
                    {
                        WeaponId = "rifle",
                        Magazine = 30,
                        Reserve = 90
                    }
                }
            };
        }

        private static RunSimulationConfiguration CreateConfiguration()
        {
            return new RunSimulationConfiguration(
                18018,
                2,
                new[]
                {
                    new WaveStageRules(2, 2, 1f),
                    new WaveStageRules(1, 1, 0f),
                    new WaveStageRules(1, 1, 0f)
                });
        }

        private static SimulationCommand Command(
            RunSimulationKernel run,
            SimulationCommandType type,
            int entityId = 0,
            float primary = 0f,
            float secondary = 0f)
        {
            return SimulationCommand.Create(
                run.Tick,
                type,
                entityId,
                primary,
                secondary);
        }

        private static void CompleteWave(
            RunSimulationKernel run,
            int firstSpawnId,
            int lastSpawnId)
        {
            for (int id = firstSpawnId; id <= lastSpawnId; id++)
            {
                Assert.That(run.Submit(Command(
                    run,
                    SimulationCommandType.EnemySpawned,
                    id)), Is.True);
            }
            for (int id = firstSpawnId; id <= lastSpawnId; id++)
            {
                Assert.That(run.Submit(Command(
                    run,
                    SimulationCommandType.EnemySettled,
                    id)), Is.True);
            }
        }

        private static void AssertEventsEqual(
            System.Collections.Generic.IReadOnlyList<SimulationEvent> expected,
            System.Collections.Generic.IReadOnlyList<SimulationEvent> actual)
        {
            Assert.That(actual.Count, Is.EqualTo(expected.Count));
            for (int index = 0; index < expected.Count; index++)
            {
                Assert.That(actual[index].Tick, Is.EqualTo(expected[index].Tick));
                Assert.That(actual[index].Sequence, Is.EqualTo(expected[index].Sequence));
                Assert.That(actual[index].Type, Is.EqualTo(expected[index].Type));
                Assert.That(actual[index].SubjectId, Is.EqualTo(expected[index].SubjectId));
                Assert.That(actual[index].IntegerValue, Is.EqualTo(expected[index].IntegerValue));
            }
        }
    }
}
