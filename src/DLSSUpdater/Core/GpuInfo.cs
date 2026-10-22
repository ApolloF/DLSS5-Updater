using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DLSSUpdater.Core;

public enum GpuVendor { Unknown, Nvidia, Amd, Intel }

/// <summary>NVIDIA generation as far as the neural-rendering methods care (decision table in docs/advisor.md).</summary>
public enum RtxTier
{
    /// <summary>No RTX: a GTX card, or a GPU of another vendor.</summary>
    None = 0,
    Rtx20 = 20,
    Rtx30 = 30,
    Rtx40 = 40,
    Rtx50 = 50,
}

/// <summary>The graphics card the advisor judges the methods against. <see cref="Unknown"/> when nothing could be read.</summary>
public sealed partial record GpuInfo(GpuVendor Vendor, RtxTier Tier, string Name)
{
    public static readonly GpuInfo Unknown = new(GpuVendor.Unknown, RtxTier.None, "");

    public bool IsKnown => Vendor != GpuVendor.Unknown;
    public bool IsNvidia => Vendor == GpuVendor.Nvidia;
    public bool IsRtx => Tier != RtxTier.None;

    /// <summary>A card the NVIDIA-only methods can run on; an unreadable GPU counts, so detection trouble never hides a method.</summary>
    public bool MayRunNvidiaMethods => Vendor == GpuVendor.Unknown || (IsNvidia && IsRtx);

    public string Label => !IsKnown ? "GPU not detected" : Name.Length > 0 ? Name : Vendor.ToString();

    [GeneratedRegex(@"\bRTX\s*(?<n>[2-5])\d{3}\b", RegexOptions.IgnoreCase)]
    private static partial Regex RtxNumber();

    /// <summary>
    /// Vendor from the PCI vendor id (0x10DE NVIDIA, 0x1002 or 0x1022 AMD, 0x8086 Intel), else from the name; the RTX
    /// generation from the model number ("RTX 4070" is 40-series). Workstation names are told apart: "Quadro RTX 4000" is Turing,
    /// "RTX A4000" is Ampere, "RTX PRO 6000" is Blackwell.
    /// </summary>
    public static GpuInfo Parse(string name, int? pciVendorId = null)
    {
        name = (name ?? "").Trim();
        var vendor = pciVendorId switch
        {
            0x10DE => GpuVendor.Nvidia,
            0x1002 or 0x1022 => GpuVendor.Amd,
            0x8086 => GpuVendor.Intel,
            _ => FromName(name),
        };
        return new GpuInfo(vendor, vendor == GpuVendor.Nvidia ? TierOf(name) : RtxTier.None, name);
    }

    private static GpuVendor FromName(string name) =>
        name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Nvidia
        : name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) || name.Contains("AMD", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Amd
        : name.Contains("Intel", StringComparison.OrdinalIgnoreCase) || name.Contains("Arc A", StringComparison.OrdinalIgnoreCase) || name.Contains("Arc B", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Intel
        : GpuVendor.Unknown;

    private static RtxTier TierOf(string name)
    {
        if (name.Contains("RTX PRO", StringComparison.OrdinalIgnoreCase)) return RtxTier.Rtx50;
        if (Regex.IsMatch(name, @"\bRTX\s*A\d{3,4}\b", RegexOptions.IgnoreCase)) return RtxTier.Rtx30;
        if (name.Contains("Quadro RTX", StringComparison.OrdinalIgnoreCase) || name.Contains("TITAN RTX", StringComparison.OrdinalIgnoreCase)) return RtxTier.Rtx20;
        if (name.Contains("Ada", StringComparison.OrdinalIgnoreCase) && name.Contains("RTX", StringComparison.OrdinalIgnoreCase)) return RtxTier.Rtx40;
        return RtxNumber().Match(name) is { Success: true } m
            ? m.Groups["n"].Value switch { "2" => RtxTier.Rtx20, "3" => RtxTier.Rtx30, "4" => RtxTier.Rtx40, _ => RtxTier.Rtx50 }
            : RtxTier.None;
    }
}

/// <summary>Reads the installed graphics cards from the display adapter class in the registry.</summary>
public static class GpuDetector
{
    private const string DisplayClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11cd-bff6-08002be10318}";

    /// <summary>The card the games run on: the best NVIDIA card, else any other discrete vendor, else whatever is there.</summary>
    public static GpuInfo Detect()
    {
        try { return Best(ReadAdapters()); }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Info($"GPU not read ({ex.Message})");
            return GpuInfo.Unknown;
        }
    }

    /// <summary>NVIDIA with the newest generation first, then AMD, then Intel (usually the integrated one next to a discrete card).</summary>
    public static GpuInfo Best(IEnumerable<GpuInfo> adapters) =>
        adapters.Where(a => a.IsKnown)
            .OrderBy(a => a.Vendor switch { GpuVendor.Nvidia => 0, GpuVendor.Amd => 1, _ => 2 })
            .ThenByDescending(a => a.Tier)
            .FirstOrDefault() ?? GpuInfo.Unknown;

    private static List<GpuInfo> ReadAdapters()
    {
        var found = new List<GpuInfo>();
        using var cls = Registry.LocalMachine.OpenSubKey(DisplayClass);
        if (cls is null) return found;
        foreach (var sub in cls.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsAsciiDigit)))
        {
            using var key = cls.OpenSubKey(sub);
            if (key?.GetValue("DriverDesc") is not string desc || desc.Length == 0) continue;
            found.Add(GpuInfo.Parse(desc, VendorId(key.GetValue("MatchingDeviceId") as string)));
        }
        return found;
    }

    /// <summary>The vendor id from a hardware id like <c>pci\ven_10de&amp;dev_2684</c>.</summary>
    internal static int? VendorId(string? matchingDeviceId)
    {
        var m = matchingDeviceId is null ? null : Regex.Match(matchingDeviceId, @"ven_([0-9a-f]{4})", RegexOptions.IgnoreCase);
        return m is { Success: true } ? Convert.ToInt32(m.Groups[1].Value, 16) : null;
    }
}
