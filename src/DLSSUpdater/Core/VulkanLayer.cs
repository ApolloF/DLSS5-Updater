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
    /// <summary>Renames a staged file over its destination.</summary>
    void Move(string from, string to);
    /// <summary>Removes the folder when nothing is left in it.</summary>
    void DeleteDirectoryIfEmpty(string dir);
    string Sha256(string path);
    /// <summary>
    /// Creates the folder with a protected DACL (SYSTEM and Administrators full control, Users and ALL APPLICATION PACKAGES read+execute;
    /// the latter lets Store games load the layer) or tightens an existing one. Throws <see cref="VulkanLayerException"/> when an
    /// existing folder is owned by anyone but Administrators or SYSTEM: a standard user can pre-create folders in ProgramData.
    /// </summary>
    void SecureLayerDir(string dir);
    /// <summary>Throws <see cref="VulkanLayerException"/> unless the file is owned by SYSTEM or Administrators and not writable by others.</summary>
    void VerifyFile(string path);
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
        host.SecureLayerDir(host.LayerDir);
        var before = ReadMarker(host);
        var done = new List<int>();
        try
        {
            foreach (var (bits, src, sha) in new[] { (64, files.Dll64, files.Sha64), (32, files.Dll32, files.Sha32) })
            {
                if (src is null) continue;
                if (sha is null) throw new VulkanLayerException($"No SHA-256 for {DllName(bits)}.");
                var dest = Path.Combine(host.LayerDir, DllName(bits));
                // Staged and hashed after the copy, so a file swapped between the check and the copy cannot get in, and a
                // failed copy leaves the layer that was working untouched.
                var manifest = ManifestPath(host, bits);
                host.WriteText(manifest, Manifest(bits));
                host.VerifyFile(manifest);
                if (host.ReadText(manifest) != Manifest(bits))
                    throw new VulkanLayerException($"{ManifestName(bits)} does not read back as written; nothing was installed.");
                var staged = dest + ".new";
                host.Copy(src, staged);
                if (!host.Sha256(staged).Equals(sha, StringComparison.OrdinalIgnoreCase))
                {
                    host.Delete(staged);
                    throw new VulkanLayerException($"{DllName(bits)} does not match the build it should be a copy of; nothing was installed.");
                }
                try { host.VerifyFile(staged); }
                catch
                {
                    host.Delete(staged);
                    throw;
                }
                host.Move(staged, dest);
                host.VerifyFile(dest);
                host.Register(manifest, bits);
                done.Add(bits);
            }
            // Replacing an older install: a bitness this build lacks is no longer ours.
            foreach (var bits in new[] { 64, 32 }.Except(done)) RemoveBits(host, bits);
            WriteMarker(host, files.Version, files.Dll64 is null ? null : files.Sha64, files.Dll32 is null ? null : files.Sha32);
        }
        catch
        {
            KeepWhatWorks(host, before, done, files);
            throw;
        }
    }

    /// <summary>
    /// After a failed install: a bitness that was not there before goes again; one that was working before stays (new build if it
    /// got replaced, the old one if it did not), and the record says which version each bitness is.
    /// </summary>
    private static void KeepWhatWorks(IVulkanLayerHost host, Marker? before, List<int> done, VulkanLayerFiles files)
    {
        var keep64 = before?.Dll64 is not null && host.FileExists(Path.Combine(host.LayerDir, DllName(64)));
        var keep32 = before?.Dll32 is not null && host.FileExists(Path.Combine(host.LayerDir, DllName(32)));
        if (!keep64) RemoveBits(host, 64);
        if (!keep32) RemoveBits(host, 32);
        if (before is null || (!keep64 && !keep32))
        {
            host.Delete(Path.Combine(host.LayerDir, MarkerFile));
            host.DeleteDirectoryIfEmpty(host.LayerDir);
            return;
        }
        var sha64 = done.Contains(64) ? files.Sha64 : keep64 ? before.Dll64 : null;
        var sha32 = done.Contains(32) ? files.Sha32 : keep32 ? before.Dll32 : null;
        var allNew = done.Contains(64) == keep64 && done.Contains(32) == keep32;
        WriteMarker(host, allNew ? files.Version : $"{before.Version} and {files.Version}", sha64, sha32);
    }

    private static void WriteMarker(IVulkanLayerHost host, string version, string? sha64, string? sha32) =>
        host.WriteText(Path.Combine(host.LayerDir, MarkerFile), new JsonObject
        {
            ["version"] = version,
            ["dll64"] = sha64,
            ["dll32"] = sha32,
        }.ToJsonString());

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
