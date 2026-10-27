namespace CodexBar.Windows;

/// <summary>
/// Keeps one tray instance alive. The first process owns a named mutex and listens on a named
/// auto-reset event; later launches signal that event and exit. Names are per user session, so two
/// signed-in users never block each other.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\CodexBar.Windows.SingleInstance";
    private const string ActivationEventName = @"Local\CodexBar.Windows.Activate";

    private readonly Mutex mutex;
    private readonly EventWaitHandle? activationEvent;
    private readonly RegisteredWaitHandle? registration;
    private bool disposed;

    private SingleInstance(Mutex mutex, EventWaitHandle? activationEvent, RegisteredWaitHandle? registration)
    {
        this.mutex = mutex;
        this.activationEvent = activationEvent;
        this.registration = registration;
        this.IsOwner = activationEvent is not null;
    }

    public bool IsOwner { get; }

    /// <summary>
    /// Claims the instance slot. When another instance already owns it, signals that instance and
    /// returns a guard whose <see cref="IsOwner"/> is false.
    /// </summary>
    public static SingleInstance Acquire(Action onActivated)
    {
        ArgumentNullException.ThrowIfNull(onActivated);

        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            SignalExistingInstance();
            return new SingleInstance(mutex, activationEvent: null, registration: null);
        }

        EventWaitHandle? activationEvent = null;
        try
        {
            activationEvent = new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                ActivationEventName);
        }
        catch (UnauthorizedAccessException)
        {
            // A stale handle owned by another integrity level: fall back to a single silent instance.
        }

        var registration = activationEvent is null
            ? null
            : ThreadPool.RegisterWaitForSingleObject(
                activationEvent,
                (_, _) => onActivated(),
                state: null,
                Timeout.Infinite,
                executeOnlyOnce: false);

        return new SingleInstance(mutex, activationEvent, registration);
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.registration?.Unregister(waitObject: null);
        this.activationEvent?.Dispose();
        if (this.IsOwner)
        {
            try
            {
                this.mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The owning thread already exited; closing the handle is enough.
            }
        }

        this.mutex.Dispose();
    }

    private static void SignalExistingInstance()
    {
        // The owner may still be creating the event between mutex creation and event creation.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var handle = EventWaitHandle.OpenExisting(ActivationEventName);
                handle.Set();
                return;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
        }
    }
}
