using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using XboxMetroLauncher.Models;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher.Services;

public sealed class FirstRunSetupService
{
    private const string FileName = "first-run.json";
    private readonly IJsonStore _store;

    public FirstRunSetupService(IJsonStore store)
    {
        _store = store;
    }

    public bool IsCompleted()
    {
        try
        {
            string path = Path.Combine(AppPaths.UserDataFolder, FileName);
            if (!File.Exists(path))
            {
                return false;
            }

            FirstRunSetupState? state = JsonSerializer.Deserialize<FirstRunSetupState>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return state?.Completed == true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<FirstRunSetupState> LoadAsync(CancellationToken cancellationToken = default)
    {
        return await _store.ReadAsync<FirstRunSetupState>(FileName, cancellationToken).ConfigureAwait(false)
            ?? new FirstRunSetupState();
    }

    public Task SaveAsync(FirstRunSetupState state, CancellationToken cancellationToken = default)
    {
        return _store.WriteAsync(FileName, state, cancellationToken);
    }
}
