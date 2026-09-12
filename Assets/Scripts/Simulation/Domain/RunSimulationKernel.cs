using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace FPS.Simulation
{
    public enum SimulationCommandType
    {
        StartRun,
        EnemySpawned,
        EnemySettled,
        TerminalCompleted,
        PlayerVitalsChanged,
        PlayerDefeated,
        EnterExtraction,
        StopRun,
        RestartRun,
        Pause,
        Resume
    }

    public enum SimulationEventType
    {
        RunStarted,
        RunRestarted,
        WaveStarted,
        EnemySpawned,
        EnemySettled,
        WaveEnded,
        WaveCompleted,
        MissionStateChanged,
        PlayerVitalsChanged,
        Victory,
        Defeat,
        RunStopped,
        Paused,
        Resumed,
        CommandRejected
    }

    [DataContract]
    public sealed class SimulationCommand
    {
        private SimulationCommand() { }

        private SimulationCommand(
            long tick,
            SimulationCommandType type,
            int entityId,
            float primaryValue,
            float secondaryValue)
        {
            Tick = tick;
            Type = type;
            EntityId = entityId;
            PrimaryValue = primaryValue;
            SecondaryValue = secondaryValue;
        }

        [DataMember(Order = 0, IsRequired = true)]
        public long Tick { get; private set; }

        [DataMember(Order = 1, IsRequired = true)]
        public SimulationCommandType Type { get; private set; }

        [DataMember(Order = 2, IsRequired = true)]
        public int EntityId { get; private set; }

        [DataMember(Order = 3, IsRequired = true)]
        public float PrimaryValue { get; private set; }

        [DataMember(Order = 4, IsRequired = true)]
        public float SecondaryValue { get; private set; }

        public static SimulationCommand Create(
            long tick,
            SimulationCommandType type,
            int entityId = 0,
            float primaryValue = 0f,
            float secondaryValue = 0f)
        {
            return new SimulationCommand(
                tick,
                type,
                entityId,
                primaryValue,
                secondaryValue);
        }
    }

    [DataContract]
    public sealed class SimulationEvent
    {
        private SimulationEvent() { }

        internal SimulationEvent(
            long tick,
            long sequence,
            SimulationEventType type,
            int subjectId,
            int integerValue,
            float primaryValue,
            float secondaryValue)
        {
            Tick = tick;
            Sequence = sequence;
            Type = type;
            SubjectId = subjectId;
            IntegerValue = integerValue;
            PrimaryValue = primaryValue;
            SecondaryValue = secondaryValue;
        }

        [DataMember(Order = 0, IsRequired = true)]
        public long Tick { get; private set; }

        [DataMember(Order = 1, IsRequired = true)]
        public long Sequence { get; private set; }

        [DataMember(Order = 2, IsRequired = true)]
        public SimulationEventType Type { get; private set; }

        [DataMember(Order = 3, IsRequired = true)]
        public int SubjectId { get; private set; }

        [DataMember(Order = 4, IsRequired = true)]
        public int IntegerValue { get; private set; }

        [DataMember(Order = 5, IsRequired = true)]
        public float PrimaryValue { get; private set; }

        [DataMember(Order = 6, IsRequired = true)]
        public float SecondaryValue { get; private set; }
    }

    public sealed class RunSimulationConfiguration
    {
        private readonly WaveStageRules[] stages;

        public RunSimulationConfiguration(
            long seed,
            int fixedTickRate,
            IReadOnlyList<WaveStageRules> configuredStages,
            int requiredMissionTargets = 1)
        {
            if (fixedTickRate < 1 || fixedTickRate > 1000)
            {
                throw new ArgumentOutOfRangeException(nameof(fixedTickRate));
            }
            if (configuredStages == null || configuredStages.Count == 0)
            {
                throw new ArgumentException(
                    "A simulation requires at least one wave stage.",
                    nameof(configuredStages));
            }
            if (requiredMissionTargets < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(requiredMissionTargets));
            }

            Seed = seed;
            FixedTickRate = fixedTickRate;
            RequiredMissionTargets = requiredMissionTargets;
            stages = new WaveStageRules[configuredStages.Count];
            for (int index = 0; index < stages.Length; index++)
            {
                stages[index] = configuredStages[index];
            }
        }

        public long Seed { get; }
        public int FixedTickRate { get; }
        public float FixedDeltaSeconds => 1f / FixedTickRate;
        public int RequiredMissionTargets { get; }
        public IReadOnlyList<WaveStageRules> Stages => stages;
    }

    public sealed class RunSimulationSnapshot
    {
        public RunSimulationSnapshot(
            long seed,
            long tick,
            long nextEventSequence,
            bool paused,
            float playerHealth,
            float playerArmor,
            MultiWaveFlowStateSnapshot wave,
            MissionFlowRestoreState mission)
        {
            Seed = seed;
            Tick = tick;
            NextEventSequence = nextEventSequence;
            Paused = paused;
            PlayerHealth = playerHealth;
            PlayerArmor = playerArmor;
            Wave = wave;
            Mission = mission;
        }

        public long Seed { get; }
        public long Tick { get; }
        public long NextEventSequence { get; }
        public bool Paused { get; }
        public float PlayerHealth { get; }
        public float PlayerArmor { get; }
        public MultiWaveFlowStateSnapshot Wave { get; }
        public MissionFlowRestoreState Mission { get; }
    }

    /// <summary>
    /// Pure authoritative run state. It never reads wall-clock time or scene data;
    /// callers submit stable commands and advance explicit fixed ticks.
    /// </summary>
    public sealed class RunSimulationKernel
    {
        private readonly RunSimulationConfiguration configuration;
        private readonly List<SimulationEvent> events =
            new List<SimulationEvent>();
        private readonly List<SimulationCommand> acceptedCommands =
            new List<SimulationCommand>();
        private MultiWaveFlowState wave;
        private MissionFlowStateMachine mission;
        private long tick;
        private long nextEventSequence;
        private bool paused;
        private float playerHealth = 100f;
        private float playerArmor;

        public RunSimulationKernel(RunSimulationConfiguration configuration)
        {
            this.configuration = configuration ??
                throw new ArgumentNullException(nameof(configuration));
            ResetState();
        }

        public RunSimulationConfiguration Configuration => configuration;
        public MultiWaveFlowState Wave => wave;
        public MissionFlowStateMachine Mission => mission;
        public long Tick => tick;
        public long NextEventSequence => nextEventSequence;
        public bool IsPaused => paused;
        public float PlayerHealth => playerHealth;
        public float PlayerArmor => playerArmor;
        public IReadOnlyList<SimulationEvent> Events => events;
        public IReadOnlyList<SimulationCommand> AcceptedCommands =>
            acceptedCommands;

        public bool Submit(SimulationCommand command, out string error)
        {
            error = string.Empty;
            if (!ValidateCommand(command, out error))
            {
                Emit(SimulationEventType.CommandRejected,
                    command?.EntityId ?? 0,
                    command == null ? -1 : (int)command.Type);
                return false;
            }

            bool accepted;
            switch (command.Type)
            {
                case SimulationCommandType.StartRun:
                    accepted = StartRun();
                    break;
                case SimulationCommandType.EnemySpawned:
                    accepted = SpawnEnemy(command.EntityId);
                    break;
                case SimulationCommandType.EnemySettled:
                    accepted = SettleEnemy(command.EntityId);
                    break;
                case SimulationCommandType.TerminalCompleted:
                    accepted = ChangeMission(
                        () => mission.CompleteTerminal());
                    break;
                case SimulationCommandType.PlayerVitalsChanged:
                    accepted = ChangePlayerVitals(
                        command.PrimaryValue,
                        command.SecondaryValue);
                    break;
                case SimulationCommandType.PlayerDefeated:
                    accepted = DefeatPlayer();
                    break;
                case SimulationCommandType.EnterExtraction:
                    accepted = ExtractPlayer();
                    break;
                case SimulationCommandType.StopRun:
                    accepted = StopRun();
                    break;
                case SimulationCommandType.RestartRun:
                    accepted = RestartRun();
                    break;
                case SimulationCommandType.Pause:
                    accepted = Pause();
                    break;
                case SimulationCommandType.Resume:
                    accepted = Resume();
                    break;
                default:
                    accepted = false;
                    break;
            }

            if (!accepted)
            {
                error = "Command is not valid for the current simulation state.";
                Emit(SimulationEventType.CommandRejected,
                    command.EntityId,
                    (int)command.Type);
                return false;
            }

            acceptedCommands.Add(command);
            return true;
        }

        public bool Submit(SimulationCommand command)
        {
            return Submit(command, out _);
        }

        public bool AdvanceTick()
        {
            if (paused)
            {
                return false;
            }

            AdvanceOneTick();
            return true;
        }

        public void Step()
        {
            AdvanceOneTick();
        }

        public long FastForward(long ticks)
        {
            if (ticks < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ticks));
            }

            long advanced = 0;
            while (advanced < ticks && !paused)
            {
                AdvanceOneTick();
                advanced++;
            }
            return advanced;
        }

        public RunSimulationSnapshot CaptureState()
        {
            return new RunSimulationSnapshot(
                configuration.Seed,
                tick,
                nextEventSequence,
                paused,
                playerHealth,
                playerArmor,
                wave.CaptureState(),
                mission.CaptureState());
        }

        public bool TryRestore(
            RunSimulationSnapshot snapshot,
            out string error)
        {
            error = string.Empty;
            if (snapshot == null ||
                snapshot.Seed != configuration.Seed ||
                snapshot.Tick < 0 ||
                snapshot.NextEventSequence < 0 ||
                !FiniteNonNegative(snapshot.PlayerHealth) ||
                !FiniteNonNegative(snapshot.PlayerArmor) ||
                snapshot.Wave == null)
            {
                error = "Simulation snapshot is invalid.";
                return false;
            }

            var restoredWave = new MultiWaveFlowState(
                configuration.Stages);
            if (!restoredWave.TryRestore(snapshot.Wave, out error))
            {
                return false;
            }

            var restoredMission = new MissionFlowStateMachine();
            restoredMission.Configure(configuration.RequiredMissionTargets);
            if (!restoredMission.TryRestoreSilently(
                    snapshot.Mission,
                    out error))
            {
                return false;
            }

            // Commit only after both staged states validate. Keep the live objects
            // so Unity adapters can retain their event subscriptions safely.
            if (!wave.TryRestore(snapshot.Wave, out error) ||
                !mission.TryRestoreSilently(snapshot.Mission, out error))
            {
                return false;
            }
            tick = snapshot.Tick;
            nextEventSequence = snapshot.NextEventSequence;
            paused = snapshot.Paused;
            playerHealth = snapshot.PlayerHealth;
            playerArmor = snapshot.PlayerArmor;
            events.Clear();
            acceptedCommands.Clear();
            return true;
        }

        public static RunSimulationKernel Replay(
            RunSimulationConfiguration configuration,
            IReadOnlyList<SimulationCommand> commands)
        {
            if (commands == null)
            {
                throw new ArgumentNullException(nameof(commands));
            }

            var replay = new RunSimulationKernel(configuration);
            for (int index = 0; index < commands.Count; index++)
            {
                SimulationCommand command = commands[index];
                if (command == null || command.Tick < replay.Tick)
                {
                    throw new InvalidDataException(
                        "Simulation command order is invalid.");
                }
                if (command.Tick > replay.Tick)
                {
                    long requiredTicks = command.Tick - replay.Tick;
                    long advanced = 0;
                    while (advanced < requiredTicks)
                    {
                        if (replay.IsPaused)
                        {
                            replay.Step();
                        }
                        else
                        {
                            replay.AdvanceTick();
                        }
                        advanced++;
                    }
                }
                if (!replay.Submit(command, out string error))
                {
                    throw new InvalidDataException(error);
                }
            }
            return replay;
        }

        private bool ValidateCommand(
            SimulationCommand command,
            out string error)
        {
            if (command == null)
            {
                error = "Command is null.";
                return false;
            }
            if (command.Tick != tick)
            {
                error = "Command tick does not match the authoritative tick.";
                return false;
            }
            if (!Enum.IsDefined(typeof(SimulationCommandType), command.Type))
            {
                error = "Command type is invalid.";
                return false;
            }
            if (!Finite(command.PrimaryValue) ||
                !Finite(command.SecondaryValue))
            {
                error = "Command values must be finite.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        private bool StartRun()
        {
            if (!wave.StartRun())
            {
                return false;
            }
            Emit(SimulationEventType.RunStarted);
            Emit(SimulationEventType.WaveStarted, integerValue: wave.CurrentWave);
            return true;
        }

        private bool SpawnEnemy(int spawnId)
        {
            if (!wave.TryRegisterSpawn(spawnId))
            {
                return false;
            }
            Emit(
                SimulationEventType.EnemySpawned,
                spawnId,
                wave.CurrentWave);
            return true;
        }

        private bool SettleEnemy(int spawnId)
        {
            int endingWave = wave.CurrentWave;
            if (!wave.TrySettle(spawnId))
            {
                return false;
            }

            Emit(
                SimulationEventType.EnemySettled,
                spawnId,
                endingWave);
            if (wave.Phase == WaveRunPhase.Intermission ||
                wave.Phase == WaveRunPhase.Completed ||
                wave.CurrentWave != endingWave)
            {
                Emit(
                    SimulationEventType.WaveEnded,
                    integerValue: endingWave);
            }

            if (wave.Phase == WaveRunPhase.Completed)
            {
                Emit(SimulationEventType.WaveCompleted,
                    integerValue: endingWave);
                ChangeMission(() => mission.RegisterTargetEliminated());
            }
            else if (wave.CurrentWave != endingWave)
            {
                Emit(SimulationEventType.WaveStarted,
                    integerValue: wave.CurrentWave);
            }
            return true;
        }

        private bool ChangePlayerVitals(float health, float armor)
        {
            if (!FiniteNonNegative(health) || !FiniteNonNegative(armor))
            {
                return false;
            }
            playerHealth = health;
            playerArmor = armor;
            Emit(
                SimulationEventType.PlayerVitalsChanged,
                primaryValue: health,
                secondaryValue: armor);
            return true;
        }

        private bool DefeatPlayer()
        {
            if (!ChangeMission(() => mission.Fail()))
            {
                return false;
            }
            Emit(SimulationEventType.Defeat);
            return true;
        }

        private bool ExtractPlayer()
        {
            if (!ChangeMission(() => mission.TryExtract()))
            {
                return false;
            }
            Emit(SimulationEventType.Victory);
            return true;
        }

        private bool StopRun()
        {
            if (!wave.StopRun())
            {
                return false;
            }
            Emit(SimulationEventType.RunStopped);
            return true;
        }

        private bool RestartRun()
        {
            ResetState();
            Emit(SimulationEventType.RunRestarted);
            return StartRun();
        }

        private bool Pause()
        {
            if (paused)
            {
                return false;
            }
            paused = true;
            Emit(SimulationEventType.Paused);
            return true;
        }

        private bool Resume()
        {
            if (!paused)
            {
                return false;
            }
            paused = false;
            Emit(SimulationEventType.Resumed);
            return true;
        }

        private bool ChangeMission(Func<bool> change)
        {
            MissionFlowState before = mission.State;
            bool accepted = change();
            if (accepted && mission.State != before)
            {
                Emit(
                    SimulationEventType.MissionStateChanged,
                    integerValue: (int)mission.State);
            }
            return accepted;
        }

        private void AdvanceOneTick()
        {
            checked
            {
                tick++;
            }
            int previousWave = wave.CurrentWave;
            if (wave.Tick(configuration.FixedDeltaSeconds) &&
                wave.CurrentWave != previousWave)
            {
                Emit(
                    SimulationEventType.WaveStarted,
                    integerValue: wave.CurrentWave);
            }
        }

        private void ResetState()
        {
            if (wave == null)
            {
                wave = new MultiWaveFlowState(configuration.Stages);
            }
            else
            {
                wave.TryRestore(
                    new MultiWaveFlowStateSnapshot(
                        0,
                        WaveRunPhase.Idle,
                        null,
                        0f),
                    out _);
            }
            if (mission == null)
            {
                mission = new MissionFlowStateMachine();
            }
            mission.Configure(configuration.RequiredMissionTargets);
            paused = false;
            playerHealth = 100f;
            playerArmor = 0f;
        }

        private void Emit(
            SimulationEventType type,
            int subjectId = 0,
            int integerValue = 0,
            float primaryValue = 0f,
            float secondaryValue = 0f)
        {
            events.Add(new SimulationEvent(
                tick,
                nextEventSequence++,
                type,
                subjectId,
                integerValue,
                primaryValue,
                secondaryValue));
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool FiniteNonNegative(float value)
        {
            return Finite(value) && value >= 0f;
        }
    }

    public static class SimulationCommandCodec
    {
        [DataContract]
        private sealed class CommandEnvelope
        {
            [DataMember(Order = 0, IsRequired = true)]
            public List<SimulationCommand> Commands =
                new List<SimulationCommand>();
        }

        public static string Serialize(
            IReadOnlyList<SimulationCommand> commands)
        {
            if (commands == null)
            {
                throw new ArgumentNullException(nameof(commands));
            }
            var envelope = new CommandEnvelope();
            for (int index = 0; index < commands.Count; index++)
            {
                if (commands[index] == null)
                {
                    throw new InvalidDataException(
                        "Simulation command cannot be null.");
                }
                envelope.Commands.Add(commands[index]);
            }
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(CommandEnvelope))
                    .WriteObject(stream, envelope);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public static IReadOnlyList<SimulationCommand> Deserialize(
            string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidDataException(
                    "Simulation command log is empty.");
            }
            try
            {
                using (var stream = new MemoryStream(
                           Encoding.UTF8.GetBytes(json)))
                {
                    var envelope = (CommandEnvelope)
                        new DataContractJsonSerializer(
                            typeof(CommandEnvelope)).ReadObject(stream);
                    if (envelope?.Commands == null)
                    {
                        throw new InvalidDataException(
                            "Simulation command log is incomplete.");
                    }
                    return envelope.Commands;
                }
            }
            catch (SerializationException exception)
            {
                throw new InvalidDataException(
                    "Simulation command log cannot be decoded.",
                    exception);
            }
        }
    }

    public static class SimulationEventCodec
    {
        [DataContract]
        private sealed class EventEnvelope
        {
            [DataMember(Order = 0, IsRequired = true)]
            public List<SimulationEvent> Events =
                new List<SimulationEvent>();
        }

        public static string Serialize(IReadOnlyList<SimulationEvent> events)
        {
            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }
            Validate(events);
            var envelope = new EventEnvelope();
            for (int index = 0; index < events.Count; index++)
            {
                envelope.Events.Add(events[index]);
            }
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(EventEnvelope))
                    .WriteObject(stream, envelope);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public static IReadOnlyList<SimulationEvent> Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidDataException(
                    "Simulation event log is empty.");
            }
            try
            {
                using (var stream = new MemoryStream(
                           Encoding.UTF8.GetBytes(json)))
                {
                    var envelope = (EventEnvelope)
                        new DataContractJsonSerializer(
                            typeof(EventEnvelope)).ReadObject(stream);
                    if (envelope?.Events == null)
                    {
                        throw new InvalidDataException(
                            "Simulation event log is incomplete.");
                    }
                    Validate(envelope.Events);
                    return envelope.Events;
                }
            }
            catch (SerializationException exception)
            {
                throw new InvalidDataException(
                    "Simulation event log cannot be decoded.",
                    exception);
            }
        }

        private static void Validate(IReadOnlyList<SimulationEvent> events)
        {
            long previousTick = -1;
            long firstSequence = events.Count > 0 && events[0] != null
                ? events[0].Sequence
                : 0;
            for (int index = 0; index < events.Count; index++)
            {
                SimulationEvent current = events[index];
                if (current == null || current.Tick < previousTick ||
                    firstSequence < 0 ||
                    current.Sequence != firstSequence + index ||
                    !Enum.IsDefined(typeof(SimulationEventType), current.Type) ||
                    float.IsNaN(current.PrimaryValue) ||
                    float.IsInfinity(current.PrimaryValue) ||
                    float.IsNaN(current.SecondaryValue) ||
                    float.IsInfinity(current.SecondaryValue))
                {
                    throw new InvalidDataException(
                        "Simulation events are not in canonical order.");
                }
                previousTick = current.Tick;
            }
        }
    }
}
