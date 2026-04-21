namespace Haukcode.MidiDevice.Internal.Windows;

/// <summary>P/Invoke declarations for winmm.dll (Windows Multimedia MIDI API).</summary>
internal static class WinMmNative
{
    private const string WinMm = "winmm.dll";

    // -------------------------------------------------------------------------
    // Constants
    // -------------------------------------------------------------------------

    internal const uint MMSYSERR_NOERROR    = 0;
    internal const uint MIDIERR_STILLPLAYING = 65;

    internal const uint CALLBACK_NULL     = 0x00000000;
    internal const uint CALLBACK_FUNCTION = 0x00030000;

    internal const uint MIM_OPEN    = 0x3C1;
    internal const uint MIM_CLOSE   = 0x3C2;
    internal const uint MIM_DATA    = 0x3C3; // regular MIDI data (packed into dwParam1)
    internal const uint MIM_LONGDATA = 0x3C4; // SysEx data (dwParam1 = MIDIHDR*)
    internal const uint MIM_ERROR   = 0x3C5;

    internal const uint MHDR_DONE     = 0x00000001;
    internal const uint MHDR_PREPARED = 0x00000002;

    // -------------------------------------------------------------------------
    // MIDIINCAPS / MIDIOUTCAPS
    // -------------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct MIDIINCAPS
    {
        public ushort wMid;
        public ushort wPid;
        public uint   vDriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szPname;
        public uint   dwSupport;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct MIDIOUTCAPS
    {
        public ushort wMid;
        public ushort wPid;
        public uint   vDriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szPname;
        public ushort wTechnology;
        public ushort wVoices;
        public ushort wNotes;
        public ushort wChannelMask;
        public uint   dwSupport;
    }

    // -------------------------------------------------------------------------
    // MIDIHDR
    // -------------------------------------------------------------------------

    /// <summary>
    /// MIDI header for long (SysEx) messages.
    /// Sequential layout matches the Windows C struct on both x86 and x64.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MIDIHDR
    {
        public nint lpData;           // pointer to MIDI data buffer
        public uint  dwBufferLength;   // size of the buffer
        public uint  dwBytesRecorded;  // bytes recorded (input only)
        public nint dwUser;           // client data
        public uint  dwFlags;          // MHDR_DONE etc.
        public nint lpNext;           // reserved
        public nint reserved;         // reserved
        public uint  dwOffset;         // callback offset
        // 8 × pointer-sized reserved words
        private nint _r0, _r1, _r2, _r3, _r4, _r5, _r6, _r7;
    }

    // -------------------------------------------------------------------------
    // Input callback delegate
    // -------------------------------------------------------------------------

    /// <summary>Delegate for midiInOpen CALLBACK_FUNCTION. Store as a field to prevent GC.</summary>
    internal delegate void MidiInProc(
        nint hMidiIn, uint wMsg, nint dwInstance, nint dwParam1, nint dwParam2);

    // -------------------------------------------------------------------------
    // Input P/Invoke
    // -------------------------------------------------------------------------

    [DllImport(WinMm)]
    internal static extern uint midiInGetNumDevs();

    [DllImport(WinMm, CharSet = CharSet.Ansi)]
    internal static extern uint midiInGetDevCaps(nint uDeviceID, ref MIDIINCAPS pmic, uint cbmic);

    [DllImport(WinMm)]
    internal static extern uint midiInOpen(
        out nint lphMidiIn, uint uDeviceID,
        MidiInProc? dwCallback, nint dwCallbackInstance, uint fdwOpen);

    [DllImport(WinMm)]
    internal static extern uint midiInClose(nint hMidiIn);

    [DllImport(WinMm)]
    internal static extern uint midiInStart(nint hMidiIn);

    [DllImport(WinMm)]
    internal static extern uint midiInStop(nint hMidiIn);

    [DllImport(WinMm)]
    internal static extern uint midiInReset(nint hMidiIn);

    [DllImport(WinMm)]
    internal static extern uint midiInPrepareHeader(nint hMidiIn, nint lpMidiInHdr, uint cbMidiInHdr);

    [DllImport(WinMm)]
    internal static extern uint midiInUnprepareHeader(nint hMidiIn, nint lpMidiInHdr, uint cbMidiInHdr);

    [DllImport(WinMm)]
    internal static extern uint midiInAddBuffer(nint hMidiIn, nint lpMidiInHdr, uint cbMidiInHdr);

    // -------------------------------------------------------------------------
    // Output P/Invoke
    // -------------------------------------------------------------------------

    [DllImport(WinMm)]
    internal static extern uint midiOutGetNumDevs();

    [DllImport(WinMm, CharSet = CharSet.Ansi)]
    internal static extern uint midiOutGetDevCaps(nint uDeviceID, ref MIDIOUTCAPS pmoc, uint cbmoc);

    [DllImport(WinMm)]
    internal static extern uint midiOutOpen(
        out nint lphMidiOut, uint uDeviceID,
        nint dwCallback, nint dwCallbackInstance, uint fdwOpen);

    [DllImport(WinMm)]
    internal static extern uint midiOutClose(nint hMidiOut);

    [DllImport(WinMm)]
    internal static extern uint midiOutShortMsg(nint hMidiOut, uint dwMsg);

    [DllImport(WinMm)]
    internal static extern uint midiOutPrepareHeader(nint hMidiOut, nint lpMidiOutHdr, uint cbMidiOutHdr);

    [DllImport(WinMm)]
    internal static extern uint midiOutUnprepareHeader(nint hMidiOut, nint lpMidiOutHdr, uint cbMidiOutHdr);

    [DllImport(WinMm)]
    internal static extern uint midiOutLongMsg(nint hMidiOut, nint lpMidiOutHdr, uint cbMidiOutHdr);

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the number of data bytes expected for a status byte.
    /// Used to trim the zero-padded 3-byte WinMM data word to the correct length.
    /// </summary>
    internal static int DataLength(byte status) => (status & 0xF0) switch
    {
        0x80 or 0x90 or 0xA0 or 0xB0 or 0xE0 => 2, // Note Off/On, PolyAT, CC, PitchBend
        0xC0 or 0xD0 => 1,                           // Program Change, Channel Pressure
        _ => status switch
        {
            0xF1 or 0xF3 => 1, // MTC, Song Select
            0xF2 => 2,         // Song Position Pointer
            _ => 0,
        },
    };
}
