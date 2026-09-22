using FPS.Core.GameModes;
using NUnit.Framework;
using UnityEngine;

namespace FPS.Tests.Architecture
{
    public sealed class BattleMusicTests
    {
        [Test]
        public void MusicOnlyInstallsInInteractiveBattleScene()
        {
            Assert.That(BattleMusicController.ShouldInstallForScene(
                GameModeStage.Battle, true, false, false), Is.True);
            Assert.That(BattleMusicController.ShouldInstallForScene(
                GameModeStage.Tutorial, true, false, false), Is.False);
            Assert.That(BattleMusicController.ShouldInstallForScene(
                GameModeStage.Battle, false, false, false), Is.False);
            Assert.That(BattleMusicController.ShouldInstallForScene(
                GameModeStage.Battle, true, true, false), Is.False);
            Assert.That(BattleMusicController.ShouldInstallForScene(
                GameModeStage.Battle, true, false, true), Is.False);
        }

        [Test]
        public void SoloMusicIntensifiesOnlyDuringSpawningAndFighting()
        {
            Assert.That(BattleMusicController.ShouldUseCombatLayer(
                GameModeId.SoloBattle, WaveRunPhase.Idle), Is.False);
            Assert.That(BattleMusicController.ShouldUseCombatLayer(
                GameModeId.SoloBattle, WaveRunPhase.Spawning), Is.True);
            Assert.That(BattleMusicController.ShouldUseCombatLayer(
                GameModeId.SoloBattle, WaveRunPhase.Fighting), Is.True);
            Assert.That(BattleMusicController.ShouldUseCombatLayer(
                GameModeId.SoloBattle, WaveRunPhase.Intermission), Is.False);
            Assert.That(BattleMusicController.ShouldUseCombatLayer(
                GameModeId.SoloBattle, WaveRunPhase.Completed), Is.False);
        }

        [Test]
        public void CooperativeBattleKeepsActionBedWithoutSoloWaveDirector()
        {
            Assert.That(BattleMusicController.ShouldUseCombatLayer(
                GameModeId.Coop, WaveRunPhase.Idle), Is.True);
        }

        [Test]
        public void MusicLayersArePresentAndSampleAligned()
        {
            AudioClip calm = Resources.Load<AudioClip>(
                BattleMusicController.CalmResourcePath);
            AudioClip combat = Resources.Load<AudioClip>(
                BattleMusicController.CombatResourcePath);

            Assert.That(calm, Is.Not.Null);
            Assert.That(combat, Is.Not.Null);
            Assert.That(calm.samples, Is.EqualTo(combat.samples));
            Assert.That(calm.frequency, Is.EqualTo(combat.frequency));
            Assert.That(calm.loadType, Is.EqualTo(AudioClipLoadType.Streaming));
            Assert.That(combat.loadType, Is.EqualTo(AudioClipLoadType.Streaming));
        }
    }
}
