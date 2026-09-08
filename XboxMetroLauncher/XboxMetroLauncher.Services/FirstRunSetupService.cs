using System.IO;
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
            return File.Exists(Path.Combine(AppPaths.UserDataFolder, FileName));
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
