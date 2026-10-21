using System.Text.Json;
using System.Text.Json.Nodes;

namespace DLSSUpdater.Core;

/// <summary>
/// What the Vulkan layer install does to the machine, behind an interface so the logic can be tested without admin rights, a
/// registry or ProgramData. Paths are full paths; <paramref name="bits"/> is 64 or 32 (the 32-bit layer is registered in the
/// WOW6432Node view, as ReShade's own setup does).
/// </summary>
public interface IVulkanLayerHost
{
    /// <summary>C:\ProgramData\ReShade, where ReShade's own setup keeps the layer too.</summary>
    string LayerDir { get; }
    bool FileExists(string path);
    string? ReadText(string path);
    void Copy(string source, string dest);
    void WriteText(string path, string text);
    void Delete(string path);
    /// <summary>Removes the folder when nothing is left in it.</summary>
    void DeleteDirectoryIfEmpty(string dir);
    string Sha256(string path);
    /// <summary>Lets packaged (UWP / Store) apps read the layer files, which ReShade's setup does too.</summary>
    void AllowAppPackages(string dir);
    bool IsRegistered(string manifestPath, int bits);
    void Register(string manifestPath, int bits);
    void Unregister(string manifestPath, int bits);
}

public enum VulkanLayerState
{
    NotInstalled,
    /// <summary>Installed by this app and complete.</summary>
    Installed,
    /// <summary>Registered, but by ReShade's own setup (or another tool): this app does not touch it.</summary>
    InstalledByOther,
    /// <summary>Some of our files or registry values are missing; Install repairs it.</summary>
    Incomplete,
}

public sealed record VulkanLayerStatus(VulkanLayerState State, string? Version, string Text)
{
    public bool IsInstalled => State is VulkanLayerState.Installed or VulkanLayerState.InstalledByOther;
}

/// <summary>The files an install copies: the build's DLLs and the hashes the copies in ProgramData must end up with.</summary>
public sealed record VulkanLayerFiles(string? Dll64, string? Sha64, string? Dll32, string? Sha32, string Version);

/// <summary>A refusal with the exit code the elevated process reports it by (<see cref="VulkanLayerCommand"/>).</summary>
public sealed class VulkanLayerException(string message, int code = VulkanLayerCommand.Refused) : Exception(message)
{
    public int Code { get; } = code;
}

/// <summary>
/// ReShade's Vulkan support is an implicit layer: the DLLs and a JSON manifest per bitness live in C:\ProgramData\ReShade, and
/// the manifest's path is a DWORD value 0 under HKLM\SOFTWARE\Khronos\Vulkan\ImplicitLayers (32-bit: the WOW6432Node view).
/// The layer is loaded into every Vulkan game, but ReShade only starts in one that has a ReShade.ini beside its exe (source/dll_main.cpp),
/// so the per-game install is that ini. The manifest text is the one in crosire/reshade's setup\ReShade64.json.
/// </summary>
public static class VulkanLayer
{
    public const string LayerName = "VK_LAYER_reshade";
    /// <summary>Our own record next to the layer, so a layer installed by ReShade's setup is never removed by us.</summary>
    public const string MarkerFile = "DLSS5Updater.layer.json";

    public static string DllName(int bits) => bits == 32 ? ReShadeBuild.File32 : ReShadeBuild.File64;
    public static string ManifestName(int bits) => bits == 32 ? "ReShade32.json" : "ReShade64.json";
    public static string ManifestPath(IVulkanLayerHost host, int bits) => Path.Combine(host.LayerDir, ManifestName(bits));

    public static string Manifest(int bits) => new JsonObject
    {
        ["file_format_version"] = "1.0.0",
        ["layer"] = new JsonObject
        {
            ["name"] = LayerName,
            ["type"] = "GLOBAL",
            ["library_path"] = ".\\" + DllName(bits),
            ["api_version"] = "1.3.268",
            ["implementation_version"] = "1",
            ["description"] = $"crosire's ReShade post-processing injector for {bits}-bit",
            ["device_extensions"] = new JsonArray(new JsonObject
            {
                ["name"] = "VK_EXT_tooling_info",
                ["spec_version"] = "1",
                ["entrypoints"] = new JsonArray("vkGetPhysicalDeviceToolPropertiesEXT"),
            }),
            ["disable_environment"] = new JsonObject { ["DISABLE_VK_LAYER_reshade_1"] = "1" },
        },
    }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    public static VulkanLayerStatus Status(IVulkanLayerHost host)
    {
        var marker = ReadMarker(host);
        var registered64 = host.IsRegistered(ManifestPath(host, 64), 64);
        var registered32 = host.IsRegistered(ManifestPath(host, 32), 32);
        if (marker is null)
            return registered64 || registered32
                ? new(VulkanLayerState.InstalledByOther, null, "The ReShade Vulkan layer is installed by ReShade's own setup; this app leaves it alone.")
                : new(VulkanLayerState.NotInstalled, null, "Not installed. Vulkan games need it (one administrator prompt).");

        var complete = marker.Dll64 is null || (host.FileExists(Path.Combine(host.LayerDir, DllName(64))) && host.FileExists(ManifestPath(host, 64)) && registered64);
        complete &= marker.Dll32 is null || (host.FileExists(Path.Combine(host.LayerDir, DllName(32))) && host.FileExists(ManifestPath(host, 32)) && registered32);
        return complete
            ? new(VulkanLayerState.Installed, marker.Version, $"Installed: ReShade {marker.Version} ({Bits(marker)}).")
            : new(VulkanLayerState.Incomplete, marker.Version, "Installed only in part (a file or registry entry is missing). Install again to repair it.");
    }

    /// <summary>Copies the DLLs, writes the manifests, registers them. The copies must hash to the given values or nothing stays.</summary>
    public static void Install(IVulkanLayerHost host, VulkanLayerFiles files)
    {
        if (files.Dll64 is null && files.Dll32 is null) throw new VulkanLayerException("This ReShade build has no DLL to install as a layer.");
        if (Status(host).State == VulkanLayerState.InstalledByOther)
            throw new VulkanLayerException("The Vulkan layer was installed by ReShade's own setup. Remove it there first, or keep using it.", VulkanLayerCommand.OtherOwner);
        host.AllowAppPackages(host.LayerDir);
        var done = new List<int>();
        try
        {
            foreach (var (bits, src, sha) in new[] { (64, files.Dll64, files.Sha64), (32, files.Dll32, files.Sha32) })
            {
                if (src is null) continue;
                if (sha is null) throw new VulkanLayerException($"No SHA-256 for {DllName(bits)}.");
                var dest = Path.Combine(host.LayerDir, DllName(bits));
                // Hashed after the copy, so a file swapped between the check and the copy cannot get in.
                host.Copy(src, dest);
                if (!host.Sha256(dest).Equals(sha, StringComparison.OrdinalIgnoreCase))
                {
                    host.Delete(dest);
                    throw new VulkanLayerException($"{DllName(bits)} does not match the build it should be a copy of; nothing was installed.");
                }
                host.WriteText(ManifestPath(host, bits), Manifest(bits));
                host.Register(ManifestPath(host, bits), bits);
                done.Add(bits);
            }
            // Replacing an older install: a bitness this build lacks is no longer ours.
            foreach (var bits in new[] { 64, 32 }.Except(done)) RemoveBits(host, bits);
            host.WriteText(Path.Combine(host.LayerDir, MarkerFile), new JsonObject
            {
                ["version"] = files.Version,
                ["dll64"] = files.Dll64 is null ? null : files.Sha64,
                ["dll32"] = files.Dll32 is null ? null : files.Sha32,
            }.ToJsonString());
        }
        catch
        {
            foreach (var bits in done) RemoveBits(host, bits);
            host.DeleteDirectoryIfEmpty(host.LayerDir);
            throw;
        }
    }

    /// <summary>Unregisters and deletes what this app installed; a layer from ReShade's setup is refused.</summary>
    public static void Remove(IVulkanLayerHost host)
    {
        var status = Status(host);
        if (status.State == VulkanLayerState.NotInstalled) return;
        if (status.State == VulkanLayerState.InstalledByOther)
            throw new VulkanLayerException("The Vulkan layer was installed by ReShade's own setup; remove it there.", VulkanLayerCommand.OtherOwner);
        foreach (var bits in new[] { 64, 32 }) RemoveBits(host, bits);
        host.Delete(Path.Combine(host.LayerDir, MarkerFile));
        host.DeleteDirectoryIfEmpty(host.LayerDir);
    }

    private static void RemoveBits(IVulkanLayerHost host, int bits)
    {
        var manifest = ManifestPath(host, bits);
        host.Unregister(manifest, bits);
        host.Delete(manifest);
        host.Delete(Path.Combine(host.LayerDir, DllName(bits)));
    }

    private sealed record Marker(string Version, string? Dll64, string? Dll32);

    private static string Bits(Marker m) => m.Dll64 is not null && m.Dll32 is not null ? "64-bit and 32-bit" : m.Dll64 is not null ? "64-bit" : "32-bit";

    private static Marker? ReadMarker(IVulkanLayerHost host)
    {
        try
        {
            return host.ReadText(Path.Combine(host.LayerDir, MarkerFile)) is { } text && JsonNode.Parse(text) is JsonObject o
                ? new Marker(o["version"]?.GetValue<string>() ?? "?", o["dll64"]?.GetValue<string>(), o["dll32"]?.GetValue<string>())
                : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return null; }
    }

}
