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
            Assert.That(controller.IsHost, Is.True);
            Assert.That(controller.PlayerCount, Is.EqualTo(1));
            NetworkCoopSessionAuthority authority =
                Object.FindFirstObjectByType<
                    NetworkCoopSessionAuthority>();
            Assert.That(authority, Is.Not.Null);
            Assert.That(authority.IsSpawned, Is.True);
            Assert.That(authority.ReplicatedPlayerCount, Is.EqualTo(2));
            Assert.That(authority.ReplicatedTargetCount, Is.EqualTo(1));

            var leave = controller.LeaveAsync();
            while (!leave.IsCompleted) yield return null;
            Object.Destroy(gameObject);
            yield return null;
        }
    }
}
