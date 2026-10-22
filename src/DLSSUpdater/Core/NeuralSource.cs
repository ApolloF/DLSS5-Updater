namespace DLSSUpdater.Core;

/// <summary>
/// nvngx_dlssnr.dll and Lecram's DLSS5 add-on come from the catalog mirror as raw binaries: downloaded, checked (size, SHA-256,
/// PE header) and kept in the same libraries an import fills, so "Newest", per-game picks and imports work as before.
/// Only the catalog's newest build is fetched on its own; older ones are fetched when the user picks one (CatalogBuildPicker.cs). Offline or
/// refused downloads are logged and change nothing.
/// </summary>
public sealed partial class ComponentStore
{
    public async Task<NrVersion?> CheckDlssNrCatalogAsync(AppSettings settings, CatalogNeuralLine? line, ICatalogFetch fetch,
        IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        if (!settings.AutoDownloadDlssNr || line?.Current is not { } newest || newest.ToBinary() is not { IsInstallable: true }) return null;
        var sha = newest.Sha256.ToLowerInvariant();
        if (sha == settings.CatalogDlssNrFetched || DlssNrVersions.Any(v => v.Id.Equals(sha, StringComparison.OrdinalIgnoreCase))) return null;

        var version = await FetchDlssNrAsync(line, newest, fetch, progress, ct);
        if (version is null) return null;
        settings.CatalogDlssNrFetched = sha;
        if (AdoptNewerDlssNr(settings) is { } adopted) Log.Info($"NR runtime default is now {adopted.Version}");
        return version;
    }

    /// <summary>One catalog build of nvngx_dlssnr.dll into the library: downloaded, size and SHA-256 checked, PE checked. Null (logged) when refused.</summary>
    private Task<NrVersion?> FetchDlssNrAsync(CatalogNeuralLine line, CatalogNeuralBuild build, ICatalogFetch fetch,
        IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        var sha = build.Sha256.ToLowerInvariant();
        var what = $"nvngx_dlssnr.dll {line.LabelOf(build)}";
        return InNeuralWorkAsync(what, sha, async work =>
        {
            var path = Path.Combine(work, DlssNrFile);
            var result = await fetch.DownloadAsync(build.ToBinary(), path, progress, ct);
            if (!result.Ok) { Log.Info($"{what} not downloaded: {result.Refusal}"); return null; }
            RequirePe(path, DlssNrFile);
            if (!IsDlssNr(path)) Log.Warn($"{what}: the file doesn't name itself DLSSNR, but its hash matches the catalog");
            var version = RetainDlssNr(path);
            Log.Info($"{what} downloaded from the catalog");
            return version;
        }, ct);
    }

    /// <summary>The newest Lecram build from the catalog into the Lecram library; the file decides v10 or v11, like an import.</summary>
    public async Task<AddonBuild?> CheckLecramCatalogAsync(AppSettings settings, CatalogNeuralLine? line, ICatalogFetch fetch,
        IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        if (!settings.AutoDownloadLecram || line?.Current is not { } newest || newest.ToBinary() is not { IsInstallable: true }) return null;
        var sha = newest.Sha256.ToLowerInvariant();
        if (sha == settings.CatalogLecramFetched || AddonBuilds(AddonKind.Lecram).Any(b => b.Id.Equals(sha, StringComparison.OrdinalIgnoreCase))) return null;

        var build = await FetchLecramAsync(line, newest, fetch, progress, ct);
        if (build is null) return null;
        settings.CatalogLecramFetched = sha;
        if (AdoptImportedAddon(settings, AddonKind.Lecram, build)) Log.Info($"Lecram's add-on default is now {build.Label}");
        settings.LecramLastResult = $"Downloaded {build.DetectedLabel} from the catalog.";
        return build;
    }

    /// <summary>One catalog build of Lecram's add-on into the library; its own file decides v10 or v11, not the catalog. Null (logged) when refused.</summary>
    private Task<AddonBuild?> FetchLecramAsync(CatalogNeuralLine line, CatalogNeuralBuild build, ICatalogFetch fetch,
        IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        var sha = build.Sha256.ToLowerInvariant();
        var what = $"Lecram's add-on {line.LabelOf(build)}";
        return InNeuralWorkAsync(what, sha, async work =>
        {
            var path = Path.Combine(work, string.IsNullOrWhiteSpace(build.Name) ? Lecram11File : Path.GetFileName(build.Name));
            var result = await fetch.DownloadAsync(build.ToBinary(), path, progress, ct);
            if (!result.Ok) { Log.Info($"{what} not downloaded: {result.Refusal}"); return null; }
            RequirePe(path, Path.GetFileName(path));
            var imported = ImportLecram(path, downloaded: true);
            if (build.Generation is { } g && !g.Equals(imported.Generation.ToString(), StringComparison.OrdinalIgnoreCase))
                Log.Warn($"{what}: the catalog says {g}, the file is {imported.Generation}; going by the file");
            Log.Info($"{what} downloaded from the catalog");
            return imported;
        }, ct);
    }

    private static async Task<T?> InNeuralWorkAsync<T>(string what, string sha, Func<string, Task<T?>> run, CancellationToken ct) where T : class
    {
        var work = Path.Combine(AppPaths.Cache, "neural-" + sha[..12]);
        try
        {
            Directory.CreateDirectory(work);
            return await run(work);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"{what} from the catalog was not usable: {ex.Message}");
            return null;
        }
        finally
        {
            try { FileUtil.DeleteDirectory(work); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>A real PE image: MZ, then the PE signature where the header points. The hash already proves it is the catalog's file.</summary>
    internal static void RequirePe(string path, string name)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var head = new byte[0x40];
        var ok = fs.Read(head, 0, head.Length) == head.Length && head[0] == 'M' && head[1] == 'Z';
        if (ok)
        {
            var pe = BitConverter.ToInt32(head, 0x3C);
            var sig = new byte[4];
            ok = pe is > 0 and < 0x10000 && fs.Seek(pe, SeekOrigin.Begin) == pe && fs.Read(sig, 0, 4) == 4 && sig.AsSpan().SequenceEqual("PE\0\0"u8);
        }
        if (!ok) throw new InvalidDataException($"{name} is not a Windows binary.");
    }
}
