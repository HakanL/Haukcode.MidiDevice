namespace Haukcode.MidiDevice.Internal.Linux;

/// <summary>P/Invoke declarations for libasound.so.2 (ALSA) and libc.</summary>
internal static class AlsaNative
{
    private const string LibAsound = "libasound.so.2";
    private const string LibC = "libc";

    // -------------------------------------------------------------------------
    // Device enumeration
    // -------------------------------------------------------------------------

    /// <summary>Enumerate ALSA devices for a given interface (e.g. "rawmidi").</summary>
    [DllImport(LibAsound, CharSet = CharSet.Ansi)]
    internal static extern int snd_device_name_hint(int card, string iface, out nint hints);

    [DllImport(LibAsound)]
    internal static extern int snd_device_name_free_hint(nint hints);

    /// <summary>Get a named property from a hint entry. Returns a malloc'd string — caller must free().</summary>
    [DllImport(LibAsound, CharSet = CharSet.Ansi)]
    internal static extern nint snd_device_name_get_hint(nint hint, string id);

    // -------------------------------------------------------------------------
    // Raw MIDI device open/close
    // -------------------------------------------------------------------------

    /// <summary>Open for input only (outputp = nint.Zero).</summary>
    [DllImport(LibAsound, EntryPoint = "snd_rawmidi_open", CharSet = CharSet.Ansi)]
    internal static extern int snd_rawmidi_open_input(out nint inputp, nint outputp, string name, int mode);

    /// <summary>Open for output only (inputp = nint.Zero).</summary>
    [DllImport(LibAsound, EntryPoint = "snd_rawmidi_open", CharSet = CharSet.Ansi)]
    internal static extern int snd_rawmidi_open_output(nint inputp, out nint outputp, string name, int mode);

    [DllImport(LibAsound)]
    internal static extern int snd_rawmidi_close(nint rawmidi);

    // -------------------------------------------------------------------------
    // I/O
    // -------------------------------------------------------------------------

    /// <summary>Blocking read. Returns bytes read (> 0) or negative errno on error.</summary>
    [DllImport(LibAsound)]
    internal static extern nint snd_rawmidi_read(nint rawmidi, byte[] buffer, nint size);

    /// <summary>Write bytes. Returns bytes written or negative errno.</summary>
    [DllImport(LibAsound)]
    internal static extern nint snd_rawmidi_write(nint rawmidi, byte[] buffer, nint size);

    /// <summary>Drain pending output before closing.</summary>
    [DllImport(LibAsound)]
    internal static extern int snd_rawmidi_drain(nint rawmidi);

    // -------------------------------------------------------------------------
    // Error strings / memory
    // -------------------------------------------------------------------------

    /// <summary>Returns a static string — do NOT free.</summary>
    [DllImport(LibAsound)]
    internal static extern nint snd_strerror(int errnum);

    /// <summary>Free a string returned by snd_device_name_get_hint.</summary>
    [DllImport(LibC)]
    internal static extern void free(nint ptr);

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    internal static string GetError(int errnum)
    {
        var ptr = snd_strerror(errnum);
        return Marshal.PtrToStringAnsi(ptr) ?? $"ALSA error {errnum}";
    }
}
