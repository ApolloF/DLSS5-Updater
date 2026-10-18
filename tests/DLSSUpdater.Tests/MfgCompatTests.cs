using DLSSUpdater.Core;
using DLSSUpdater.Scan;
using Xunit;

namespace DLSSUpdater.Tests;

public class MfgCompatTests
{
    private static List<DlssDll> Set(string dir, string version, params string[] extra) =>
        new[] { "sl.interposer.dll", "sl.common.dll", "sl.dlss_g.dll", "sl.reflex.dll" }.Concat(extra)
            .Select(n => new DlssDll { Name = n, Path = Path.Combine(dir, n), Version = Version.Parse(version) }).ToList();

    private static readonly Version Fg9_1 = new(310, 9, 1, 0);
    private static readonly Version Fg9_16 = new(310, 9, 16, 0);

    [Fact]
    public void Game_without_streamline_dlss_g_is_skipped()
    {
        var sl = Set(@"C:\g", "2.14.1.0").Where(d => d.Name != "sl.dlss_g.dll").ToList();
        var plan = MfgCompat.Plan(sl, Fg9_1, null, false, false, false);
        Assert.False(plan.Install);
        Assert.Contains("sl.dlss_g.dll", plan.Skip);
        Assert.False(MfgCompat.Plan([], null, null, false, false, false).Install);
    }

    [Fact]
    public void Matched_set_with_new_dlss_g_installs_without_warnings()
    {
        var plan = MfgCompat.Plan(Set(@"C:\g", "2.14.16.0"), Fg9_16, null, false, false, false);
        Assert.True(plan.Install);
        Assert.False(plan.SwapStreamline);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void Old_dlss_g_without_multi_frame_code_warns()
    {
        var plan = MfgCompat.Plan(Set(@"C:\g", "2.7.30.0"), new Version(3, 7, 10, 0), null, false, false, false);
        Assert.True(plan.Install);
        Assert.Contains(plan.Warnings, w => w.Contains("310.x"));
    }

    [Fact]
    public void Fixed_mfg_accepts_an_unlisted_pair()
    {
        // The README's basic route: only nvngx_dlssg.dll replaced, the game's Streamline left alone.
        var plan = MfgCompat.Plan(Set(@"C:\g", "2.10.3.0"), Fg9_16, new Version(2, 14, 16), false, false, false);
        Assert.Empty(plan.Warnings);
        Assert.False(plan.SwapStreamline);
    }

    [Fact]
    public void Dynamic_mfg_swaps_in_the_matching_streamline_set()
    {
        var plan = MfgCompat.Plan(Set(@"C:\g", "2.14.1.0"), Fg9_16, new Version(2, 14, 16, 0), false, true, false);
        Assert.True(plan.SwapStreamline);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void Dynamic_mfg_keeps_a_set_that_already_matches()
    {
        var plan = MfgCompat.Plan(Set(@"C:\g", "2.14.1.0"), Fg9_1, new Version(2, 14, 16, 0), false, true, false);
        Assert.False(plan.SwapStreamline);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void Dynamic_mfg_warns_when_no_matching_sdk()
    {
        var plan = MfgCompat.Plan(Set(@"C:\g", "2.10.3.0"), Fg9_1, new Version(2, 14, 16, 0), false, true, false);
        Assert.False(plan.SwapStreamline);
        Assert.Contains(plan.Warnings, w => w.Contains("complete Streamline 2.14.1 set"));
    }

    [Fact]
    public void Dynamic_mfg_warns_on_unvalidated_dlss_g()
    {
        var plan = MfgCompat.Plan(Set(@"C:\g", "2.14.16.0"), new Version(310, 6, 0, 0), new Version(2, 14, 16), false, true, false);
        Assert.Contains(plan.Warnings, w => w.Contains("310.9.1 or 310.9.16"));
    }

    [Fact]
    public void Mixed_streamline_files_in_one_folder_warn_unless_swapped()
    {
        var sl = Set(@"C:\g", "2.14.1.0");
        sl.Single(d => d.Name == "sl.dlss_g.dll").Version = new Version(2, 10, 3, 0);
        Assert.Contains(MfgCompat.Plan(sl, Fg9_16, null, false, false, false).Warnings, w => w.Contains("different versions"));
        Assert.Empty(MfgCompat.Plan(sl, Fg9_16, new Version(2, 14, 16), true, false, false).Warnings);
    }

    [Fact]
    public void Separate_streamline_folders_are_judged_on_their_own()
    {
        var sl = Set(@"C:\g\engine", "2.8.0.0").Concat(Set(@"C:\g\game", "2.14.1.0")).ToList();
        Assert.Empty(MfgCompat.Plan(sl, Fg9_1, null, false, true, false).Warnings);
    }

    [Fact]
    public async Task Installer_skips_mfg_in_a_game_without_streamline_fg()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "dlssu-mfg-" + Guid.NewGuid().ToString("N"));
        var old = AppPaths.Root;
        try
        {
            AppPaths.Root = Path.Combine(tmp, "app");
            AppPaths.Ensure();
            var store = new ComponentStore(new GitHubClient(), () => true);
            var mfgDir = ComponentStore.TagDir(Component.MfgUnlock, "1.0");
            Directory.CreateDirectory(mfgDir);
            File.WriteAllText(Path.Combine(mfgDir, ComponentStore.MfgFile), "MFG");
            var release = new ReleaseInfo { Tag = "1.0" };
            ComponentStore.MarkComplete(Component.MfgUnlock, release);
            store.Mfg = release;
            var root = Path.Combine(tmp, "game");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "Game.exe"), "EXE");
            File.WriteAllText(Path.Combine(root, "nvngx_dlssg.dll"), "FG");

            var game = GameScanner.Inspect(new GameEntry("Game", root, "Manual"));
            await new Installer(store).InstallAsync(game, root, new InstallOptions { Mfg = true }, null, default);

            Assert.False(File.Exists(Path.Combine(root, ComponentStore.MfgFile)));
        }
        finally
        {
            AppPaths.Root = old;
            try { Directory.Delete(tmp, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Streamline_2_14_0_with_nr_addon_warns_about_menu_slowdown()
    {
        var sl = Set(@"C:\g", "2.14.0.0");
        Assert.Contains(MfgCompat.Plan(sl, Fg9_1, null, false, false, true).Warnings, w => w.Contains("menu slowdown"));
        Assert.DoesNotContain(MfgCompat.Plan(sl, Fg9_1, null, false, false, false).Warnings, w => w.Contains("menu slowdown"));
    }
}
