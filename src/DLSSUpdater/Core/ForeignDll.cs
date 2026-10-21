using System.Diagnostics;
using System.Text;

namespace DLSSUpdater.Core;

public enum DllKind { Missing, ReShade, OptiScaler, Dxvk, SpecialK, Enb, Unknown }

/// <summary>What a dll sitting under a proxy name is, and what told us.</summary>
public sealed record DllIdentity(DllKind Kind, string? Version, string Evidence)
{
    public string Name => Kind switch
    {
        DllKind.ReShade => "ReShade",
        DllKind.OptiScaler => "OptiScaler",
        DllKind.Dxvk => "DXVK",
        DllKind.SpecialK => "Special K",
        DllKind.Enb => "ENB",
        DllKind.Missing => "nothing",
        _ => "an unknown dll",
    };

    /// <summary>Not ours to overwrite silently: anything but ReShade itself (an update) or an empty slot.</summary>
    public bool IsForeign => Kind is not (DllKind.Missing or DllKind.ReShade);
}

/// <summary>
/// Tells which mod a dll under a proxy name (dxgi.dll, d3d9.dll ...) belongs to, before ReShade is placed there. Signs, checked
/// against real builds (see docs/addons/reshade.md):
/// <list type="bullet">
/// <item>ReShade: version resource ProductName "ReShade" (OriginalFilename ReShade64.dll / ReShade32.dll); strings "reshade.me" and "crosire".</item>
/// <item>OptiScaler: OriginalFilename "OptiScaler.dll".</item>
/// <item>DXVK: ProductName "DXVK" (also what ReShade's own setup looks for); the string DXVK_CONFIG_FILE.</item>
/// <item>Special K: ProductName "Special K", InternalName SpecialK64.dll / SpecialK32.dll; the exported SK_ImGui_* / SK_Render_* symbols.</item>
/// <item>ENB: no real binary was available to check. ENB's own documentation puts enblocal.ini / enbseries.ini beside its wrapper, so those files (or ENB in the version resource or strings) mark it.</item>
/// </list>
/// Anything else is "unknown". The scan reads at most <see cref="ScanLimit"/> bytes.
/// </summary>
public static class ForeignDll
{
    public const int ScanLimit = 64 * 1024 * 1024;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (long Length, DateTime Written, DllIdentity Found)> Seen = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What the dll is. Remembered per file size and write time: the game page asks for every game on every refresh.</summary>
    public static DllIdentity Identify(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) return new(DllKind.Missing, null, "");
        DllIdentity found;
        if (Seen.TryGetValue(info.FullName, out var known) && known.Length == info.Length && known.Written == info.LastWriteTimeUtc) found = known.Found;
        else Seen[info.FullName] = (info.Length, info.LastWriteTimeUtc, found = Scan(path));
        // ENB's companion files are checked every time: they come and go without the dll changing.
        var dir = info.DirectoryName!;
        return found.Kind == DllKind.Unknown && (File.Exists(Path.Combine(dir, "enblocal.ini")) || File.Exists(Path.Combine(dir, "enbseries.ini")))
            ? new(DllKind.Enb, found.Version, "enblocal.ini / enbseries.ini beside it")
            : found;
    }

    private static DllIdentity Scan(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            var product = info.ProductName?.Trim() ?? "";
            var original = info.OriginalFilename?.Trim() ?? "";
            var internalName = info.InternalName?.Trim() ?? "";
            var company = info.CompanyName?.Trim() ?? "";
            var version = string.IsNullOrWhiteSpace(info.FileVersion) ? null : info.FileVersion.Trim();

            if (original.Equals("OptiScaler.dll", StringComparison.OrdinalIgnoreCase))
                return new(DllKind.OptiScaler, version, "version resource: OptiScaler.dll");
            if (product.Equals("ReShade", StringComparison.OrdinalIgnoreCase) || original.StartsWith("ReShade", StringComparison.OrdinalIgnoreCase))
                return new(DllKind.ReShade, version, "version resource: ReShade");
            if (product.Equals("DXVK", StringComparison.OrdinalIgnoreCase))
                return new(DllKind.Dxvk, version, "version resource: DXVK");
            if (product.Equals("Special K", StringComparison.OrdinalIgnoreCase) || internalName.StartsWith("SpecialK", StringComparison.OrdinalIgnoreCase))
                return new(DllKind.SpecialK, version, "version resource: Special K");
            if (product.StartsWith("ENB", StringComparison.OrdinalIgnoreCase) || company.Contains("Vorontsov", StringComparison.OrdinalIgnoreCase))
                return new(DllKind.Enb, version, "version resource: ENB");

            var data = Read(path);
            if (Has(data, "SpecialK64.dll") || Has(data, "SpecialK32.dll") || (Has(data, "SK_ImGui_") && Has(data, "Special K")))
                return new(DllKind.SpecialK, version, "Special K symbols and strings");
            if (Has(data, "DXVK_CONFIG_FILE") || Has(data, "dxvk.conf"))
                return new(DllKind.Dxvk, version, "DXVK strings");
            if (Has(data, "reshade.me") && Has(data, "crosire"))
                return new(DllKind.ReShade, version, "ReShade strings");
            if (Has(data, "OptiScaler.ini") && Has(data, "OptiScaler"))
                return new(DllKind.OptiScaler, version, "OptiScaler strings");

            if (HasNoCase(data, "enbseries") || HasNoCase(data, "enblocal.ini"))
                return new(DllKind.Enb, version, "ENB strings");
            return new(DllKind.Unknown, version, "no known mod found in it");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(DllKind.Unknown, null, "could not be read: " + ex.Message);
        }
    }

    /// <summary>
    /// The one line shown before ReShade replaces a dll that is not ReShade, or null when nothing needs saying.
    /// The original is backed up either way (the install record restores it on uninstall).
    /// </summary>
    public static string? Warning(string fileName, DllIdentity found)
    {
        if (!found.IsForeign) return null;
        return found.Kind == DllKind.Unknown
            ? $"{fileName} here is a dll I don't recognise (it may belong to another mod); it is backed up and restored on uninstall."
            : $"{fileName} here belongs to {found.Name}; installing ReShade replaces it (backed up, restored on uninstall) and {found.Name} stops working.";
    }

    private static byte[] Read(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buffer = new byte[Math.Min(fs.Length, ScanLimit)];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = fs.Read(buffer, read, buffer.Length - read);
            if (n == 0) break;
            read += n;
        }
        return read == buffer.Length ? buffer : buffer[..read];
    }

    private static bool Has(byte[] data, string ascii) => data.AsSpan().IndexOf(Encoding.ASCII.GetBytes(ascii)) >= 0;

    private static bool HasNoCase(byte[] data, string ascii)
    {
        var needle = Encoding.ASCII.GetBytes(ascii.ToLowerInvariant());
        for (var i = 0; i <= data.Length - needle.Length; i++)
        {
            var j = 0;
            while (j < needle.Length && Lower(data[i + j]) == needle[j]) j++;
            if (j == needle.Length) return true;
        }
        return false;
    }

    private static byte Lower(byte b) => b is >= (byte)'A' and <= (byte)'Z' ? (byte)(b | 0x20) : b;
}
