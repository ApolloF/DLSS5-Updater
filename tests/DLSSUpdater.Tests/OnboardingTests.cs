using System.Text.Json;
using DLSSUpdater.Core;
using DLSSUpdater.ViewModels;

namespace DLSSUpdater.Tests;

/// <summary>The first-run setup: when it shows, and what each choice writes to settings.</summary>
public sealed class OnboardingTests : IDisposable
{
    public OnboardingTests()
    {
        AppPaths.Root = Path.Combine(Path.GetTempPath(), "dlssu-onboarding-" + Guid.NewGuid().ToString("N")[..8]);
        AppPaths.Ensure();
    }

    public void Dispose()
    {
        try { Directory.Delete(AppPaths.Root, true); } catch (IOException) { }
    }

    private static bool Shows(AppSettings s) => OnboardingViewModel.ShouldShow(s, demo: false, demoSwitch: false);

    [Fact]
    public void FreshInstall_ShowsIt()
    {
        Assert.True(Shows(AppSettings.Load()));
    }

    [Fact]
    public void UpgradeFrom1x_DoesNotShowIt()
    {
        File.WriteAllText(AppPaths.SettingsFile, """{"DefaultProxy":"winmm.dll"}""");
        Assert.False(Shows(AppSettings.Load()));
    }

    [Fact]
    public void UnreadableSettings_DoNotShowIt()
    {
        File.WriteAllText(AppPaths.SettingsFile, "{ not json");
        Assert.False(Shows(AppSettings.Load()));
    }

    [Fact]
    public void ClosedBeforeFinishing_ShowsItAgain()
    {
        AppSettings.Load().Save();
        Assert.True(Shows(AppSettings.Load()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SkipOrFinish_IsRemembered(bool skip)
    {
        var setup = new OnboardingViewModel(AppSettings.Load());
        if (skip) setup.SkipCommand.Execute(null);
        else setup.Finish();
        Assert.False(Shows(AppSettings.Load()));
    }

    [Fact]
    public void LecramChannel_FollowsTheBuildFlag_AndTheStoredChoiceIsKept()
    {
        var s = AppSettings.Load();
        s.AutoDownloadLecram = true;
        var setup = new OnboardingViewModel(s);
        Assert.Equal(BuildFlags.LecramChannel, setup.ShowLecramChannel);
        Assert.True(setup.AutoDownloadLecram);
    }

    [Fact]
    public void LecramTexts_SayWhereTheBuildsComeFrom()
    {
        Assert.Contains("fetched automatically", OnboardingViewModel.LecramHint(true));
        Assert.Contains("Buy Me a Coffee", OnboardingViewModel.LecramHint(false));
        Assert.DoesNotContain("fetch", OnboardingViewModel.LecramHint(false));
        Assert.Contains("MediaFire", HelpTopics.LecramUpdates(true));
        Assert.DoesNotContain("MediaFire", HelpTopics.LecramUpdates(false));
        Assert.DoesNotContain("MediaFire", HelpTopics.LecramSourceIntro(false));
        Assert.Contains("Buy Me a Coffee", HelpTopics.LecramSourceIntro(false));
        Assert.DoesNotContain("Check now", AddonLibraryViewModel.NoLecramBuilds(false));
        Assert.Contains("Check now", AddonLibraryViewModel.NoLecramBuilds(true));
    }

    [Theory]
    [InlineData(AddonKind.Lecram, true, true)]
    [InlineData(AddonKind.Lecram, false, false)]
    [InlineData(AddonKind.RenoDx, true, false)]
    public void AddonChecks_OnlyForLecramInThePrivateBuild(AddonKind kind, bool channel, bool expected) =>
        Assert.Equal(expected, AddonLibraryViewModel.CanCheck(kind, channel));

    [Fact]
    public void Demo_ShowsItOnlyWithTheSwitch()
    {
        var s = AppSettings.Load();
        Assert.False(OnboardingViewModel.ShouldShow(s, demo: true, demoSwitch: false));
        Assert.True(OnboardingViewModel.ShouldShow(s, demo: true, demoSwitch: true));
    }

    [Fact]
    public void Skip_LeavesTheDefaultsUntouched()
    {
        var s = AppSettings.Load();
        var before = Json(s);
        var setup = new OnboardingViewModel(s);
        OnboardingResult? result = null;
        setup.Closed += r => result = r;
        setup.Step = OnboardingStep.Updates;
        setup.Mode = OnboardingViewModel.Modes[1];
        setup.Addon = OnboardingViewModel.Addons[2];
        setup.CheckAppUpdates = false;
        setup.AutoDownloadReShade = false;
        setup.AutoCleanBuilds = true;
        setup.KeepBuilds = 7;

        setup.SkipCommand.Execute(null);

        Assert.Equal(new OnboardingResult(false, null), result);
        Assert.True(s.OnboardingDone);
        s.OnboardingDone = false;
        Assert.Equal(before, Json(s));
    }

    [Fact]
    public void FinishWithoutChanges_KeepsTheDefaults()
    {
        var s = AppSettings.Load();
        var before = Json(s);
        OnboardingResult? result = null;
        var setup = new OnboardingViewModel(s);
        setup.Closed += r => result = r;
        setup.Finish();
        Assert.Equal(new OnboardingResult(true, null), result);
        s.OnboardingDone = false;
        Assert.Equal(before, Json(s));
    }

    [Fact]
    public void Defaults_AreTheAppDefaults()
    {
        var setup = new OnboardingViewModel(new AppSettings());
        Assert.Equal(InstallMode.OptiScaler, setup.Mode.Value);
        Assert.Equal(OptiSources.F5, setup.OptiBuild.Value);
        Assert.Null(setup.Addon.Value);
        Assert.True(setup.CheckAppUpdates);
        Assert.False(setup.AutoInstallAppUpdates);
        Assert.True(setup.AutoDownloadReShade);
        Assert.True(setup.AutoDownloadLecram);
        Assert.False(setup.AutoCleanBuilds);
    }

    [Fact]
    public void OptiScalerNrBuild_IsReturnedForSettingsToSwitch()
    {
        var s = new AppSettings();
        OnboardingResult? result = null;
        var setup = new OnboardingViewModel(s) { OptiBuild = OnboardingViewModel.Builds.First(b => b.Value == OptiSources.Default) };
        setup.Closed += r => result = r;
        setup.Finish();
        Assert.Equal(InstallMode.OptiScaler, s.DefaultMode);
        Assert.Equal(OptiSources.Default, result!.Build);
    }

    [Theory]
    [InlineData(NrAddon.RenoDx)]
    [InlineData(NrAddon.Lecram)]
    [InlineData(NrAddon.Uplift)]
    public void ReShadeMode_WithAnAddon(NrAddon addon)
    {
        var s = new AppSettings { InstallUplift = addon != NrAddon.Uplift };
        OnboardingResult? result = null;
        var setup = new OnboardingViewModel(s)
        {
            Mode = OnboardingViewModel.Modes.First(m => m.Value == InstallMode.ReShadeOnly),
            OptiBuild = OnboardingViewModel.Builds.First(b => b.Value == OptiSources.Default),
        };
        setup.Addon = OnboardingViewModel.Addons.First(a => a.Value == addon);
        setup.Closed += r => result = r;
        setup.Finish();

        Assert.Equal(InstallMode.ReShadeOnly, s.DefaultMode);
        Assert.Equal([addon], NrAddons.Defaults(s));
        // The build only counts in OptiScaler mode.
        Assert.Null(result!.Build);
        Assert.Equal(OptiSources.F5.Repo, s.OptiSource);
    }

    [Fact]
    public void ReShadeMode_WithoutAnAddon_TurnsThemOff()
    {
        var s = new AppSettings { InstallLecram = true };
        var setup = new OnboardingViewModel(s) { Mode = OnboardingViewModel.Modes[1] };
        Assert.Equal(NrAddon.Lecram, setup.Addon.Value);
        setup.Addon = OnboardingViewModel.Addons.First(a => a.Value is null);
        setup.Finish();
        Assert.Empty(NrAddons.Defaults(s));
    }

    [Fact]
    public void UnchangedAddon_KeepsAnAllowedPair()
    {
        var s = new AppSettings { AllowSeveralNrAddons = true, InstallRenoDx = true, InstallLecram = true, DefaultMode = InstallMode.ReShadeOnly };
        var setup = new OnboardingViewModel(s);
        Assert.Equal(NrAddon.Lecram, setup.Addon.Value);
        setup.Finish();
        Assert.True(s.InstallRenoDx && s.InstallLecram);
    }

    [Fact]
    public void OptiScalerMode_LeavesTheAddonsAlone()
    {
        var s = new AppSettings { InstallUplift = true, DefaultMode = InstallMode.ReShadeOnly };
        var setup = new OnboardingViewModel(s) { Mode = OnboardingViewModel.Modes[0] };
        setup.Addon = OnboardingViewModel.Addons.First(a => a.Value is null);
        setup.Finish();
        Assert.Equal(InstallMode.OptiScaler, s.DefaultMode);
        Assert.True(s.InstallUplift);
    }

    [Fact]
    public void UpdateChoices_AreWritten()
    {
        var s = new AppSettings();
        var setup = new OnboardingViewModel(s)
        {
            CheckAppUpdates = false,
            AutoInstallAppUpdates = true,
            AutoDownloadReShade = false,
            AutoDownloadLecram = false,
        };
        setup.Finish();
        Assert.False(s.CheckAppUpdates);
        Assert.True(s.AutoInstallAppUpdates);
        Assert.False(s.AutoDownloadReShade);
        Assert.False(s.AutoDownloadLecram);
        Assert.True(AppSettings.Load().AutoInstallAppUpdates);
    }

    [Fact]
    public void CleanupChoices_AreWritten_KeepToEveryLibrary()
    {
        var s = new AppSettings();
        var setup = new OnboardingViewModel(s);
        Assert.False(setup.AutoCleanBuilds);
        Assert.Equal(BuildCleanup.DefaultKeep, setup.KeepBuilds);
        setup.AutoCleanBuilds = true;
        setup.KeepBuilds = 4;
        setup.Finish();
        Assert.True(s.AutoCleanBuilds);
        Assert.All(Enum.GetValues<LibraryKind>(), k => Assert.Equal(4, s.KeepBuildsFor(k)));
    }

    [Fact]
    public void UnchangedKeep_LeavesPerLibraryCountsAlone()
    {
        var s = new AppSettings { KeepBuilds = { [nameof(LibraryKind.Lecram)] = 5 } };
        new OnboardingViewModel(s).Finish();
        Assert.Equal(5, s.KeepBuildsFor(LibraryKind.Lecram));
        Assert.Single(s.KeepBuilds);
    }

    [Fact]
    public void Steps_GoForwardAndBack()
    {
        var setup = new OnboardingViewModel(new AppSettings());
        Assert.False(setup.BackCommand.CanExecute(null));
        setup.NextCommand.Execute(null);
        Assert.Equal(OnboardingStep.Mode, setup.Step);
        Assert.Equal("Step 2 of 4", setup.StepText);
        setup.BackCommand.Execute(null);
        Assert.Equal(OnboardingStep.Welcome, setup.Step);
        setup.Step = OnboardingStep.Done;
        Assert.Equal("Start", setup.NextText);
        var closed = 0;
        setup.Closed += _ => closed++;
        setup.NextCommand.Execute(null);
        setup.SkipCommand.Execute(null);
        Assert.Equal(1, closed);
    }

    private static string Json(AppSettings s) => JsonSerializer.Serialize(s, JsonCtx.Default.AppSettings);
}
