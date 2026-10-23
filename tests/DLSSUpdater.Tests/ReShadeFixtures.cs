using System.IO.Compression;
using System.Net;
using System.Text;
using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

/// <summary>Fake ReShade builds for the ReShade tests: real PE files (a system dll of the right bitness) with ReShade's version resource.</summary>
internal static class ReShadeFixtures
{
    private static string BaseFile(int bits) => Path.Combine(
        bits == 32 ? Environment.GetFolderPath(Environment.SpecialFolder.SystemX86) : Environment.SystemDirectory, "version.dll");

    /// <summary>Writes a ReShade64.dll / ReShade32.dll look-alike; <paramref name="tag"/> makes two builds of one version differ.</summary>
    public static string Dll(string dir, int bits, string version, string tag = "")
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, bits == 32 ? ReShadeBuild.File32 : ReShadeBuild.File64);
        FakeVersionDll.Write(path, System.Version.Parse(version), Path.GetFileName(path), tag.Length > 0 ? tag : null, BaseFile(bits), "ReShade", "crosire");
        return path;
    }

    /// <summary>A folder with both DLLs.</summary>
    public static string Folder(string root, string name, string version, string tag = "", bool only64 = false)
    {
        var dir = Path.Combine(root, name);
        Dll(dir, 64, version, tag);
        if (!only64) Dll(dir, 32, version, tag);
        return dir;
    }

    /// <summary>ReShade's setup shape: a stub exe (over 500 KB) with a zip appended.</summary>
    public static byte[] Setup(string dllFolder, bool only64 = false)
    {
        var zip = new MemoryStream();
        using (var a = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in new[] { ReShadeBuild.File32, ReShadeBuild.File64, "ReShade64.json" })
            {
                if (only64 && name == ReShadeBuild.File32) continue;
                var src = Path.Combine(dllFolder, name);
                using var w = a.CreateEntry(name).Open();
                w.Write(File.Exists(src) ? File.ReadAllBytes(src) : Encoding.ASCII.GetBytes("{}"));
            }
        }
        var stub = new byte[600 * 1024];
        stub[0] = (byte)'M';
        stub[1] = (byte)'Z';
        return [.. stub, .. zip.ToArray()];
    }

    public static byte[] Zip(string dllFolder, string name)
    {
        var zip = new MemoryStream();
        using (var a = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var w = a.CreateEntry(name).Open();
            w.Write(File.ReadAllBytes(Path.Combine(dllFolder, name)));
        }
        return zip.ToArray();
    }

    public static string Sha(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    public const string SitePage = "<a href=\"/downloads/ReShade_Setup_6.8.0.exe\">plain</a> <a href=\"/downloads/ReShade_Setup_6.8.0_Addon.exe\">addon</a>";

    public sealed class FakeNet(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (Requests) Requests.Add($"{request.Method} {request.RequestUri}");
            return Task.FromResult(respond(request));
        }
    }

    public static HttpResponseMessage Bytes(byte[] body) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
    public static HttpResponseMessage Text(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
}
