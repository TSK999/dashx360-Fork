using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XboxMetroLauncher.Models;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher.Services;

public sealed class RunningGameService : IRunningGameService, IDisposable
{
    private readonly object _syncRoot = new();
    private readonly SemaphoreSlim _closeLock = new(1, 1);
    private Process? _process;
    private GameMetadata? _game;
    private DateTimeOffset _launchedAt;
    private DateTimeOffset? _playtimeStarted;
    private bool _hasPlaytimeUpdate;
    private RunningGameState _state;
    private long _generation;
    private HashSet<int> _knownProcessIds = new();
    private ProcessIdentity? _identity;
    private ProcessIdentity? _forceIdentity;
    private DateTimeOffset _forceExpires;
    private sealed record ProcessIdentity(int Id, long StartTicks);

    public bool HasRunningGame { get { lock (_syncRoot) return _game != null; } }
    public bool HasTrackedProcess { get { lock (_syncRoot) return IsAlive(_process); } }
    public string RunningGameTitle { get { lock (_syncRoot) return _game?.Title ?? string.Empty; } }
    public RunningGameState State { get { lock (_syncRoot) return _state == RunningGameState.Tracked && !IsAlive(_process) ? RunningGameState.ProcessNotDetected : _state; } }
    public GameMetadata? CurrentGame { get { lock (_syncRoot) return _game; } }
    public event EventHandler? StateChanged;
    public bool ConsumePlaytimeUpdate() { lock (_syncRoot) { var result = _hasPlaytimeUpdate; _hasPlaytimeUpdate = false; return result; } }

    public void BeginLaunch(GameMetadata game, DateTimeOffset launchedAt)
    {
        lock (_syncRoot) { ClearLocked(); _knownProcessIds = CaptureProcessIds(); _game = game; _launchedAt = launchedAt; _state = RunningGameState.Launching; }
        Changed();
    }

    public void Track(GameMetadata game, Process? process)
    {
        lock (_syncRoot)
        {
            // A late launch result must not attach to a newer launch.
            if (!ReferenceEquals(_game, game)) { process?.Dispose(); return; }
            if (process != null && IsVerified(game, process, _launchedAt)) AttachLocked(process);
            else { process?.Dispose(); _state = RunningGameState.ProcessNotDetected; }
        }
        Changed();
    }

    public void Clear() { lock (_syncRoot) ClearLocked(); Changed(); }
    public void Dispose() => Clear();

    public async Task<RunningGameCloseResult> CloseAsync(bool forceKill, CancellationToken cancellationToken = default)
    {
        await _closeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await CloseCoreAsync(forceKill, cancellationToken).ConfigureAwait(false); }
        finally { _closeLock.Release(); }
    }

    private async Task<RunningGameCloseResult> CloseCoreAsync(bool forceKill, CancellationToken token)
    {
        GameMetadata? game;
        long generation;
        DateTimeOffset launchedAt;
        lock (_syncRoot) { game = _game; generation = _generation; launchedAt = _launchedAt; }
        if (game == null) return Result(false, "No game running.");
        if (string.Equals(game.LaunchType, "Url", StringComparison.OrdinalIgnoreCase)) return Result(false, "Close this website in your browser.");

        for (var round = 0; round < 8; round++)
        {
            token.ThrowIfCancellationRequested();
            lock (_syncRoot)
            {
                if (generation != _generation) return Result(false, "The running game changed. Try again.");
                if (IsAlive(_process)) break;
                foreach (var candidate in Process.GetProcesses())
                {
                    if (_process == null && IsVerified(game, candidate, launchedAt)) AttachLocked(candidate);
                    else candidate.Dispose();
                }
                if (IsAlive(_process)) break;
            }
            if (DateTimeOffset.UtcNow - launchedAt >= TimeSpan.FromSeconds(12)) break;
            await Task.Delay(500, token).ConfigureAwait(false);
        }
        ProcessIdentity? identity;
        lock (_syncRoot) identity = _identity;
        if (identity == null) return Result(false, "Game running, but its process could not be verified.");
        try
        {
            // A dedicated handle stays valid even when the tracked process exits during this await.
            using var process = Process.GetProcessById(identity.Id);
            _ = process.SafeHandle;
            if (ReadIdentity(process) != identity || !IsVerified(game, process, launchedAt)) return Result(false, "The game process changed. Try again.");
            bool exited;
            lock (_syncRoot)
            {
                if (generation != _generation || identity != _identity) return Result(false, "The running game changed. Try again.");
                exited = process.CloseMainWindow();
            }
            if (exited) exited = await WaitForExitAsync(process, token).ConfigureAwait(false);
            if (!exited)
            {
                lock (_syncRoot)
                {
                    if (generation != _generation || identity != _identity) return Result(false, "The running game changed. Try again.");
                    if (!forceKill || _forceIdentity != identity || DateTimeOffset.UtcNow > _forceExpires)
                    {
                        _forceIdentity = identity;
                        _forceExpires = DateTimeOffset.UtcNow.AddSeconds(8);
                        return new RunningGameCloseResult { Success = false, RequiresForceConfirmation = true, Message = game.Title + " did not close. Press X again to force close." };
                    }
                    token.ThrowIfCancellationRequested();
                    if (ReadIdentity(process) != identity || !IsVerified(game, process, launchedAt)) return Result(false, "The game process could no longer be verified.");
                    _forceIdentity = null;
                    // Descendant processes have not been independently verified.
                    process.Kill(entireProcessTree: false);
                }
                exited = await WaitForExitAsync(process, token).ConfigureAwait(false);
            }
            if (!exited) return Result(false, game.Title + " has not exited yet.");
            lock (_syncRoot) { if (generation == _generation) ClearLocked(); }
            Changed();
            return Result(true, game.Title + " closed.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            App.LogException(ex, "RunningGameService.CloseAsync");
            lock (_syncRoot)
            {
                if (generation == _generation && !IsAlive(_process)) { ClearLocked(); return Result(true, game.Title + " closed."); }
            }
            return Result(false, "Unable to close " + game.Title + ": " + ex.Message);
        }
    }

    private bool IsVerified(GameMetadata game, Process process, DateTimeOffset launchedAt)
    {
        try
        {
            if (process.Id == Environment.ProcessId || process.HasExited || string.Equals(game.LaunchType, "Url", StringComparison.OrdinalIgnoreCase)) return false;
            if (_knownProcessIds.Contains(process.Id) || process.StartTime.ToUniversalTime() < launchedAt.UtcDateTime.AddSeconds(-1)) return false;
            var name = process.ProcessName;
            if (new[] { "steam", "steamwebhelper", "explorer", "dashx360", "xboxmetrolauncher", "cmd", "rundll32", "conhost", "gameoverlayui", "steamservice" }.Contains(name, StringComparer.OrdinalIgnoreCase)) return false;
            return SafePaths.MatchesExecutable(ProcessPaths.TryRead(process), game.ExecutablePath,
                string.Equals(game.LaunchType, "Steam", StringComparison.OrdinalIgnoreCase) ? game.InstallPath : null);
        }
        catch { return false; }
    }
    private static HashSet<int> CaptureProcessIds()
    {
        var ids = new HashSet<int>();
        foreach (var process in Process.GetProcesses())
        {
            using (process) { try { ids.Add(process.Id); } catch (InvalidOperationException) { } }
        }
        return ids;
    }
    private static ProcessIdentity ReadIdentity(Process process) => new(process.Id, process.StartTime.ToUniversalTime().Ticks);
    private static bool IsAlive(Process? process) { try { return process != null && !process.HasExited; } catch { return false; } }
    private static RunningGameCloseResult Result(bool success, string message) => new() { Success = success, RequiresForceConfirmation = false, Message = message };
    private static async Task<bool> WaitForExitAsync(Process process, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); return true; }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return process.HasExited; }
    }
    private void AttachLocked(Process process)
    {
        DetachLocked();
        _process = process;
        _identity = ReadIdentity(process);
        _state = RunningGameState.Tracked;
        _playtimeStarted = string.Equals(_game?.LaunchType, "Exe", StringComparison.OrdinalIgnoreCase) ? _launchedAt : null;
        process.Exited += ProcessExited;
        process.EnableRaisingEvents = true;
    }
    private void ProcessExited(object? sender, EventArgs args)
    {
        lock (_syncRoot) { if (!ReferenceEquals(sender, _process)) return; ClearLocked(); }
        Changed();
    }
    private void ClearLocked()
    {
        if (_game != null && _playtimeStarted.HasValue)
        {
            var elapsed = DateTimeOffset.UtcNow - _playtimeStarted.Value;
            if (elapsed > TimeSpan.Zero) { _game.Playtime += elapsed; _hasPlaytimeUpdate = true; }
        }
        _playtimeStarted = null;
        DetachLocked();
        _game = null;
        _state = RunningGameState.None;
        _generation++;
    }
    private void DetachLocked()
    {
        if (_process != null) { _process.Exited -= ProcessExited; _process.Dispose(); }
        _process = null;
        _identity = null;
        _forceIdentity = null;
    }
    private void Changed() => StateChanged?.Invoke(this, EventArgs.Empty);
}
