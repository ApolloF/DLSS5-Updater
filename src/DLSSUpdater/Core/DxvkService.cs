using DLSSUpdater.Scan;

namespace DLSSUpdater.Core;

/// <summary>What stands in the way of DXVK in one game. Blockers can't be confirmed away (two dlls can't share a name); warnings can.</summary>
public sealed record DxvkCheck(IReadOnlyList<string> Blockers, IReadOnlyList<string> Warnings, bool SwitchReShade)
{
    public bool CanInstall => Blockers.Count == 0;
    public bool NeedsConfirm => Warnings.Count > 0;
}

/// <summary>The warnings need the user's OK first; the game page shows them and asks (anti-cheat, a dll that belongs to another mod, ReShade moving to its Vulkan layer).</summary>
public sealed class DxvkConfirmException(IReadOnlyList<string> warnings)
    : InvalidOperationException("DXVK is only installed after you confirm: " + string.Join(" ", warnings))
{
    public IReadOnlyList<string> Warnings { get; } = warnings;
}

/// <summary>When DXVK applies and what it clashes with. Pure: reads the folder and the install record, writes nothing.</summary>
public static class DxvkRules
{
    /// <summary>Why DXVK has nothing to do in a game, or null when it applies.</summary>
    public static string? NotApplicable(GameInfo game)
    {
        var primary = game.Effective.PrimaryApi;
        if (DxvkDlls.Applies(primary)) return null;
        return primary switch
        {
            GraphicsApis.None => "The graphics API of this game isn't known, so the dlls DXVK needs can't be chosen. Set the API under the detection overrides.",
            GraphicsApis.Vulkan => "This game already renders with Vulkan; DXVK only translates Direct3D 8 to 11.",
            GraphicsApis.OpenGL => "This game renders with OpenGL; DXVK only translates Direct3D 8 to 11.",
            _ => "This game renders with DirectX 12; DXVK only translates Direct3D 8 to 11.",
        };
    }

    public static DxvkCheck Check(GameInfo game, string targetDir, InstallManifest? m, IVulkanLayerHost layerHost)
    {
        var blockers = new List<string>();
        var warnings = new List<string>();
        if (NotApplicable(game) is { } why) return new DxvkCheck([why], [], false);

        targetDir = FileUtil.Normalize(targetDir);
        var ours = m?.Component(ComponentRegistry.Dxvk);
        var switchReShade = false;
        if (game.AntiCheat is { } ac && ours is null)
            warnings.Add($"{game.Name} has {ac} anti-cheat. DXVK replaces graphics dlls the game loads; anti-cheat may block the game or ban the account. Only use it offline or in single player.");
        foreach (var dll in DxvkDlls.For(game.Effective.PrimaryApi))
        {
            var full = Path.Combine(targetDir, dll);
            var rel = InstallManifest.Entry(full, targetDir, FileUtil.Normalize(game.Root));
            if (ours?.FileAt(rel) is not null) continue;
            if (m is not null && m.Owns(rel))
            {
                if (m is { ReShade: true, ReShadeVulkan: false, Mode: InstallMode.ReShadeOnly } && string.Equals(m.Proxy, dll, StringComparison.OrdinalIgnoreCase))
                {
                    switchReShade = true;
                    warnings.Add($"ReShade is installed here as {dll}, the name DXVK needs. ReShade is moved to its Vulkan layer (it keeps working on top of DXVK) and {dll} becomes DXVK's.");
                }
                else blockers.Add($"{dll} belongs to {OwnerOf(m, dll)}; DXVK can't use the same name. Remove that first.");
                continue;
            }
            if (!File.Exists(full)) continue;
            var found = ForeignDll.Identify(full);
            if (found.Kind == DllKind.Missing) continue;
            warnings.Add(found.Kind switch
            {
                DllKind.Dxvk => $"{dll} here is a DXVK that this app didn't install; it is replaced (backed up, put back when DXVK is removed).",
                DllKind.Unknown => $"{dll} here is a dll I don't recognise (it may belong to another mod); it is replaced (backed up, put back when DXVK is removed).",
                _ => $"{dll} here belongs to {found.Name}; installing DXVK replaces it (backed up, put back when DXVK is removed) and {found.Name} stops working.",
            });
        }
        if (switchReShade && !VulkanLayer.Status(layerHost).IsInstalled)
            blockers.Add("ReShade has to move to its Vulkan layer to share this game with DXVK, and the layer isn't installed yet (Settings → ReShade → Vulkan layer; one administrator prompt).");
        return new DxvkCheck(blockers, warnings, switchReShade);
    }

    private static string OwnerOf(InstallManifest m, string dll) =>
        m.Opti && string.Equals(m.Proxy, dll, StringComparison.OrdinalIgnoreCase) ? "OptiScaler"
        : m.Components.FirstOrDefault(c => c.FileAt(dll) is not null) is { } c ? ComponentRegistry.Find(c.Id)?.Name ?? c.Id
        : "another mod this app installed";
}

/// <param name="BuildPick">The game's pick (a library build id or a channel), else the Settings default; null = the Stable channel's newest.</param>
/// <param name="Tier">Lilium preset id; null = the safest tier, <see cref="DxvkConf.TierOff"/> = no HDR keys.</param>
/// <param name="ReShadeSwitch">The options that reproduce the game's ReShade install; the service installs them again with the Vulkan layer when DXVK needs the proxy name.</param>
/// <param name="Force">An explicit Apply: the preset's keys replace what dxvk.conf holds. False for an update.</param>
public sealed record DxvkRequest(
    GameInfo Game,
    string TargetDir,
    NvidiaGame Driver,
    string? BuildPick,
    string? Tier,
    bool PresentMethod,
    string? UserConf,
    InstallOptions? ReShadeSwitch,
    bool Force = true,
    bool Confirmed = false);

public sealed record DxvkResult(LibraryBuild Build, IReadOnlyList<ComponentUndo> Undone, IReadOnlyList<string> Notes);

/// <summary>
/// Installs DXVK in a game: the dlls of the game's graphics API for its bitness, a dxvk.conf, and for the Lilium build the NVIDIA present method.
/// What can fail (no build, a download, a clash) is checked before anything changes. ReShade is moved to its Vulkan layer first when it sits on a name
/// DXVK needs. Removal undoes the record, then puts the driver profile back.
/// </summary>
public sealed class DxvkService(Installer installer, GitHubClient github, NvidiaProfile nvidia)
{
    private readonly DxvkLibrary _library = new(github);

    public bool NvidiaAvailable => nvidia.Available;

    public DxvkCheck Check(GameInfo game, string targetDir) => DxvkRules.Check(game, targetDir, InstallManifest.Read(FileUtil.Normalize(targetDir)), installer.VulkanHost);

    public async Task<DxvkResult> ApplyAsync(DxvkRequest r, string? defaultPick, Action saveSettings, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        var targetDir = FileUtil.Normalize(r.TargetDir);
        var manifest = InstallManifest.Read(targetDir);
        var existing = manifest?.Component(ComponentRegistry.Dxvk);
        var check = DxvkRules.Check(r.Game, targetDir, manifest, installer.VulkanHost);
        if (!check.CanInstall) throw new InvalidOperationException(string.Join(" ", check.Blockers));
        if (check.NeedsConfirm && !r.Confirmed) throw new DxvkConfirmException(check.Warnings);

        var notes = new List<string>();
        var undone = new List<ComponentUndo>();
        // Prepare: the build and the plan come first, so a download or a mistake in the extra lines changes nothing in the game.
        var build = await ResolveAsync(r.BuildPick, defaultPick, notes, progress, ct);
        var present = DxvkLibrary.HasLiliumOptions(build) && r.PresentMethod;
        var plan = DxvkPlanner.Plan(r.Game, targetDir, build, r.Tier, present, r.UserConf, r.Force, existing);

        if (check.SwitchReShade)
        {
            var options = r.ReShadeSwitch ?? throw new InvalidOperationException("ReShade has to move to its Vulkan layer first, but its install options aren't known.");
            progress?.Report(new TransferProgress($"Moving ReShade to its Vulkan layer in {r.Game.Name}", null));
            await installer.InstallAsync(r.Game, targetDir, options with { ReShadeVulkan = true }, progress, ct);
            notes.Add("ReShade now loads through its Vulkan layer on top of DXVK.");
        }

        progress?.Report(new TransferProgress($"Installing {build.Label} to {r.Game.Name}", null));
        undone.Add(await installer.InstallComponentAsync(r.Game, targetDir, plan, ct));

        notes.AddRange(DriverProfile(r, present));
        saveSettings();
        return new DxvkResult(build, undone, notes);
    }

    /// <summary>
    /// Takes DXVK out, puts back the dlls it replaced and the driver profile values it changed. When DXVK had moved ReShade to the Vulkan
    /// layer, ReShade is installed again under the proxy name of the game's API (<paramref name="reShadeBack"/> = the options of its install),
    /// so a Direct3D game that has a ReShade.ini still has the dll that loads it.
    /// </summary>
    public async Task<ComponentUndo> RemoveAsync(GameInfo game, string targetDir, NvidiaGame driver, InstallOptions? reShadeBack, Action saveSettings, CancellationToken ct)
    {
        targetDir = FileUtil.Normalize(targetDir);
        var undo = await installer.RemoveComponentAsync(game, targetDir, ComponentRegistry.Dxvk, ct);
        if (nvidia.Available) nvidia.RestoreLiliumPresentMethod(driver);
        saveSettings();
        if (InstallManifest.Read(targetDir) is { ReShade: true, ReShadeVulkan: true } && ReShadeNaming.ForApi(game.Effective.PrimaryApi) is { } proxy)
        {
            if (reShadeBack is null)
                Log.Info($"{game.Name}: ReShade still loads through its Vulkan layer. Install ReShade again to give it the {proxy} name back.");
            else
            {
                await installer.InstallAsync(game, targetDir, reShadeBack with { ReShadeVulkan = false, Proxy = proxy }, null, ct);
                Log.Info($"{game.Name}: ReShade is installed as {proxy} again.");
            }
        }
        return undo;
    }

    /// <summary>The build an install uses: the pinned one from the library, or the newest of the followed channel (downloaded when the library lacks it, else the library's own newest when offline).</summary>
    private async Task<LibraryBuild> ResolveAsync(string? pick, string? defaultPick, List<string> notes, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        var chosen = pick ?? defaultPick;
        if (chosen is not null && !DxvkChannels.IsChannel(chosen))
            return DxvkLibrary.Find(chosen) ?? throw new InvalidOperationException("The chosen DXVK build is no longer in the library. Pick another build or import it again.");

        var channel = DxvkChannels.Parse(chosen) ?? DxvkChannel.Stable;
        var have = DxvkLibrary.Choose(chosen);
        var latest = await _library.LatestAsync(channel, ct);
        if (latest is not null)
        {
            try { return await _library.EnsureAsync(latest, progress, ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested && have is not null && ex is HttpRequestException or IOException)
            {
                notes.Add($"{latest.Label} could not be downloaded ({ex.Message}); the newest build in the library is used.");
            }
        }
        return have ?? throw new InvalidOperationException(
            $"No {DxvkChannels.Label(channel)} DXVK build is in the library and GitHub can't be reached or has none usable. Import a DXVK release in Settings, or try again online.");
    }

    /// <summary>The Lilium build runs with the driver's present method layered on DXGI; any other build gives back what an earlier Lilium install changed.</summary>
    private IEnumerable<string> DriverProfile(DxvkRequest r, bool present)
    {
        if (!nvidia.Available)
            return present ? ["No NVIDIA driver found, so the present method was not set. Lilium HDR is made for NVIDIA's driver; AMD and Intel may need nothing."] : [];
        if (!present)
        {
            nvidia.RestoreLiliumPresentMethod(r.Driver);
            return [];
        }
        NvidiaResult result;
        try { result = nvidia.SetLiliumPresentMethod(r.Driver); }
        catch (NvApiException ex)
        {
            // The write is one session saved at the end, so a refusal leaves the profile as it was.
            return [$"The NVIDIA driver refused the present method settings ({ex.Message}), so they were not set. DXVK is installed without them."];
        }
        if (result.Verified) return ["The NVIDIA driver profile of this game now presents Vulkan layered on the DXGI swap chain and treats DXVK as native (put back when DXVK is removed)."];
        nvidia.RestoreLiliumPresentMethod(r.Driver);
        return ["The NVIDIA driver didn't keep the present method settings, so they were put back. DXVK is installed without them."];
    }
}
