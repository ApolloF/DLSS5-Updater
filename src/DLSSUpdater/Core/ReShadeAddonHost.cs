using DLSSUpdater.Scan;

namespace DLSSUpdater.Core;

/// <summary>The words an add-on component uses when ReShade isn't ready for it: what it is called in a sentence, and the version complaint.</summary>
public sealed record AddonPurpose(string Name, string NeedsReShade6);

/// <summary>
/// Makes sure a game has ReShade with add-on support before an add-on component goes in (the RenoDX HDR mod, the frame limiters):
/// the managed ReShade install runs when the game has none, but not beside OptiScaler, where the user decides about ReShade.
/// </summary>
public sealed class ReShadeAddonHost(ComponentStore store, Installer installer)
{
    public async Task EnsureAsync(GameInfo game, string targetDir, HdrReShade setup, AddonPurpose purpose, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        var m = InstallManifest.Read(targetDir);
        if (m is { ReShade: true })
        {
            CheckVersion(targetDir, m, purpose);
            return;
        }
        if (m is { Opti: true })
            throw new InvalidOperationException($"OptiScaler is installed here without ReShade. Turn ReShade on in the install above and install again, then add {purpose.Name}.");
        var target = ReShadeNaming.Resolve(game.Effective, optiScalerLoads: false, setup.Proxy, DxvkDlls.InstalledIn(m));
        if (target.VulkanLayer && !VulkanLayer.Status(installer.VulkanHost).IsInstalled)
            throw new InvalidOperationException("This game renders with Vulkan, which needs ReShade's Vulkan layer. Install the layer first (Settings → ReShade → Vulkan layer; one administrator prompt).");
        Log.Info($"{game.Name}: no ReShade yet, installing it as {target.Display} (ReShade + add-ons) for {purpose.Name}");
        var o = new InstallOptions
        {
            Mode = InstallMode.ReShadeOnly, ReShade = true, Proxy = target.DllName ?? ReShadeNaming.Default,
            ReShadeVulkan = target.VulkanLayer, ReShadeBuildPick = setup.BuildPick,
            ReShadeOverrides = setup.Overrides ?? [], ReShadeTemplate = setup.Template ?? [],
            Mfg = m?.Mfg == true, KeepMfg = m?.Mfg == true,
            DlssNr = m?.DlssNr == true, DlssNrId = m?.DlssNrId,
            Streamline = m?.Streamline == true && game.Streamline.Count > 0,
        };
        await installer.InstallAsync(game, targetDir, o, progress, ct);
        CheckVersion(targetDir, InstallManifest.Read(targetDir), purpose);
    }

    private void CheckVersion(string targetDir, InstallManifest? m, AddonPurpose purpose)
    {
        if (m is null) return;
        // Through the Vulkan layer the game holds no dll: the library build the record names is what runs.
        var version = m.ReShadeVulkan
            ? store.ReShadeBuildFor(m.ReShadeId)?.ParsedVersion
            : FileUtil.ReadVersion(Path.Combine(targetDir, m is { Mode: InstallMode.ReShadeOnly, Proxy: { } p } ? p : ComponentStore.ReShadeFile));
        if (version is { Major: < 6 })
            throw new InvalidOperationException(purpose.NeedsReShade6);
    }
}
