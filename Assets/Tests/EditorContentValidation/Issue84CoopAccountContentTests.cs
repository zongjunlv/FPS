using System.IO;
using System.Linq;
using FPS.Core.GameModes;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public sealed class Issue84CoopAccountContentTests
{
    [Test]
    public void CoopLoginSceneKeepsDedicatedAccountStage()
    {
        Scene scene = EditorSceneManager.OpenPreviewScene(
            GameModeScenePaths.CoopLogin);
        try
        {
            GameModeSceneMarker marker = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    GameModeSceneMarker>(true)).Single();
            Assert.That(marker, Is.Not.Null);
            Assert.That(marker.Mode, Is.EqualTo(GameModeId.Coop));
            Assert.That(marker.Stage, Is.EqualTo(GameModeStage.CoopLogin));
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    [Test]
    public void BootstrapUsesAccountViewAndProductionAuthenticationGateway()
    {
        string bootstrap = File.ReadAllText(
            "Assets/Scripts/GameModes/GameModeSceneBootstrap.cs");
        Assert.That(bootstrap, Does.Contain("CoopAccountView.Create"));
        Assert.That(bootstrap,
            Does.Contain("CoopAccountView.CreateAuthenticationGate"));
        Assert.That(bootstrap,
            Does.Contain("SetAuthenticationUnlocked(false)"));
        Assert.That(bootstrap, Does.Contain("SelfHostedAuthenticationGateway"));
        Assert.That(bootstrap, Does.Not.Contain("new UnityAuthenticationGateway"));
        Assert.That(bootstrap, Does.Contain("GameModeStage.CoopLogin"));
    }
}
