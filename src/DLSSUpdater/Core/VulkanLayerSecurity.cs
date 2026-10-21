using System.Security.AccessControl;
using System.Security.Principal;

namespace DLSSUpdater.Core;

/// <summary>One access rule of a folder or file, reduced to what the layer's trust decision needs.</summary>
public sealed record LayerAce(string Sid, bool Allow, bool CanWrite);

/// <summary>
/// The layer folder is loaded into every Vulkan process, so only SYSTEM and Administrators may own or write it. ProgramData lets
/// a standard user create folders and own what they create, so an existing folder or file is only trusted when its owner is one of those.
/// </summary>
public static class VulkanLayerSecurity
{
    public const string System = "S-1-5-18";
    public const string Administrators = "S-1-5-32-544";
    public const string Users = "S-1-5-32-545";
    public const string AllAppPackages = "S-1-15-2-1";

    public static bool IsTrustedOwner(string? ownerSid) => ownerSid is System or Administrators;

    /// <summary>Null when the owner and the rules are acceptable, otherwise the reason to refuse.</summary>
    public static string? Refusal(string what, string? ownerSid, IEnumerable<LayerAce> rules)
    {
        if (!IsTrustedOwner(ownerSid))
            return $"{what} is owned by another account than Administrators or SYSTEM, so the Vulkan layer was not installed. Delete it as an administrator and try again.";
        if (rules.Any(r => r.Allow && r.CanWrite && !IsTrustedOwner(r.Sid)))
            return $"{what} can be changed by non-administrators, so the Vulkan layer was not installed.";
        return null;
    }

    public static IReadOnlyList<LayerAce> Describe(FileSystemSecurity security) =>
        security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
            .Select(r => new LayerAce(r.IdentityReference.Value, r.AccessControlType == AccessControlType.Allow,
                (r.FileSystemRights & (FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.WriteAttributes
                    | FileSystemRights.WriteExtendedAttributes | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles
                    | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership)) != 0))
            .ToList();

    public static string? OwnerSid(FileSystemSecurity security) =>
        security.GetOwner(typeof(SecurityIdentifier))?.Value;

    /// <summary>Creates or tightens the folder: protected DACL (no inheritance), full control for SYSTEM and Administrators, read+execute for Users and ALL APPLICATION PACKAGES (a Store game cannot load the layer without the latter).</summary>
    public static void SecureDirectory(string dir)
    {
        var info = new DirectoryInfo(dir);
        if (info.Exists)
        {
            var existing = info.GetAccessControl();
            // Writable-by-others rules don't matter here: the DACL is replaced below. Only the owner decides.
            if (!IsTrustedOwner(OwnerSid(existing)))
                throw new VulkanLayerException(Refusal("The folder " + dir, OwnerSid(existing), [])!);
        }
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags both = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        foreach (var sid in new[] { System, Administrators })
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid), FileSystemRights.FullControl, both, PropagationFlags.None, AccessControlType.Allow));
        foreach (var sid in new[] { Users, AllAppPackages })
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid), FileSystemRights.ReadAndExecute, both, PropagationFlags.None, AccessControlType.Allow));
        if (info.Exists) info.SetAccessControl(security);
        else info.Create(security);

        // A race (someone replaced the folder between the check and now) shows here.
        var applied = new DirectoryInfo(dir).GetAccessControl();
        if (Refusal("The folder " + dir, OwnerSid(applied), Describe(applied)) is { } why) throw new VulkanLayerException(why);
    }

    /// <summary>Throws unless <paramref name="path"/> is owned by SYSTEM/Administrators and nobody else can write it.</summary>
    public static void VerifyFile(string path)
    {
        var security = new FileInfo(path).GetAccessControl();
        if (Refusal(Path.GetFileName(path), OwnerSid(security), Describe(security)) is { } why) throw new VulkanLayerException(why);
    }

    /// <summary>
    /// Copies/writes through a temp file beside the target without ever reusing one that is already there: a planted
    /// <c>.dlssu-tmp</c> is deleted first and the new one is created with CreateNew, so its content and owner are ours.
    /// </summary>
    public static void WriteFresh(string dest, Action<Stream> write)
    {
        var tmp = dest + ".dlssu-tmp";
        FileUtil.DeleteFile(tmp);
        try
        {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(fs);
                fs.Flush(flushToDisk: true);
            }
            File.Move(tmp, dest, true);
        }
        catch
        {
            FileUtil.TryDeleteFile(tmp);
            throw;
        }
    }

    public static void CopyFresh(string source, string dest) =>
        WriteFresh(dest, fs => { using var src = File.OpenRead(source); src.CopyTo(fs); });

    public static void WriteTextFresh(string dest, string text) =>
        WriteFresh(dest, fs => fs.Write(new System.Text.UTF8Encoding(false).GetBytes(text)));
}
