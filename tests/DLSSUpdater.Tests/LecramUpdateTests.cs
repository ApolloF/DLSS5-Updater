using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

/// <summary>Lecram's public MediaFire channel against a fake server: no request leaves the machine.</summary>
public sealed class LecramUpdateTests : IDisposable
{
    private const string Folder = "wrwi1qkpgy0qu";
    private const string Download = "https://download1.mediafire.test/";

    private sealed class FakeMediaFire(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (Requests) Requests.Add(request.RequestUri!.ToString());
            return Task.FromResult(respond(request));
        }
    }

    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-lecramup-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly ComponentStore _store = new(new GitHubClient(), () => true);

    public LecramUpdateTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "data");
        AppPaths.Ensure();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); } catch (IOException) { }
    }

    private static string Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static byte[] AddonBytes(string version)
    {
        var bytes = new byte[0x200];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        var text = $"RenoDX.DLSS5\0NR-SETTINGS event=effective\0NR-COMPAT v1 coinstalled=0\0{version}\0{version}NR on\0{Guid.NewGuid()}\0";
        return [.. bytes, .. Encoding.ASCII.GetBytes(text)];
    }

    private static byte[] Zip(params (string Name, byte[] Data)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            foreach (var (name, data) in entries)
            {
                using var s = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
                s.Write(data);
            }
        return ms.ToArray();
    }

    /// <summary>A PE-looking prefix followed by a ZIP. With <paramref name="absolute"/> the ZIP offsets count from the file start.</summary>
    private static byte[] SetupExe(byte[] zip, bool absolute)
    {
        var prefix = new byte[0x1000];
        prefix[0] = (byte)'M';
        prefix[1] = (byte)'Z';
        if (absolute)
        {
            var end = zip.Length - 22;
            while (BitConverter.ToUInt32(zip, end) != 0x06054b50) end--;
            var cd = (int)BitConverter.ToUInt32(zip, end + 16);
            BitConverter.TryWriteBytes(zip.AsSpan(end + 16), (uint)(cd + prefix.Length));
            for (var p = cd; p < end && BitConverter.ToUInt32(zip, p) == 0x02014b50;)
            {
                BitConverter.TryWriteBytes(zip.AsSpan(p + 42), (uint)(BitConverter.ToUInt32(zip, p + 42) + prefix.Length));
                p += 46 + BitConverter.ToUInt16(zip, p + 28) + BitConverter.ToUInt16(zip, p + 30) + BitConverter.ToUInt16(zip, p + 32);
            }
        }
        return [.. prefix, .. zip];
    }

    private sealed record Remote(string Name, byte[] Data, string Created, string? Hash = null)
    {
        public string Key => Name.Replace('.', '_');
    }

    /// <summary>Serves a listing (<paramref name="folders"/> maps folder key to its files), landing pages and downloads.</summary>
    private static FakeMediaFire Server(Dictionary<string, Remote[]> folders, Func<Remote, byte[]>? serve = null, int perChunk = 100) => new(r =>
    {
        var url = r.RequestUri!;
        if (url.Host == "www.mediafire.com" && url.AbsolutePath.StartsWith("/api/"))
        {
            var query = System.Web.HttpUtility.ParseQueryString(url.Query);
            var files = folders[query["folder_key"]!];
            var chunk = int.Parse(query["chunk"]!);
            var page = files.Skip((chunk - 1) * perChunk).Take(perChunk).ToArray();
            var items = string.Join(",", page.Select(f =>
                "{\"quickkey\":\"" + f.Key + "\",\"filename\":\"" + f.Name + "\",\"hash\":\"" + (f.Hash ?? Hex(f.Data)) + "\",\"size\":\"" + f.Data.Length
                + "\",\"created_utc\":\"" + f.Created + "\",\"links\":{\"normal_download\":\"https://www.mediafire.com/file/" + f.Key + "/" + f.Name + "/file\"}}"));
            var more = chunk * perChunk < files.Length ? "yes" : "no";
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"response\":{\"folder_content\":{\"chunk\":\"" + chunk + "\",\"files\":[" + items + "],\"more_chunks\":\"" + more + "\"},\"result\":\"Success\"}}") };
        }
        if (url.Host == "download1.mediafire.test")
        {
            var remote = folders.Values.SelectMany(f => f).First(f => f.Key == url.AbsolutePath.Trim('/'));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(serve?.Invoke(remote) ?? remote.Data) };
        }
        var key = url.Segments[2].Trim('/');
        return new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent($"<a class=\"x\" href=\"{Download}{key}?a=1&amp;b=2\">Download</a>") };
    });

    private static Dictionary<string, Remote[]> Channel(params Remote[] files) => new() { [Folder] = files };

    // ---------- parsing and selection ----------

    [Theory]
    [InlineData("DLSS5-NR-Setup-v11.0.0-beta.24.exe", "v11.0.0-beta.24")]
    [InlineData("DLSS5-NR-Setup-v11.2.3.exe", "v11.2.3")]
    [InlineData("DLSS5-NR-v11.0.0-rc.1.zip", "v11.0.0-rc.1")]
    [InlineData("DLSS5-NR-v12.0.0-alpha.3.zip", "v12.0.0-alpha.3")]
    [InlineData("dlss5-nr-v11.0.1.addon64", "v11.0.1")]
    [InlineData("DLSS5-NR-Setup-v10.9.0.exe", null)]
    [InlineData("DLSS5-NR-v10.7.0.zip", null)]
    [InlineData("DLSS5-NR-Setup-v11.0.0-gamma.1.exe", null)]
    [InlineData("DLSS5-NR-Setup-latest.exe", null)]
    [InlineData("licenses.txt", null)]
    public void VersionOf_ReadsTheNameAndSkipsOldMajors(string name, string? expected) =>
        Assert.Equal(expected, MediaFireClient.VersionOf(name));

    [Fact]
    public void ParseListing_KeepsCandidatesWithHashAndDate_AndReadsMoreChunks()
    {
        var hash = new string('a', 64);
        string File(string name, string h, string created, string q) =>
            "{\"filename\":\"" + name + "\",\"hash\":\"" + h + "\",\"size\":\"1\",\"created_utc\":\"" + created + "\",\"links\":{\"normal_download\":\"https://www.mediafire.com/file/" + q + "/x/file\"}}";
        var body = "{\"response\":{\"folder_content\":{\"files\":["
                   + string.Join(",",
                       File("DLSS5-NR-Setup-v11.0.0-beta.24.exe", hash, "2026-10-18T18:51:16Z", "a"),
                       File("DLSS5-NR-Setup-v11.0.0-beta.25.exe", "", "2026-10-19T18:51:16Z", "b"),
                       File("DLSS5-NR-Setup-v11.0.0-beta.26.exe", hash, "", "c"),
                       File("licenses.txt", hash, "2026-10-18T18:51:16Z", "d"),
                       File("Channel.JSON", hash, "2026-10-18T18:51:16Z", "e"))
                   + "],\"more_chunks\":\"Yes\"}}}";
        var listing = MediaFireClient.ParseListing(body);

        Assert.True(listing.More);
        Assert.Equal(["DLSS5-NR-Setup-v11.0.0-beta.24.exe", "Channel.JSON"], listing.Files.Select(f => f.Name));
        Assert.Equal(new DateTimeOffset(2026, 10, 18, 18, 51, 16, TimeSpan.Zero), listing.Files[0].Created);
    }

    [Fact]
    public void SelectLatest_TakesTheNewestUpload_AndOnATieTheRawAddonThenZipThenExe()
    {
        MediaFireFile F(string name, string created) => new(name, new string('b', 64), DateTimeOffset.Parse(created), "https://x/", MediaFireClient.VersionOf(name),
            name.EndsWith(".addon64") ? 2 : name.EndsWith(".zip") ? 1 : 0);

        var newest = MediaFireClient.SelectLatest([F("DLSS5-NR-Setup-v11.0.0-beta.24.exe", "2026-10-18T10:00:00Z"), F("DLSS5-NR-Setup-v11.0.0-beta.22.exe", "2026-10-17T10:00:00Z")]);
        Assert.Equal("DLSS5-NR-Setup-v11.0.0-beta.24.exe", newest!.Name);

        var tie = MediaFireClient.SelectLatest([F("DLSS5-NR-Setup-v11.0.0-beta.24.exe", "2026-10-18T10:00:00Z"), F("DLSS5-NR-v11.0.0-beta.24.zip", "2026-10-18T10:00:00Z"),
            F("dlss5-nr-v11.0.0-beta.24.addon64", "2026-10-18T10:00:00Z")]);
        Assert.Equal("dlss5-nr-v11.0.0-beta.24.addon64", tie!.Name);
        Assert.Null(MediaFireClient.SelectLatest([]));
    }

    [Fact]
    public void DownloadLink_TakesTheFirstDownloadHost_AndDecodesEntities()
    {
        var html = "<a href=\"https://www.mediafire.com/other\">x</a><a class=\"input\" href=\"https://download2390.mediafire.com/abc/file.exe?x=1&amp;y=2&#38;z=3\">Download</a><a href=\"https://download9.mediafire.com/second\">";
        Assert.Equal("https://download2390.mediafire.com/abc/file.exe?x=1&y=2&z=3", MediaFireClient.DownloadLink(html));
        Assert.Throws<InvalidDataException>(() => MediaFireClient.DownloadLink("<html>captcha</html>"));
    }

    // ---------- extraction ----------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtractAddon_ReadsTheAppendedZip_WhateverTheOffsetsCountFrom(bool absolute)
    {
        var addon = AddonBytes("v11.0.0-beta.24");
        var exe = Path.Combine(_tmp, "setup.exe");
        File.WriteAllBytes(exe, SetupExe(Zip(("package.json", "{\"version\":\"v11.0.0-beta.24\"}"u8.ToArray()), ("dlss5-nr.addon64", addon),
            ("studio/renodx-dlss5.addon64", AddonBytes("v10.0.0"))), absolute));
        var dest = Path.Combine(_tmp, "out", "dlss5-nr.addon64");
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

        MediaFireClient.ExtractAddon(exe, dest);

        Assert.Equal(addon, File.ReadAllBytes(dest));
    }

    [Fact]
    public void ExtractAddon_RefusesAnExeWithoutZip_AndAZipWithoutTheAddon()
    {
        var plain = Path.Combine(_tmp, "plain.exe");
        File.WriteAllBytes(plain, AddonBytes("v11.0.0"));
        Assert.Throws<InvalidDataException>(() => MediaFireClient.ExtractAddon(plain, Path.Combine(_tmp, "o1")));

        var zip = Path.Combine(_tmp, "other.zip");
        File.WriteAllBytes(zip, Zip(("README.txt", "x"u8.ToArray())));
        Assert.Throws<InvalidDataException>(() => MediaFireClient.ExtractAddon(zip, Path.Combine(_tmp, "o2")));
        Assert.False(File.Exists(Path.Combine(_tmp, "o2")));
    }

    // ---------- listing, channel.json, download ----------

    [Fact]
    public async Task Check_ReadsEveryChunkOfTheListing()
    {
        var files = Enumerable.Range(1, 5).Select(i => new Remote($"DLSS5-NR-Setup-v11.0.{i}.exe", AddonBytes("v11.0." + i), $"2026-10-1{i}T10:00:00Z")).ToArray();
        var server = Server(Channel(files), perChunk: 2);

        var result = await new MediaFireClient(server).CheckAsync(Folder, CancellationToken.None);

        Assert.Equal("DLSS5-NR-Setup-v11.0.5.exe", result.Latest!.Name);
        Assert.Equal(3, server.Requests.Count);
        Assert.All(server.Requests, u => Assert.Contains("content_type=files", u));
    }

    [Fact]
    public async Task Check_FollowsChannelJsonOnce_AndRemembersTheNewKey()
    {
        var channel = "{\"notice\":\"Moved.\",\"moved_to\":\"newfolder1\"}"u8.ToArray();
        var folders = new Dictionary<string, Remote[]>
        {
            [Folder] = [new Remote("channel.json", channel, "2026-10-18T10:00:00Z"), new Remote("DLSS5-NR-Setup-v11.0.0-beta.1.exe", AddonBytes("v11.0.0-beta.1"), "2026-10-01T10:00:00Z")],
            ["newfolder1"] = [new Remote("DLSS5-NR-Setup-v11.0.0-beta.24.exe", AddonBytes("v11.0.0-beta.24"), "2026-10-18T10:00:00Z")],
        };

        var result = await new MediaFireClient(Server(folders)).CheckAsync(Folder, CancellationToken.None);

        Assert.Equal("newfolder1", result.Key);
        Assert.Equal("Moved.", result.Notice);
        Assert.Equal("DLSS5-NR-Setup-v11.0.0-beta.24.exe", result.Latest!.Name);
    }

    [Fact]
    public async Task Check_StopsOnAMoveLoopOrASecondMove()
    {
        Remote Move(string to) => new("channel.json", Encoding.UTF8.GetBytes($"{{\"moved_to\":\"{to}\"}}"), "2026-10-18T10:00:00Z");
        var loop = Channel(Move(Folder));
        await Assert.ThrowsAsync<InvalidDataException>(() => new MediaFireClient(Server(loop)).CheckAsync(Folder, CancellationToken.None));

        var chain = new Dictionary<string, Remote[]> { [Folder] = [Move("two")], ["two"] = [Move("three")], ["three"] = [] };
        await Assert.ThrowsAsync<InvalidDataException>(() => new MediaFireClient(Server(chain)).CheckAsync(Folder, CancellationToken.None));
    }

    [Fact]
    public async Task Download_ChecksTheListedSha256()
    {
        var data = AddonBytes("v11.0.0-beta.24");
        var good = new Remote("dlss5-nr.addon64", data, "2026-10-18T10:00:00Z");
        var client = new MediaFireClient(Server(Channel(good)));
        var latest = (await client.CheckAsync(Folder, CancellationToken.None)).Latest!;
        var dest = Path.Combine(_tmp, "dl", "file");

        await client.DownloadAsync(latest, dest, CancellationToken.None);
        Assert.Equal(data, File.ReadAllBytes(dest));

        File.Delete(dest);
        var tampered = new MediaFireClient(Server(Channel(good), serve: _ => [.. data, 1]));
        await Assert.ThrowsAsync<InvalidDataException>(() => tampered.DownloadAsync(latest, dest, CancellationToken.None));
        Assert.False(File.Exists(dest));
    }

    // ---------- the library ----------

    private static Remote SetupRemote(string version, string created, bool absolute = false) =>
        new($"DLSS5-NR-Setup-{version}.exe", SetupExe(Zip(("package.json", Encoding.UTF8.GetBytes($"{{\"version\":\"{version}\"}}")), ("dlss5-nr.addon64", AddonBytes(version))), absolute), created);

    [Fact]
    public async Task Update_AddsTheBuildToTheLibraryAndMakesItTheDefault()
    {
        var settings = new AppSettings();
        var server = Server(Channel(SetupRemote("v11.0.0-beta.24", "2026-10-18T18:51:16Z"), new Remote("licenses.txt", "x"u8.ToArray(), "2026-10-18T18:51:16Z")));

        await _store.CheckLecramAsync(settings, new MediaFireClient(server), CancellationToken.None);

        var build = Assert.Single(_store.LecramBuilds);
        Assert.Equal("v11.0.0-beta.24", build.DetectedLabel);
        Assert.Equal(LecramGeneration.V11, build.Generation);
        Assert.Null(settings.LecramId);
        Assert.Equal(build.Id, _store.LecramBuildFor(null)!.Id);
        Assert.Equal(build.Id, settings.LecramLastBuildId);
        Assert.Equal("v11.0.0-beta.24", settings.LecramLastVersion);
        Assert.Contains("v11.0.0-beta.24", settings.LecramLastResult);
        Assert.Empty(Directory.EnumerateFileSystemEntries(AppPaths.Cache, "lecram-download-*"));

        var requests = server.Requests.Count;
        await _store.CheckLecramAsync(settings, new MediaFireClient(server), CancellationToken.None);
        Assert.Single(_store.LecramBuilds);
        Assert.StartsWith("Up to date", settings.LecramLastResult);
        Assert.Equal(requests + 1, server.Requests.Count);
    }

    [Fact]
    public async Task Update_RecordsTheBuildAsDownloaded_UntilTheUserImportsTheSameFile()
    {
        var settings = new AppSettings();
        var server = Server(Channel(SetupRemote("v11.0.0-beta.24", "2026-10-18T18:51:16Z")));

        await _store.CheckLecramAsync(settings, new MediaFireClient(server), CancellationToken.None);

        var build = Assert.Single(_store.LecramBuilds);
        Assert.True(build.Downloaded);
        Assert.Contains(build.Id, _store.TakeAdded());
        // Imported by hand: from now on it is the user's build, which no automatic cleanup removes.
        var imported = _store.ImportLecram(ComponentStore.AddonPathOf(AddonKind.Lecram, build));
        Assert.False(imported.Downloaded);
        Assert.False(Assert.Single(_store.LecramBuilds).Downloaded);
        Assert.Empty(_store.TakeAdded());
    }

    [Fact]
    public async Task Update_NeverMovesAPinnedDefault()
    {
        var settings = new AppSettings();
        var newer = _store.ImportLecram(WriteAddon("v11.0.0-beta.30"));
        settings.LecramId = newer.Id;

        await _store.CheckLecramAsync(settings, new MediaFireClient(Server(Channel(SetupRemote("v11.0.0-beta.24", "2026-10-18T18:51:16Z")))), CancellationToken.None);

        Assert.Equal(2, _store.LecramBuilds.Count);
        Assert.Equal(newer.Id, settings.LecramId);

        await _store.CheckLecramAsync(settings, new MediaFireClient(Server(Channel(SetupRemote("v11.0.0-beta.31", "2026-10-19T10:00:00Z")))), CancellationToken.None);

        Assert.Equal(3, _store.LecramBuilds.Count);
        Assert.Equal(newer.Id, settings.LecramId);
    }

    [Fact]
    public async Task Update_TakesARawAddonAndAZipToo()
    {
        var settings = new AppSettings();
        var raw = new Remote("dlss5-nr.addon64", AddonBytes("v11.0.0-beta.25"), "2026-10-19T10:00:00Z");
        await _store.CheckLecramAsync(settings, new MediaFireClient(Server(Channel(raw))), CancellationToken.None);
        Assert.Equal("v11.0.0-beta.25", _store.LecramBuildFor(null)!.DetectedLabel);

        var zip = new Remote("DLSS5-NR-v11.0.0-beta.26.zip", Zip(("dlss5-nr.addon64", AddonBytes("v11.0.0-beta.26"))), "2026-10-20T10:00:00Z");
        await _store.CheckLecramAsync(settings, new MediaFireClient(Server(Channel(raw, zip))), CancellationToken.None);
        Assert.Equal("v11.0.0-beta.26", _store.LecramBuildFor(settings.LecramId)!.DetectedLabel);
    }

    [Fact]
    public async Task Update_RefusesAHashMismatch_AndKeepsTheLibrary()
    {
        var settings = new AppSettings();
        var kept = _store.ImportLecram(WriteAddon("v11.0.0-beta.6"));
        settings.LecramId = kept.Id;
        var bad = SetupRemote("v11.0.0-beta.24", "2026-10-18T18:51:16Z") with { Hash = new string('0', 64) };

        await _store.CheckLecramAsync(settings, new MediaFireClient(Server(Channel(bad))), CancellationToken.None);

        Assert.Equal([kept.Id], _store.LecramBuilds.Select(b => b.Id));
        Assert.Equal(kept.Id, settings.LecramId);
        Assert.StartsWith("Check failed", settings.LecramLastResult);
    }

    [Fact]
    public async Task Update_AnExeWithoutZip_OrANonLecramFile_IsLoggedAndSkipped()
    {
        var settings = new AppSettings();
        var noZip = new Remote("DLSS5-NR-Setup-v11.0.0-beta.24.exe", AddonBytes("v11.0.0-beta.24"), "2026-10-18T18:51:16Z");
        await _store.CheckLecramAsync(settings, new MediaFireClient(Server(Channel(noZip))), CancellationToken.None);
        Assert.Empty(_store.LecramBuilds);
        Assert.StartsWith("Check failed", settings.LecramLastResult);

        var notLecram = new Remote("dlss5-nr.addon64", [(byte)'M', (byte)'Z', 0, 0], "2026-10-18T18:51:16Z");
        await _store.CheckLecramAsync(settings, new MediaFireClient(Server(Channel(notLecram))), CancellationToken.None);
        Assert.Empty(_store.LecramBuilds);
        Assert.StartsWith("Check failed", settings.LecramLastResult);
    }

    [Fact]
    public async Task Update_WhenOffline_KeepsTheLibrary_AndNeverThrows()
    {
        var settings = new AppSettings();
        var kept = _store.ImportLecram(WriteAddon("v11.0.0-beta.6"));
        settings.LecramId = kept.Id;
        var offline = new FakeMediaFire(_ => throw new HttpRequestException("no network"));

        await _store.CheckLecramAsync(settings, new MediaFireClient(offline), CancellationToken.None);

        Assert.Equal([kept.Id], _store.LecramBuilds.Select(b => b.Id));
        Assert.StartsWith("Check failed", settings.LecramLastResult);
    }

    [Fact]
    public async Task Update_UsesTheCachedListingOffline_WithoutDownloading()
    {
        var settings = new AppSettings();
        var channel = Channel(SetupRemote("v11.0.0-beta.24", "2026-10-18T18:51:16Z"));
        await new MediaFireClient(Server(channel)).CheckAsync(Folder, CancellationToken.None);
        var offline = new FakeMediaFire(_ => throw new HttpRequestException("no network"));

        await _store.CheckLecramAsync(settings, new MediaFireClient(offline), CancellationToken.None);

        Assert.Empty(_store.LecramBuilds);
        Assert.StartsWith("Offline", settings.LecramLastResult);
        Assert.Single(offline.Requests);
    }

    [Fact]
    public async Task Update_SettingOff_SendsNoRequest()
    {
        var settings = new AppSettings { AutoDownloadLecram = false };
        var server = Server(Channel(SetupRemote("v11.0.0-beta.24", "2026-10-18T18:51:16Z")));

        await _store.CheckLecramAsync(settings, new MediaFireClient(server), CancellationToken.None);

        Assert.Empty(server.Requests);
        Assert.Empty(_store.LecramBuilds);
    }

    [Fact]
    public async Task Update_SettingOff_StillChecksWhenForced()
    {
        var settings = new AppSettings { AutoDownloadLecram = false };
        var server = Server(Channel(SetupRemote("v11.0.0-beta.24", "2026-10-18T18:51:16Z")));

        await _store.CheckLecramAsync(settings, new MediaFireClient(server), CancellationToken.None, force: true);

        Assert.NotEmpty(server.Requests);
        Assert.Single(_store.LecramBuilds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Update_ChannelOff_SendsNoRequestAndAddsNothing_EvenWhenForced(bool force)
    {
        var settings = new AppSettings { AutoDownloadLecram = true };
        var server = Server(Channel(SetupRemote("v11.0.0-beta.24", "2026-10-18T18:51:16Z")));

        await _store.CheckLecramAsync(settings, new MediaFireClient(server), CancellationToken.None, force, channelEnabled: false);

        Assert.Empty(server.Requests);
        Assert.Empty(_store.LecramBuilds);
        Assert.True(settings.AutoDownloadLecram);
    }

    [Fact]
    public async Task Update_ChannelOn_BehavesAsBefore()
    {
        var settings = new AppSettings();
        var server = Server(Channel(SetupRemote("v11.0.0-beta.24", "2026-10-18T18:51:16Z")));

        await _store.CheckLecramAsync(settings, new MediaFireClient(server), CancellationToken.None, force: true, channelEnabled: true);

        Assert.NotEmpty(server.Requests);
        Assert.Single(_store.LecramBuilds);
    }

    [Fact]
    public async Task Update_RemembersAMovedChannel()
    {
        var settings = new AppSettings();
        var folders = new Dictionary<string, Remote[]>
        {
            [Folder] = [new Remote("channel.json", "{\"moved_to\":\"newfolder1\"}"u8.ToArray(), "2026-10-18T10:00:00Z")],
            ["newfolder1"] = [SetupRemote("v11.0.0-beta.24", "2026-10-18T18:51:16Z")],
        };

        await _store.CheckLecramAsync(settings, new MediaFireClient(Server(folders)), CancellationToken.None);

        Assert.Equal("newfolder1", settings.LecramFolderKey);
        Assert.Single(_store.LecramBuilds);
    }

    [Fact]
    public void Settings_RoundTripTheLecramUpdateFields()
    {
        var s = new AppSettings { AutoDownloadLecram = false, LecramFolderKey = "abc", LecramLastResult = "r", LecramLastVersion = "v11.0.0", LecramLastAssetHash = "h", LecramLastBuildId = "i" };
        var back = JsonSerializer.Deserialize(JsonSerializer.Serialize(s, JsonCtx.Default.AppSettings), JsonCtx.Default.AppSettings)!;

        Assert.False(back.AutoDownloadLecram);
        Assert.Equal(("abc", "r", "v11.0.0", "h", "i"), (back.LecramFolderKey, back.LecramLastResult, back.LecramLastVersion, back.LecramLastAssetHash, back.LecramLastBuildId));
        Assert.True(new AppSettings().AutoDownloadLecram);
        Assert.Equal(MediaFireClient.DefaultFolder, new AppSettings().LecramFolderKey);
    }

    [Theory]
    [InlineData("v11.0.0-beta.22", "v11.0.0-beta.24")]
    [InlineData("v11.0.0-beta.9", "v11.0.0-beta.10")]
    [InlineData("v11.0.0-beta.24", "v11.0.0-rc.1")]
    [InlineData("v11.0.0-rc.1", "v11.0.0")]
    public void Order_RanksPreReleasesByStageAndNumber(string older, string newer)
    {
        AddonBuild B(string label) => new(Hex(Encoding.UTF8.GetBytes(label)), "dlss5-nr.addon64", "", LecramGeneration.V11, label, null, DateTime.UtcNow);
        Assert.True(B(older).Order.CompareTo(B(newer).Order) < 0);
    }

    private string WriteAddon(string version)
    {
        var path = Path.Combine(_tmp, "imports", Guid.NewGuid().ToString("N")[..6], ComponentStore.Lecram11File);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, AddonBytes(version));
        return path;
    }
}
