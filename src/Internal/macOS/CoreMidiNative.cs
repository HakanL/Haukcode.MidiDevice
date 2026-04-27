namespace Haukcode.MidiDevice.Internal.macOS;

/// <summary>
/// P/Invoke declarations for CoreMIDI.framework and CoreFoundation.framework (macOS).
/// All handles are opaque integer references (MIDIObjectRef etc.) represented as nint.
/// </summary>
internal static class CoreMidiNative
{
    private const string CoreMidi        = "/System/Library/Frameworks/CoreMIDI.framework/CoreMIDI";
    private const string CoreFoundation  = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    // -------------------------------------------------------------------------
    // OSStatus / error codes
    // -------------------------------------------------------------------------

    internal const int NoErr = 0;

    // -------------------------------------------------------------------------
    // CoreFoundation — CFString
    // -------------------------------------------------------------------------

    /// <summary>kCFStringEncodingUTF8 = 0x08000100</summary>
    internal const uint kCFStringEncodingUTF8 = 0x08000100;

    [DllImport(CoreFoundation)]
    internal static extern nint CFStringCreateWithCString(nint allocator, string cStr, uint encoding);

    [DllImport(CoreFoundation)]
    internal static extern void CFRelease(nint cf);

    /// <summary>
    /// Returns a C-string pointer from a CFString (may return null for some encodings).
    /// The pointer is valid as long as the CFString is not mutated or released.
    /// </summary>
    [DllImport(CoreFoundation, CharSet = CharSet.Ansi)]
    internal static extern nint CFStringGetCStringPtr(nint theString, uint encoding);

    /// <summary>Fallback: copies the CFString into a caller-supplied buffer.</summary>
    [DllImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool CFStringGetCString(nint theString, byte[] buffer, nint bufferSize, uint encoding);

    // -------------------------------------------------------------------------
    // CoreFoundation — CFRunLoop
    // -------------------------------------------------------------------------

    [DllImport(CoreFoundation)]
    internal static extern nint CFRunLoopGetCurrent();

    [DllImport(CoreFoundation)]
    internal static extern void CFRunLoopRun();

    [DllImport(CoreFoundation)]
    internal static extern void CFRunLoopStop(nint rl);

    // -------------------------------------------------------------------------
    // CoreMIDI — device / endpoint enumeration
    // -------------------------------------------------------------------------

    [DllImport(CoreMidi)]
    internal static extern uint MIDIGetNumberOfSources();

    [DllImport(CoreMidi)]
    internal static extern nint MIDIGetSource(uint sourceIndex0);

    [DllImport(CoreMidi)]
    internal static extern uint MIDIGetNumberOfDestinations();

    [DllImport(CoreMidi)]
    internal static extern nint MIDIGetDestination(uint destIndex0);

    /// <summary>
    /// Retrieve a string property from a MIDIObject.
    /// The returned CFStringRef must be CFRelease'd by the caller.
    /// </summary>
    [DllImport(CoreMidi)]
    internal static extern int MIDIObjectGetStringProperty(nint obj, nint propertyID, out nint str);

    /// <summary>Retrieve an integer property from a MIDIObject.</summary>
    [DllImport(CoreMidi)]
    internal static extern int MIDIObjectGetIntegerProperty(nint obj, nint propertyID, out int outValue);

    // CoreMIDI property name constants (CFStringRef, obtained at runtime via CFStringCreateWithCString)
    internal static readonly nint kMIDIPropertyName         = CreatePropertyKey("name");
    internal static readonly nint kMIDIPropertyManufacturer = CreatePropertyKey("manufacturer");
    internal static readonly nint kMIDIPropertyUniqueID     = CreatePropertyKey("uniqueID");
    internal static readonly nint kMIDIPropertyOffline      = CreatePropertyKey("offline");

    private static nint CreatePropertyKey(string name)
        => CFStringCreateWithCString(nint.Zero, name, kCFStringEncodingUTF8);

    // -------------------------------------------------------------------------
    // CoreMIDI — client / port / connection
    // -------------------------------------------------------------------------

    /// <summary>Callback type for MIDIInputPortCreate.</summary>
    internal delegate void MIDIReadProc(nint pktlist, nint readProcRefCon, nint srcConnRefCon);

    /// <summary>
    /// Notification callback passed to MIDIClientCreate.
    /// <paramref name="message"/> points to a MIDINotification struct:
    ///   SInt32 messageID   (offset 0)
    ///   UInt32 messageSize (offset 4)
    /// messageID == 1 means kMIDIMsgSetupChanged (sources/destinations added or removed).
    /// </summary>
    internal delegate void MIDINotifyProc(nint message);

    /// <summary>Create a MIDIClient with a device-change notification callback.</summary>
    [DllImport(CoreMidi)]
    internal static extern int MIDIClientCreate(nint name, MIDINotifyProc? notifyProc, nint notifyRefCon, out nint outClient);

    [DllImport(CoreMidi)]
    internal static extern int MIDIClientDispose(nint client);

    [DllImport(CoreMidi)]
    internal static extern int MIDIInputPortCreate(nint client, nint portName, MIDIReadProc readProc,
        nint refCon, out nint outPort);

    [DllImport(CoreMidi)]
    internal static extern int MIDIOutputPortCreate(nint client, nint portName, out nint outPort);

    [DllImport(CoreMidi)]
    internal static extern int MIDIPortConnectSource(nint port, nint source, nint connRefCon);

    [DllImport(CoreMidi)]
    internal static extern int MIDIPortDisconnectSource(nint port, nint source);

    [DllImport(CoreMidi)]
    internal static extern int MIDIPortDispose(nint port);

    // -------------------------------------------------------------------------
    // CoreMIDI — sending
    // -------------------------------------------------------------------------

    /// <summary>Send a MIDIPacketList already prepared in unmanaged memory.</summary>
    [DllImport(CoreMidi)]
    internal static extern int MIDISend(nint port, nint dest, nint pktlist);

    // -------------------------------------------------------------------------
    // MIDIPacket / MIDIPacketList layout constants
    // -------------------------------------------------------------------------

    // MIDIPacket layout (packed):
    //   UInt64 timeStamp   (8 bytes)
    //   UInt16 length      (2 bytes)
    //   Byte   data[256]   (up to 256, actual = length)
    //
    // MIDIPacketList layout:
    //   UInt32 numPackets   (4 bytes)
    //   MIDIPacket packet[1] (variable)

    internal const int PacketListHeaderSize = 4;       // numPackets: UInt32
    internal const int PacketHeaderSize     = 10;      // timeStamp(8) + length(2)
    internal const int MaxPacketDataSize    = 65536;   // practical ceiling for SysEx

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>Retrieve an integer property; returns 0 if not found.</summary>
    internal static int GetIntegerProperty(nint midiObject, nint propertyKey)
    {
        MIDIObjectGetIntegerProperty(midiObject, propertyKey, out var value);
        return value;
    }

    /// <summary>Convert a CoreMIDI CFStringRef property to a managed string, then release it.</summary>
    internal static string GetStringProperty(nint midiObject, nint propertyKey)
    {
        if (MIDIObjectGetStringProperty(midiObject, propertyKey, out var cfStr) != NoErr || cfStr == nint.Zero)
            return string.Empty;

        try
        {
            // Fast path — works for pure ASCII names (most MIDI device names)
            var ptr = CFStringGetCStringPtr(cfStr, kCFStringEncodingUTF8);
            if (ptr != nint.Zero)
                return Marshal.PtrToStringAnsi(ptr) ?? string.Empty;

            // Fallback: copy into a managed buffer
            var buf = new byte[512];
            if (CFStringGetCString(cfStr, buf, buf.Length, kCFStringEncodingUTF8))
            {
                var len = Array.IndexOf(buf, (byte)0);
                return System.Text.Encoding.UTF8.GetString(buf, 0, len < 0 ? buf.Length : len);
            }

            return string.Empty;
        }
        finally
        {
            CFRelease(cfStr);
        }
    }
}
