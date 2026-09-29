using System.Collections;
using FPS.Core.GameModes;
using FPS.Networking.Session;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode.Issue69
{
    public sealed class Issue102CoopLobbyCursorRecoveryTests
    {
        [UnityTest]
        public IEnumerator CancelledBattleLoadRestoresLobbyCursorWithoutPlayer()
        {
            yield return SceneManager.LoadSceneAsync(
                GameModeScenePaths.CoopLogin, LoadSceneMode.Single);
            yield return null;

            CoopSceneLoadCoordinator coordinator =
                Object.FindFirstObjectByType<CoopSceneLoadCoordinator>();
            Assert.That(coordinator, Is.Not.Null);
            Assert.That(Object.FindFirstObjectByType<PlayerController>(),
                Is.Null, "连接失败时尚未生成玩家，也必须恢复大厅光标。");

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            CoopUiInputGate.PauseMenuVisible = true;
            CoopUiInputGate.EconomyModalVisible = true;
            GameModeContext.BeginTransition(GameModeId.Coop,
                GameModeStage.CoopBattle);
            coordinator.SendMessage("HandleLoadCancelled", "连接战斗服务器超时");
            yield return null;

            Assert.That(GameModeContext.IsTransitioning, Is.False,
                "未离开 CoopLogin 时取消加载，也应结束待处理的战斗切换。");
            Assert.That(GameModeContext.IsActive(GameModeId.Coop,
                GameModeStage.CoopLogin), Is.True);
            Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
            Assert.That(Cursor.visible, Is.True);
            Assert.That(CoopUiInputGate.GameplayInputSuppressed, Is.False);
        }

        [UnityTest]
        public IEnumerator FailedConnectionReturnsFromBattleSceneWithUsableCursor()
        {
            yield return SceneManager.LoadSceneAsync(
                GameModeScenePaths.CoopLogin, LoadSceneMode.Single);
            yield return null;
            CoopSceneLoadCoordinator coordinator =
                Object.FindFirstObjectByType<CoopSceneLoadCoordinator>();
            Assert.That(coordinator, Is.Not.Null);

            GameModeContext.BeginTransition(GameModeId.Coop,
                GameModeStage.CoopBattle);
            yield return SceneManager.LoadSceneAsync(
                GameModeScenePaths.CityNew, LoadSceneMode.Single);
            yield return null;
            Assert.That(GameModeContext.IsActive(GameModeId.Coop,
                GameModeStage.CoopBattle), Is.True);

            foreach (PlayerController player in Object.FindObjectsByType<
                         PlayerController>(FindObjectsSortMode.None))
                Object.Destroy(player.gameObject);
            yield return null;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            coordinator.SendMessage("HandleLoadCancelled", "连接战斗服务器超时");

            float deadline = Time.realtimeSinceStartup + 20f;
            while (SceneManager.GetActiveScene().path !=
                   GameModeScenePaths.CoopLogin &&
                   Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(SceneManager.GetActiveScene().path,
                Is.EqualTo(GameModeScenePaths.CoopLogin));
            yield return null;

            Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
            Assert.That(Cursor.visible, Is.True);
            Assert.That(Object.FindFirstObjectByType<CoopAccountView>(),
                Is.Not.Null);
        }
    }
}
