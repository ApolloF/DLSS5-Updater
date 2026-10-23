using System.Text;

namespace DLSSUpdater.Tests;

/// <summary>Builds tiny but valid PE32 / PE32+ files with an import table and a delay-load table, for the detection tests.</summary>
internal static class SyntheticPe
{
    private const int SectionRva = 0x1000;
    private const int SectionRaw = 0x400;
    private const uint ImageBase32 = 0x400000;

    public static byte[] Build(bool pe32Plus, string[] imports, string[]? delayImports = null, bool delayWithVirtualAddresses = false, ushort? machine = null)
    {
        delayImports ??= [];
        var optionalSize = pe32Plus ? 0xF0 : 0xE0;

        // Section content: import descriptors, delay-load descriptors, then the names they point at.
        var importBytes = (imports.Length + 1) * 20;
        var delayBytes = (delayImports.Length + 1) * 32;
        var namesAt = importBytes + delayBytes;
        var names = new MemoryStream();
        var nameRva = new List<uint>();
        foreach (var n in imports.Concat(delayImports))
        {
            nameRva.Add((uint)(SectionRva + namesAt + names.Length));
            names.Write(Encoding.ASCII.GetBytes(n + "\0"));
        }

        var section = new byte[namesAt + names.Length];
        for (var i = 0; i < imports.Length; i++)
            BitConverter.GetBytes(nameRva[i]).CopyTo(section, i * 20 + 12);
        for (var i = 0; i < delayImports.Length; i++)
        {
            var at = importBytes + i * 32;
            var rva = nameRva[imports.Length + i];
            BitConverter.GetBytes(delayWithVirtualAddresses ? 0u : 1u).CopyTo(section, at);
            BitConverter.GetBytes(delayWithVirtualAddresses ? rva + ImageBase32 : rva).CopyTo(section, at + 4);
        }
        names.ToArray().CopyTo(section, namesAt);

        var file = new byte[SectionRaw + section.Length];
        file[0] = (byte)'M';
        file[1] = (byte)'Z';
        BitConverter.GetBytes(0x80).CopyTo(file, 0x3C);
        Encoding.ASCII.GetBytes("PE\0\0").CopyTo(file, 0x80);

        var coff = 0x84;
        BitConverter.GetBytes(machine ?? (pe32Plus ? (ushort)0x8664 : (ushort)0x14c)).CopyTo(file, coff);
        BitConverter.GetBytes((ushort)1).CopyTo(file, coff + 2);
        BitConverter.GetBytes((ushort)optionalSize).CopyTo(file, coff + 16);
        BitConverter.GetBytes((ushort)0x0022).CopyTo(file, coff + 18);

        var opt = coff + 20;
        BitConverter.GetBytes((ushort)(pe32Plus ? 0x20b : 0x10b)).CopyTo(file, opt);
        var dirs = pe32Plus ? opt + 112 : opt + 96;
        if (pe32Plus) BitConverter.GetBytes((ulong)ImageBase32).CopyTo(file, opt + 24);
        else BitConverter.GetBytes(ImageBase32).CopyTo(file, opt + 28);
        BitConverter.GetBytes(0x1000u).CopyTo(file, opt + 32);                       // SectionAlignment
        BitConverter.GetBytes(0x200u).CopyTo(file, opt + 36);                        // FileAlignment
        BitConverter.GetBytes((uint)(SectionRva + 0x1000)).CopyTo(file, opt + 56);   // SizeOfImage
        BitConverter.GetBytes((uint)SectionRaw).CopyTo(file, opt + 60);              // SizeOfHeaders
        BitConverter.GetBytes((ushort)3).CopyTo(file, opt + 68);                     // Subsystem: console
        BitConverter.GetBytes(16u).CopyTo(file, dirs - 4);                           // NumberOfRvaAndSizes

        if (imports.Length > 0)
        {
            BitConverter.GetBytes((uint)SectionRva).CopyTo(file, dirs + 1 * 8);
            BitConverter.GetBytes((uint)importBytes).CopyTo(file, dirs + 1 * 8 + 4);
        }
        if (delayImports.Length > 0)
        {
            BitConverter.GetBytes((uint)(SectionRva + importBytes)).CopyTo(file, dirs + 13 * 8);
            BitConverter.GetBytes((uint)delayBytes).CopyTo(file, dirs + 13 * 8 + 4);
        }

        var sec = opt + optionalSize;
        Encoding.ASCII.GetBytes(".rdata").CopyTo(file, sec);
        BitConverter.GetBytes((uint)section.Length).CopyTo(file, sec + 8);
        BitConverter.GetBytes((uint)SectionRva).CopyTo(file, sec + 12);
        BitConverter.GetBytes((uint)section.Length).CopyTo(file, sec + 16);
        BitConverter.GetBytes((uint)SectionRaw).CopyTo(file, sec + 20);
        BitConverter.GetBytes(0x40000040u).CopyTo(file, sec + 36);

        section.CopyTo(file, SectionRaw);
        return file;
    }
}
