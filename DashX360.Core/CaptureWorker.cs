namespace XboxMetroLauncher.Utilities;

/// <summary>Serializes capture generations without blocking the UI during native cleanup.</summary>
public sealed class CaptureWorker : IDisposable
{
    private readonly object sync = new();
    private readonly Action<CancellationToken> capture;
    private readonly Action<Exception> onError;
    private CancellationTokenSource? cancellation;
    private bool wanted;
    private bool disposed;
    public CaptureWorker(Action<CancellationToken> capture, Action<Exception> onError) { this.capture = capture; this.onError = onError; }
    public bool IsRunning { get { lock (sync) return cancellation != null; } }
    public void Start() { lock (sync) { if (disposed) return; wanted = true; if (cancellation == null) StartLocked(); } }
    private void StartLocked()
    {
        var owner = new CancellationTokenSource();
        cancellation = owner;
        var thread = new Thread(() =>
        {
            try { capture(owner.Token); }
            catch (OperationCanceledException) when (owner.IsCancellationRequested) { }
            catch (Exception ex) { onError(ex); }
            finally
            {
                lock (sync)
                {
                    var restart = wanted && owner.IsCancellationRequested && !disposed;
                    cancellation = null;
                    owner.Dispose();
                    if (restart) StartLocked();
                }
            }
        }) { IsBackground = true, Name = "Metro audio analyzer" };
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }
    public void Stop() { lock (sync) { wanted = false; cancellation?.Cancel(); } }
    public void Dispose() { lock (sync) { disposed = true; wanted = false; cancellation?.Cancel(); } }
}
