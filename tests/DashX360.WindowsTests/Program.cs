using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using XboxMetroLauncher.Models;
using XboxMetroLauncher.Services;
using XboxMetroLauncher.Utilities;

namespace DashX360.WindowsTests;

internal static class Program
{
    private static int failures;
    private static int checks;
    private static string root = "";
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--child")) { Thread.Sleep(30000); return 0; }
        root = Path.Combine(Path.GetTempPath(), "dashx360-windows-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("DASHX360_USER_DATA_FOLDER", root);
        try
        {
            Check("WPF images retain and refresh their bindings across repeated suspension", BindingRestores);
            Check("Partial settings import preserves profile, library and other settings", PartialImport);
            Check("Malformed, null and future backups leave live JSON unchanged", InvalidImports);
            Check("Theme traversal and invalid image payloads do not publish files", UnsafeThemes);
            Check("Backup round trip preserves volume, friends and portable game artwork", RoundTrip);
            Check("A library load does not rewrite its persisted JSON", LibraryReadOnly);
            Check("Steam keys are protected and friend requests span all batches", SteamFriends);
            Check("An unrelated process is never tracked or closed", UnrelatedProcess);
            Check("Force close requires confirmation for the verified process", ForceClose);
        }
        finally { Directory.Delete(root, recursive: true); }
        Console.WriteLine($"{checks - failures}/{checks} Windows regression checks passed.");
        return failures == 0 ? 0 : 1;
    }
    private static void Check(string name, Func<Task> action)
    {
        checks++;
        try { action().GetAwaiter().GetResult(); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + ex); }
    }
    private static void Assert(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
    private static BitmapSource Pixel(byte value) => BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { value, 0, 0, 255 }, 4);
    private sealed class ImageModel : INotifyPropertyChanged
    {
        public BitmapSource? Value { get; set; }
        public event PropertyChangedEventHandler? PropertyChanged;
        public void Notify() => PropertyChanged?.Invoke(this, new(nameof(Value)));
    }
    private static Task BindingRestores()
    {
        var model = new ImageModel { Value = Pixel(1) };
        var image = new Image();
        BindingOperations.SetBinding(image, Image.SourceProperty, new Binding(nameof(ImageModel.Value)) { Source = model });
        for (var i = 0; i < 4; i++)
        {
            ImageResources.Suspend(image);
            Assert(image.Source == null && BindingOperations.IsDataBound(image, Image.SourceProperty));
            ImageResources.Resume(image);
            Assert(ReferenceEquals(image.Source, model.Value));
            model.Value = Pixel((byte)(i + 2)); model.Notify();
            Assert(ReferenceEquals(image.Source, model.Value));
        }
        return Task.CompletedTask;
    }
    private sealed record Fixture(string Root, JsonStore Store, JsonGameLibraryService Library, ProfileService Profile, SettingsService Settings, ImportExportService Importer);
    private static async Task<Fixture> CreateFixture()
    {
        var path = Path.Combine(root, Guid.NewGuid().ToString("N"));
        var store = new JsonStore(path);
        var library = new JsonGameLibraryService(store);
        var profile = new ProfileService(store);
        var settings = new SettingsService(store);
        await library.SaveAsync(new GameLibrary { Games = new() { new GameMetadata { Id = "game", Title = "Existing", ExecutablePath = @"C:\Games\Existing.exe" } } });
        await profile.SaveAsync(new Profile { Gamertag = "Original", Gamerscore = 42 });
        await settings.SaveAsync(new AppSettings { ThemeName = "Original theme", DashboardVolume = .37, StartFullscreen = true });
        return new(path, store, library, profile, settings, new ImportExportService(library, profile, settings, path));
    }
    private static async Task<DashboardImportResult> Import(Fixture fixture, string json)
    {
        var path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, json);
        return await fixture.Importer.ImportAsync(path);
    }
    private static async Task PartialImport()
    {
        var fixture = await CreateFixture();
        var result = await Import(fixture, "{\"settings\":{\"ThemeName\":\"New theme\"}}");
        Assert(result.Success, result.Message);
        var settings = await fixture.Settings.LoadAsync();
        Assert(settings.ThemeName == "New theme" && settings.StartFullscreen && Math.Abs(settings.DashboardVolume - .37) < .001);
        Assert((await fixture.Library.LoadAsync()).Games.Single().Title == "Existing");
        Assert((await fixture.Profile.LoadAsync()).Gamertag == "Original");
        var safety = JsonSerializer.Deserialize<DashboardBackup>(await File.ReadAllTextAsync(result.SafetyBackupPath!))!;
        Assert(safety.Settings.ThemeName == "Original theme");
        Assert((await Import(fixture, "{\"CustomThemes\":[]}")).Success);
        Assert((await fixture.Library.LoadAsync()).Games.Count == 1);
    }
    private static async Task InvalidImports()
    {
        var fixture = await CreateFixture();
        var before = await File.ReadAllTextAsync(Path.Combine(fixture.Root, "library.json"));
        foreach (var json in new[] { "{", "{\"ExportVersion\":\"9\"}", "{\"Library\":null}", "{\"Library\":{\"Games\":[null]}}", "{\"CustomThemes\":[null]}" })
        {
            Assert(!(await Import(fixture, json)).Success);
            Assert(await File.ReadAllTextAsync(Path.Combine(fixture.Root, "library.json")) == before);
        }
    }
    private static async Task UnsafeThemes()
    {
        var fixture = await CreateFixture();
        foreach (var name in new[] { "..", "../escape", "CON", "bad." })
        {
            var result = await Import(fixture, JsonSerializer.Serialize(new { CustomThemes = new[] { new { FolderName = name, Name = "Bad" } } }));
            Assert(!result.Success);
        }
        var invalidImage = await Import(fixture, "{\"CustomThemes\":[{\"FolderName\":\"ValidName\",\"HomeImageFileName\":\"home.png\",\"HomeImageBase64\":\"bm90LWFuLWltYWdl\"}]}");
        Assert(!invalidImage.Success);
        Assert(!File.Exists(Path.Combine(fixture.Root, "Assets", "Custom Files", "Themes", "ValidName", "theme.json")));
    }
    private static async Task RoundTrip()
    {
        var fixture = await CreateFixture();
        var imagePath = Path.Combine(fixture.Root, "cover.png");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(Pixel(42)));
        using (var stream = File.Create(imagePath)) encoder.Save(stream);
        var library = await fixture.Library.LoadAsync(); library.Games[0].CoverArtPath = imagePath;
        await fixture.Library.SaveAsync(library);
        await fixture.Store.WriteAsync("friends.json", new FriendsData { Friends = new() { new FriendProfile { Gamertag = "Friend" } } });
        var backup = Path.Combine(root, "roundtrip.json");
        await fixture.Importer.ExportAsync(library, await fixture.Profile.LoadAsync(), await fixture.Settings.LoadAsync(), backup);
        var destination = await CreateFixture();
        var result = await destination.Importer.ImportAsync(backup);
        Assert(result.Success, result.Message);
        var restored = (await destination.Library.LoadAsync()).Games.Single();
        Assert(restored.CoverArtPath.StartsWith(destination.Root) && File.Exists(restored.CoverArtPath));
        Assert((await destination.Store.ReadAsync<FriendsData>("friends.json"))!.Friends.Count == 1);
        Assert(Math.Abs((await destination.Settings.LoadAsync()).DashboardVolume - .37) < .001);
    }
    private static async Task LibraryReadOnly()
    {
        var fixture = await CreateFixture(); var path = Path.Combine(fixture.Root, "library.json");
        var stamp = DateTime.UtcNow.AddDays(-1); File.SetLastWriteTimeUtc(path, stamp);
        await fixture.Library.LoadAsync(); Assert(File.GetLastWriteTimeUtc(path) == stamp);
    }
    private static async Task SteamFriends()
    {
        var data = Path.Combine(root, "steam");
        using var handler = new SteamHandler(); using var http = new HttpClient(handler);
        var service = new SteamCommunityService(http, data);
        var config = new SteamCommunityConfig { SteamId64 = "76561198000000001", SteamApiKey = "test-key-never-store-plaintext" };
        await service.SaveConfigAsync(config);
        Assert(!(await File.ReadAllTextAsync(Path.Combine(data, "steam-web-config.json"))).Contains(config.SteamApiKey));
        Assert((await service.LoadConfigAsync()).SteamApiKey == config.SteamApiKey);
        Assert((await service.LoadFriendsAsync()).Count == 205 && handler.Batches == 3);
        var cache = Path.Combine(data, "SteamCache", "Accounts", config.SteamId64, "friends.json");
        await File.WriteAllTextAsync(cache, "{broken");
        Assert((await service.LoadFriendsAsync()).Count == 205 && handler.Batches == 6);
        config.SteamId64 = "76561198000000002"; await service.SaveConfigAsync(config);
        Assert((await service.LoadFriendsAsync()).Count == 205 && handler.Batches == 9);
    }
    private sealed class SteamHandler : HttpMessageHandler
    {
        public int Batches;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var uri = request.RequestUri!;
            object body;
            if (uri.AbsolutePath.Contains("GetFriendList"))
                body = new { friendslist = new { friends = Enumerable.Range(0, 205).Select(i => new { steamid = (76561198100000000UL + (ulong)i).ToString() }) } };
            else if (uri.AbsolutePath.Contains("GetPlayerSummaries"))
            {
                Batches++;
                var ids = Uri.UnescapeDataString(uri.Query.Split("steamids=")[1]).Split(',');
                Assert(ids.Length <= 100);
                body = new { response = new { players = ids.Select(id => new { steamid = id, personaname = "Shared display name", personastate = 1 }) } };
            }
            else throw new Exception("Unexpected HTTP request: " + uri.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") });
        }
    }
    private static Process Child() => Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--child") { UseShellExecute = false, CreateNoWindow = true })!;
    private static async Task UnrelatedProcess()
    {
        using var service = new RunningGameService();
        var game = new GameMetadata { Title = "Unrelated", ExecutablePath = @"C:\not-a-game\missing.exe" };
        service.BeginLaunch(game, DateTimeOffset.UtcNow.AddSeconds(-20));
        using var child = Child(); var id = child.Id;
        try
        {
            service.Track(game, Process.GetProcessById(id));
            Assert(!service.HasTrackedProcess);
            Assert(!(await service.CloseAsync(true)).Success);
            Assert(!child.HasExited);
        }
        finally { if (!child.HasExited) child.Kill(); await child.WaitForExitAsync(); }
    }
    private static async Task ForceClose()
    {
        using var service = new RunningGameService();
        var game = new GameMetadata { Title = "Test child", ExecutablePath = Environment.ProcessPath! };
        service.BeginLaunch(game, DateTimeOffset.UtcNow);
        using var child = Child();
        try
        {
            service.Track(game, Process.GetProcessById(child.Id));
            Assert(service.HasTrackedProcess);
            var unconfirmed = await service.CloseAsync(true);
            Assert(unconfirmed.RequiresForceConfirmation && !child.HasExited);
            var confirmed = await service.CloseAsync(true);
            Assert(confirmed.Success, confirmed.Message);
            await child.WaitForExitAsync();
        }
        finally { if (!child.HasExited) child.Kill(); await child.WaitForExitAsync(); }
    }
}
