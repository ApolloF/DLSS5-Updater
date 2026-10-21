using System.Reflection.PortableExecutable;
using System.Text;

namespace DLSSUpdater.Scan;

/// <summary>What the loader would pull in for a PE file: its machine type and the dlls in its import and delay-load tables (lower case).</summary>
public sealed record PeFile(int Bits, IReadOnlyList<string> Imports, IReadOnlyList<string> DelayImports);

/// <summary>
/// Reads the import and delay-load tables of a PE32 or PE32+ file. Only the headers and the two tables are read, never the
/// whole (often huge) exe. Anything unreadable, locked, not a PE or malformed returns null instead of throwing.
/// </summary>
public static class PeReader
{
    private const ushort MachineI386 = 0x14c;
    private const int MaxDescriptors = 1024;
    private const int MaxNameLength = 256;

    public static PeFile? Read(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096);
            return Read(fs);
        }
        catch (Exception ex) when (IsReadFailure(ex)) { return null; }
    }

    public static PeFile? Read(Stream s)
    {
        try { return ReadCore(s); }
        catch (Exception ex) when (IsReadFailure(ex)) { return null; }
    }

    private static bool IsReadFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException
            or ArgumentException or OverflowException or NotSupportedException or System.Security.SecurityException;

    private static PeFile? ReadCore(Stream s)
    {
        var headers = new PEHeaders(s);
        var pe = headers.PEHeader;
        if (pe is null) return null;

        var bits = headers.CoffHeader.Machine == (Machine)MachineI386 ? 32 : 64;
        var imageBase = (long)pe.ImageBase;
        var sections = headers.SectionHeaders;
        var headerSize = pe.SizeOfHeaders;

        long? Offset(long rva)
        {
            if (rva <= 0) return null;
            foreach (var sec in sections)
            {
                var size = Math.Max(sec.VirtualSize, sec.SizeOfRawData);
                if (rva >= sec.VirtualAddress && rva < (long)sec.VirtualAddress + size)
                    return sec.PointerToRawData + (rva - sec.VirtualAddress);
            }
            return rva < headerSize ? rva : null;
        }

        var imports = ReadTable(s, Offset, pe.ImportTableDirectory, descriptorSize: 20, nameField: 12, imageBase, delayLoad: false);
        var delays = ReadTable(s, Offset, pe.DelayImportTableDirectory, descriptorSize: 32, nameField: 4, imageBase, delayLoad: true);
        return new PeFile(bits, imports, delays);
    }

    private static List<string> ReadTable(Stream s, Func<long, long?> offset, DirectoryEntry dir, int descriptorSize, int nameField,
        long imageBase, bool delayLoad)
    {
        var names = new List<string>();
        if (dir.RelativeVirtualAddress == 0 || dir.Size == 0 || offset(dir.RelativeVirtualAddress) is not { } start) return names;

        var buf = new byte[descriptorSize];
        for (var i = 0; i < MaxDescriptors; i++)
        {
            if (!ReadAt(s, start + (long)i * descriptorSize, buf)) break;
            var nameRef = BitConverter.ToUInt32(buf, nameField);
            if (nameRef == 0) break;

            long rva = nameRef;
            // A delay-load descriptor with attribute bit 0 clear (very old linkers) stores virtual addresses, not RVAs.
            if (delayLoad && (BitConverter.ToUInt32(buf, 0) & 1) == 0) rva -= imageBase;
            if (offset(rva) is { } at && ReadName(s, at) is { Length: > 0 } name) names.Add(name);
        }
        return names;
    }

    private static bool ReadAt(Stream s, long offset, byte[] buf)
    {
        if (offset < 0 || offset + buf.Length > s.Length) return false;
        s.Seek(offset, SeekOrigin.Begin);
        var read = 0;
        while (read < buf.Length)
        {
            var n = s.Read(buf, read, buf.Length - read);
            if (n <= 0) return false;
            read += n;
        }
        return true;
    }

    private static string? ReadName(Stream s, long offset)
    {
        if (offset < 0 || offset >= s.Length) return null;
        s.Seek(offset, SeekOrigin.Begin);
        var sb = new StringBuilder();
        for (var i = 0; i < MaxNameLength; i++)
        {
            var b = s.ReadByte();
            if (b <= 0) break;
            sb.Append((char)b);
        }
        return sb.ToString().ToLowerInvariant();
    }
}
