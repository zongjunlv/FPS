using System.Collections;
using FPS.Networking.Netcode;
using FPS.Networking.Session;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode.Issue65
{
    public sealed class Issue65SessionRuntimeTests
    {
        [UnityTest]
        public IEnumerator DirectHostEntryBuildsCompleteAuthoritativeSlice()
        {
            var gameObject = new GameObject("Issue65 Session Integration");
            CoopSessionController controller =
                gameObject.AddComponent<CoopSessionController>();

            NetworkEndpointSettings endpoint =
                NetworkEndpointSettings.Localhost;
            endpoint.Port = 17968;
            Assert.That(controller.StartDirect(true, endpoint), Is.True,
                controller.LastFailure);

            for (int frame = 0; frame < 30; frame++) yield return null;

            Assert.That(controller.IsConnected, Is.True);
            Assert.That(controller.IsBattleTransportConnected, Is.True,
                "UGS/会话连接与可游玩的 NGO 战斗连接必须明确区分。");
            Assert.That(controller.IsHost, Is.True);
            Assert.That(controller.PlayerCount, Is.EqualTo(1));
            NetworkCoopSessionAuthority authority =
                Object.FindFirstObjectByType<
                    NetworkCoopSessionAuthority>();
            Assert.That(authority, Is.Not.Null);
            Assert.That(authority.IsSpawned, Is.True);
            Assert.That(authority.ReplicatedPlayerCount, Is.EqualTo(2));
            Assert.That(authority.ReplicatedTargetCount, Is.GreaterThan(6),
                "直连联机战斗必须加载完整 CityNew 敌人清单，不能退回单只 Spider 占位场景。");
            Assert.That(authority.WorldState.TotalWaves, Is.EqualTo(3));
            Assert.That(authority.WorldState.RequiredKills,
                Is.EqualTo(authority.ReplicatedTargetCount));

            var leave = controller.LeaveAsync();
            while (!leave.IsCompleted) yield return null;
            Object.Destroy(gameObject);
            yield return null;
        }
    }
}
