using System.Linq;
using FPS.Core.GameModes;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class Issue75TutorialShootingWallContentTests
{
    [Test]
    public void TutorialSceneDeclaresOneValidatedShootingTarget()
    {
        Scene scene = EditorSceneManager.OpenPreviewScene(
            GameModeScenePaths.Tutorial);
        try
        {
            TutorialTrainingEnvironment environment = scene
                .GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    TutorialTrainingEnvironment>(true))
                .Single();
            TutorialShootingTarget target = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    TutorialShootingTarget>(true))
                .Single();

            Assert.That(target.TryValidate(out string error),
                Is.True, error);
            Assert.That(environment.ShootingTarget, Is.SameAs(target));
            Assert.That(target.WallCollider,
                Is.SameAs(environment.ShootingWall));
            Assert.That(target.Combat,
                Is.SameAs(environment.PlayerRig.Combat));
            Assert.That(target.transform.parent,
                Is.SameAs(environment.ShootingWall.transform));
            Assert.That(target.HitRegion.isTrigger, Is.True);
            Assert.That(target.HitRegion.gameObject.layer,
                Is.EqualTo(LayerMask.NameToLayer("Ignore Raycast")));
            Assert.That(target.HitRegion.bounds.size.x,
                Is.EqualTo(5f).Within(0.01f));
            Assert.That(target.HitRegion.bounds.size.y,
                Is.EqualTo(4f).Within(0.01f));

            SurfaceDescriptor surface = environment.ShootingWall
                .GetComponent<SurfaceDescriptor>();
            Assert.That(surface, Is.Not.Null);
            Assert.That(surface.Type, Is.EqualTo(SurfaceType.Concrete));
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    [Test]
    public void TargetProvidesCenterSpreadGuidesAndRaycastSafeVisuals()
    {
        Scene scene = EditorSceneManager.OpenPreviewScene(
            GameModeScenePaths.Tutorial);
        try
        {
            TutorialShootingTarget target = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    TutorialShootingTarget>(true))
                .Single();

            Assert.That(target.transform.Find("Target Center Zone"),
                Is.Not.Null);
            Assert.That(target.transform.Find("Spread Guide Horizontal"),
                Is.Not.Null);
            Assert.That(target.transform.Find("Spread Guide Vertical"),
                Is.Not.Null);
            Assert.That(target.transform.Find("Spread Ring Inner")
                ?.GetComponent<LineRenderer>(), Is.Not.Null);
            Assert.That(target.transform.Find("Spread Ring Outer")
                ?.GetComponent<LineRenderer>(), Is.Not.Null);

            Collider[] visualColliders = target.GetComponentsInChildren<
                Collider>(true);
            Assert.That(visualColliders, Has.Length.EqualTo(1));
            Assert.That(visualColliders[0], Is.SameAs(target.HitRegion));
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    [Test]
    public void TargetMaterialsAreProjectAssets()
    {
        Assert.That(AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/Resources/Tutorial/Materials/" +
            "TutorialTargetAccent.mat"), Is.Not.Null);
        Assert.That(AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/Resources/Tutorial/Materials/" +
            "TutorialTargetCenter.mat"), Is.Not.Null);
    }
}
