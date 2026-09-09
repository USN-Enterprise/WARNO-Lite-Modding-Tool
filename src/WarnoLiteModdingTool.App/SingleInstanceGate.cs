namespace WarnoLiteModdingTool.App;

public sealed class SingleInstanceGate : IDisposable
{
    // Stable across versions, executable names and portable installation folders.
    public const string Name = @"Local\WarnoLiteModdingTool.SingleInstance";
    private readonly Mutex _mutex;
    private bool _disposed;

    private SingleInstanceGate(Mutex mutex) => _mutex = mutex;

    public static SingleInstanceGate? TryAcquire(string name = Name)
    {
        var mutex = new Mutex(false, name);
        try
        {
            bool acquired;
            try { acquired = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (acquired) return new SingleInstanceGate(mutex);
            mutex.Dispose();
            return null;
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    // Acquired and released on the application dispatcher thread.
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
