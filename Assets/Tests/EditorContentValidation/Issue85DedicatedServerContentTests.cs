using System.IO;
using NUnit.Framework;

public sealed class Issue85DedicatedServerContentTests
{
    [Test]
    public void BuildPipelineUsesUnityServerSubtargetAndCityNew()
    {
        string source = File.ReadAllText(
            "Assets/Editor/Networking/Issue85DedicatedServerBuild.cs");
        Assert.That(source, Does.Contain("StandaloneBuildSubtarget.Server"));
        Assert.That(source, Does.Contain("CityNew.unity"));
        Assert.That(source, Does.Contain("dedicated-server-build.json"));
        Assert.That(source, Does.Contain("previousSubtarget"));
        Assert.That(source, Does.Contain("finally"));
    }

    [Test]
    public void LocalClusterScriptStartsOneServerAndTwoClients()
    {
        string script = File.ReadAllText(
            "Tools/Networking/launch_local_cluster.sh");
        Assert.That(script, Does.Contain("-fps-server"));
        Assert.That(script, Does.Contain("for client_index in 1 2"));
        Assert.That(script, Does.Contain("-nographics -disable-audio"));
        Assert.That(script, Does.Contain("server-diagnostics.json"));
        Assert.That(script, Does.Contain("server_ready"));
        Assert.That(script, Does.Contain("未进入 READY"));
    }
}
