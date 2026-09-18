namespace Haukcode.MidiDevice.Internal;

/// <summary>
/// One-shot "the device went away" flag shared by a backend and the public
/// device wrapper. <see cref="Raise"/> may be called from any thread, any
/// number of times; subscribers hear it once.
/// </summary>
internal sealed class DisconnectSignal
{
    private int _raised;

    /// <summary>Raised once, on the thread that detected the removal.</summary>
    public event Action? Raised;

    public bool IsRaised => Volatile.Read(ref _raised) != 0;

    public void Raise()
    {
        if (Interlocked.Exchange(ref _raised, 1) != 0)
            return;

        try
        {
            Raised?.Invoke();
        }
        catch
        {
            // A subscriber's exception must not propagate into an OS callback thread.
        }
    }
}
