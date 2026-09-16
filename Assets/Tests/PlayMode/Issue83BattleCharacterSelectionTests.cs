using System.Collections;
using System.Linq;
using FPS.Core.GameModes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class Issue83BattleCharacterSelectionTests : InputTestFixture
{
    private bool hadStoredSelection;
    private string storedSelection;

    [SetUp]
    public void CaptureSelection()
    {
        hadStoredSelection = PlayerPrefs.HasKey(
            PlayerAppearanceSelection.PlayerPrefsKey);
        storedSelection = PlayerPrefs.GetString(
            PlayerAppearanceSelection.PlayerPrefsKey, string.Empty);
        PlayerPrefs.DeleteKey(PlayerAppearanceSelection.PlayerPrefsKey);
        GameModeFlowController.ResetRuntimeForTests();
    }

    [TearDown]
    public void RestoreSelection()
    {
        if (hadStoredSelection)
            PlayerPrefs.SetString(PlayerAppearanceSelection.PlayerPrefsKey,
                storedSelection);
        else
            PlayerPrefs.DeleteKey(PlayerAppearanceSelection.PlayerPrefsKey);
        PlayerPrefs.Save();
        GameModeFlowController.ResetRuntimeForTests();
    }

    [UnityTest]
    public IEnumerator BattleEntryShowsThreeFullCharactersBeforeCityNew()
    {
        yield return LoadAndSettle(GameModeScenePaths.Entry);
        ModeEntryView entry = Object.FindFirstObjectByType<ModeEntryView>();
        entry.GetButton(GameModeId.SoloBattle).onClick.Invoke();
        yield return WaitForScene(GameModeScenePaths.BattlePreparation);

        Assert.That(SceneManager.GetActiveScene().path,
            Is.EqualTo(GameModeScenePaths.BattlePreparation));
        BattleCharacterSelectionView view =
            Object.FindFirstObjectByType<BattleCharacterSelectionView>();
        Assert.That(view, Is.Not.Null);
        Assert.That(view.CharacterButtons.Count, Is.EqualTo(3));
        Assert.That(view.CharacterInstances.Count, Is.EqualTo(3));
        Assert.That(view.CharacterInstances.All(instance =>
            instance.GetComponentInChildren<SkinnedMeshRenderer>(true) != null),
            Is.True);
        Assert.That(Object.FindFirstObjectByType<ModeDestinationView>(), Is.Null);
        Assert.That(Object.FindFirstObjectByType<PlayerController>(), Is.Null);
        Assert.That(GameModeContext.IsActive(GameModeId.SoloBattle,
            GameModeStage.BattlePreparation), Is.True);
    }

    [UnityTest]
    public IEnumerator KeyboardSelectConfirmPersistsAndAppliesInCityNew()
    {
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        yield return LoadAndSettle(GameModeScenePaths.BattlePreparation);
        BattleCharacterSelectionView view =
            Object.FindFirstObjectByType<BattleCharacterSelectionView>();
        Assert.That(view.SelectedIndex, Is.EqualTo(0));

        Press(keyboard.rightArrowKey);
        yield return null;
        Release(keyboard.rightArrowKey);
        yield return null;
        Assert.That(view.SelectedIndex, Is.EqualTo(1));
        string chosenId = view.SelectedAppearanceId;

        Press(keyboard.enterKey);
        yield return null;
        Release(keyboard.enterKey);
        yield return WaitForScene(GameModeScenePaths.CityNew, 12);

        Assert.That(PlayerPrefs.GetString(
            PlayerAppearanceSelection.PlayerPrefsKey), Is.EqualTo(chosenId));
        CityNewPlayerModeInstaller installer =
            Object.FindFirstObjectByType<CityNewPlayerModeInstaller>();
        Assert.That(installer, Is.Not.Null);
        Assert.That(installer.IsInitialized, Is.True, installer.InitializationError);
        Assert.That(installer.AppearanceHost, Is.Not.Null);
        Assert.That(installer.AppearanceHost.CurrentDefinition.StableId,
            Is.EqualTo(chosenId));
        Assert.That(installer.AppearanceHost.CurrentInstance
            .GetComponentsInChildren<Renderer>(true)
            .All(renderer => !renderer.enabled), Is.True,
            "本地第一人称不得被第三人称全身模型遮挡。");
        InputSystem.RemoveDevice(keyboard);
    }

    [UnityTest]
    public IEnumerator MouseSelectionReturnAndReentryKeepLastConfirmedCharacter()
    {
        PlayerAppearanceCatalog catalog = Resources.Load<PlayerAppearanceCatalog>(
            PlayerAppearanceCatalog.ResourcesPath);
        string remembered = catalog.Definitions[2].StableId;
        PlayerPrefs.SetString(PlayerAppearanceSelection.PlayerPrefsKey, remembered);
        PlayerPrefs.Save();

        yield return LoadAndSettle(GameModeScenePaths.BattlePreparation);
        BattleCharacterSelectionView first =
            Object.FindFirstObjectByType<BattleCharacterSelectionView>();
        Assert.That(first.SelectedAppearanceId, Is.EqualTo(remembered));
        first.CharacterButtons[1].onClick.Invoke();
        Assert.That(first.SelectedIndex, Is.EqualTo(1));
        first.ReturnButton.onClick.Invoke();
        yield return WaitForScene(GameModeScenePaths.Entry);
        Assert.That(PlayerPrefs.GetString(
            PlayerAppearanceSelection.PlayerPrefsKey), Is.EqualTo(remembered),
            "返回不得保存尚未确认的角色。");

        Object.FindFirstObjectByType<ModeEntryView>()
            .GetButton(GameModeId.SoloBattle).onClick.Invoke();
        yield return WaitForScene(GameModeScenePaths.BattlePreparation);
        BattleCharacterSelectionView second =
            Object.FindFirstObjectByType<BattleCharacterSelectionView>();
        Assert.That(second.SelectedAppearanceId, Is.EqualTo(remembered));
    }

    [UnityTest]
    public IEnumerator InvalidSavedIdShowsFallbackAndTutorialBypassesSelection()
    {
        PlayerAppearanceCatalog catalog = Resources.Load<PlayerAppearanceCatalog>(
            PlayerAppearanceCatalog.ResourcesPath);
        PlayerPrefs.SetString(PlayerAppearanceSelection.PlayerPrefsKey,
            "missing.character");
        PlayerPrefs.Save();
        yield return LoadAndSettle(GameModeScenePaths.BattlePreparation);
        BattleCharacterSelectionView selection =
            Object.FindFirstObjectByType<BattleCharacterSelectionView>();
        Assert.That(selection.SelectedAppearanceId,
            Is.EqualTo(catalog.DefaultAppearanceId));
        Assert.That(selection.VisibleStatus, Does.Contain("回退"));

        selection.ReturnButton.onClick.Invoke();
        yield return WaitForScene(GameModeScenePaths.Entry);
        Object.FindFirstObjectByType<ModeEntryView>()
            .GetButton(GameModeId.Tutorial).onClick.Invoke();
        yield return WaitForScene(GameModeScenePaths.Tutorial, 6);
        Assert.That(Object.FindFirstObjectByType<
            BattleCharacterSelectionView>(), Is.Null);
        Assert.That(Object.FindFirstObjectByType<PlayerGameplayRig>(), Is.Not.Null);
    }

    private static IEnumerator LoadAndSettle(string path, int frames = 3)
    {
        yield return SceneManager.LoadSceneAsync(path, LoadSceneMode.Single);
        for (int index = 0; index < frames; index++) yield return null;
    }

    private static IEnumerator WaitForScene(string path, int settleFrames = 3)
    {
        float deadline = Time.realtimeSinceStartup + 20f;
        while (Time.realtimeSinceStartup < deadline)
        {
            if (SceneManager.GetActiveScene().path == path &&
                !GameModeContext.IsTransitioning)
            {
                for (int index = 0; index < settleFrames; index++) yield return null;
                yield break;
            }
            yield return null;
        }
        Assert.Fail($"Timed out loading scene: {path}; actual=" +
                    $"{SceneManager.GetActiveScene().path}; " +
                    $"transitioning={GameModeContext.IsTransitioning}; " +
                    $"failure={GameModeContext.LastFailure}");
    }
}
