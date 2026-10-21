using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;

namespace DLSSUpdater.Core;

/// <summary>The real machine: C:\ProgramData\ReShade and HKLM. Only the elevated process (<see cref="VulkanLayerCommand"/>) writes through it.</summary>
public sealed class WindowsVulkanLayerHost : IVulkanLayerHost
{
    private const string ImplicitLayers64 = @"SOFTWARE\Khronos\Vulkan\ImplicitLayers";
    private const string ImplicitLayers32 = @"SOFTWARE\Wow6432Node\Khronos\Vulkan\ImplicitLayers";

    public string LayerDir { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ReShade");

    public bool FileExists(string path) => File.Exists(path);

    public string? ReadText(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

    public void Copy(string source, string dest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        FileUtil.AtomicCopy(source, dest);
    }

    public void WriteText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        FileUtil.AtomicWriteText(path, text);
    }

    public void Delete(string path) => FileUtil.DeleteFile(path);

    public void DeleteDirectoryIfEmpty(string dir) => FileUtil.TryDeleteEmptyDir(dir);

    public string Sha256(string path) => FileUtil.Sha256(path);

    public void AllowAppPackages(string dir)
    {
        Directory.CreateDirectory(dir);
        // ALL APPLICATION PACKAGES: without it a Store (UWP) game cannot load the layer.
        var sid = new SecurityIdentifier("S-1-15-2-1");
        var info = new DirectoryInfo(dir);
        var security = info.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.ReadAndExecute,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        info.SetAccessControl(security);
    }

    private static RegistryKey Hklm() => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);

    private static string KeyPath(int bits) => bits == 32 ? ImplicitLayers32 : ImplicitLayers64;

    public bool IsRegistered(string manifestPath, int bits)
    {
        using var hklm = Hklm();
        using var key = hklm.OpenSubKey(KeyPath(bits));
        return key?.GetValue(manifestPath) is int value && value == 0;
    }

    public void Register(string manifestPath, int bits)
    {
        using var hklm = Hklm();
        using var key = hklm.CreateSubKey(KeyPath(bits));
        key.SetValue(manifestPath, 0, RegistryValueKind.DWord);
    }

    public void Unregister(string manifestPath, int bits)
    {
        using var hklm = Hklm();
        using var key = hklm.OpenSubKey(KeyPath(bits), writable: true);
        key?.DeleteValue(manifestPath, throwOnMissingValue: false);
    }
}

/// <summary>
/// The command line of the elevated process: <c>--vulkan-layer install &lt;build folder&gt; &lt;sha64|-&gt; &lt;sha32|-&gt; &lt;version&gt;</c> or
/// <c>--vulkan-layer remove</c>. The app is started with it through "runas" for this one action; the rest of the app never
/// runs elevated. The process only installs DLLs that pass the ReShade check and hash to the given values, and reports by exit code.
/// </summary>
public static class VulkanLayerCommand
{
    public const string Verb = "--vulkan-layer";
    public const int Ok = 0, BadArguments = 2, Refused = 3, OtherOwner = 4, Failed = 5;

    public static int Run(string[] args, IVulkanLayerHost host)
    {
        var i = Array.IndexOf(args, Verb);
        if (i < 0 || i + 1 >= args.Length) return BadArguments;
        try
        {
            switch (args[i + 1])
            {
                case "remove":
                    VulkanLayer.Remove(host);
                    return Ok;
                case "install" when args.Length >= i + 6:
                    var dir = args[i + 2];
                    string? Sha(string a) => a == "-" ? null : a;
                    var (sha64, sha32, version) = (Sha(args[i + 3]), Sha(args[i + 4]), args[i + 5]);
                    if (!Directory.Exists(dir) || !Path.IsPathFullyQualified(dir)) return BadArguments;
                    string? Dll(int bits, string? sha)
                    {
                        if (sha is null) return null;
                        var path = Path.Combine(dir, VulkanLayer.DllName(bits));
                        if (!File.Exists(path)) throw new VulkanLayerException($"{VulkanLayer.DllName(bits)} is missing from the build folder.");
                        var check = ReShadeSource.Check(path, bits);
                        if (!check.Ok) throw new VulkanLayerException($"{VulkanLayer.DllName(bits)} is not a ReShade DLL: {check.Error}.");
                        return path;
                    }
                    VulkanLayer.Install(host, new VulkanLayerFiles(Dll(64, sha64), sha64, Dll(32, sha32), sha32, version));
                    return Ok;
                default:
                    return BadArguments;
            }
        }
        catch (VulkanLayerException ex) { return ex.Code; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) { return Failed; }
    }

    public static string MessageFor(int exitCode) => exitCode switch
    {
        Ok => "",
        BadArguments => "The elevated step was started with bad arguments.",
        Refused => "The layer was not changed: a file did not match its ReShade build.",
        OtherOwner => "The Vulkan layer belongs to ReShade's own setup; this app leaves it alone.",
        _ => "Windows refused the change (registry or ProgramData). Nothing was left half-installed that the next try can't repair.",
    };

    /// <summary>
    /// Starts this exe elevated for one install or removal and waits for it. Declining the UAC prompt is
    /// <see cref="OperationCanceledException"/>; any other failure a <see cref="VulkanLayerException"/> with a plain message.
    /// </summary>
    public static void RunElevated(IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add(Verb);
        foreach (var a in arguments) start.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(start) ?? throw new VulkanLayerException("The elevated step could not be started.", Failed);
            p.WaitForExit();
            if (p.ExitCode != Ok) throw new VulkanLayerException(MessageFor(p.ExitCode), p.ExitCode);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("The administrator prompt was declined.");
        }
    }

    public static IReadOnlyList<string> InstallArguments(string buildDir, ReShadeBuild build) =>
        ["install", buildDir, build.Sha64 ?? "-", build.Sha32 ?? "-", build.Version];

    public static IReadOnlyList<string> RemoveArguments => ["remove"];
}
