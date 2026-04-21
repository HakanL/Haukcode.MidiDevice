using Haukcode.MidiDevice.Internal;

namespace MidiDevice.Tests;

public class MidiStreamParserTests
{
    private static List<MidiMessage> Parse(params byte[] bytes)
    {
        var parser = new MidiStreamParser();
        var result = new List<MidiMessage>();
        parser.Process(bytes, result.Add);
        return result;
    }

    // -------------------------------------------------------------------------
    // Basic message types
    // -------------------------------------------------------------------------

    [Fact]
    public void NoteOn_SingleMessage()
    {
        var msgs = Parse(0x90, 0x3C, 0x7F);
        var msg = Assert.Single(msgs);
        var noteOn = Assert.IsType<NoteOnMessage>(msg);
        Assert.Equal(0, noteOn.Channel);
        Assert.Equal(60, noteOn.NoteNumber);
        Assert.Equal(127, noteOn.Velocity);
    }

    [Fact]
    public void NoteOff_ExplicitStatus()
    {
        var msgs = Parse(0x81, 0x3C, 0x40);
        var msg = Assert.Single(msgs);
        var noteOff = Assert.IsType<NoteOffMessage>(msg);
        Assert.Equal(1, noteOff.Channel);
        Assert.Equal(60, noteOff.NoteNumber);
    }

    [Fact]
    public void NoteOn_VelocityZero_ProducesNoteOff()
    {
        var msgs = Parse(0x92, 0x3C, 0x00);
        var msg = Assert.Single(msgs);
        var noteOff = Assert.IsType<NoteOffMessage>(msg);
        Assert.Equal(2, noteOff.Channel);
        Assert.Equal(60, noteOff.NoteNumber);
        Assert.Equal(0, noteOff.Velocity);
    }

    [Fact]
    public void ControlChange()
    {
        var msgs = Parse(0xB0, 0x07, 0x64);
        var msg = Assert.Single(msgs);
        var cc = Assert.IsType<ControlChangeMessage>(msg);
        Assert.Equal(0, cc.Channel);
        Assert.Equal(7, cc.ControlNumber);
        Assert.Equal(100, cc.Value);
    }

    [Fact]
    public void ProgramChange()
    {
        var msgs = Parse(0xC3, 0x0A);
        var msg = Assert.Single(msgs);
        var pc = Assert.IsType<ProgramChangeMessage>(msg);
        Assert.Equal(3, pc.Channel);
        Assert.Equal(10, pc.ProgramNumber);
    }

    [Fact]
    public void ChannelPressure()
    {
        var msgs = Parse(0xD0, 0x40);
        var msg = Assert.Single(msgs);
        var cp = Assert.IsType<ChannelPressureMessage>(msg);
        Assert.Equal(0, cp.Channel);
        Assert.Equal(64, cp.Pressure);
    }

    [Fact]
    public void PolyKeyPressure()
    {
        var msgs = Parse(0xA1, 0x3C, 0x50);
        var msg = Assert.Single(msgs);
        var pkp = Assert.IsType<PolyKeyPressureMessage>(msg);
        Assert.Equal(1, pkp.Channel);
        Assert.Equal(60, pkp.NoteNumber);
        Assert.Equal(80, pkp.Pressure);
    }

    [Fact]
    public void PitchBend_Center()
    {
        // Center position: LSB=0x00 MSB=0x40 → raw=8192 → value=0
        var msgs = Parse(0xE0, 0x00, 0x40);
        var msg = Assert.Single(msgs);
        var pb = Assert.IsType<PitchBendMessage>(msg);
        Assert.Equal(0, pb.Channel);
        Assert.Equal(0, pb.Value);
    }

    [Fact]
    public void PitchBend_FullDown()
    {
        // Full down: LSB=0x00 MSB=0x00 → raw=0 → value=-8192
        var msgs = Parse(0xE0, 0x00, 0x00);
        var msg = Assert.Single(msgs);
        var pb = Assert.IsType<PitchBendMessage>(msg);
        Assert.Equal(-8192, pb.Value);
    }

    // -------------------------------------------------------------------------
    // Running status
    // -------------------------------------------------------------------------

    [Fact]
    public void RunningStatus_MultipleCC()
    {
        // Status once, then 3 pairs of data bytes — all CC ch0
        var msgs = Parse(0xB0, 0x07, 0x64, 0x0A, 0x7F, 0x0B, 0x00);
        Assert.Equal(3, msgs.Count);
        Assert.IsType<ControlChangeMessage>(msgs[0]);
        Assert.IsType<ControlChangeMessage>(msgs[1]);
        Assert.IsType<ControlChangeMessage>(msgs[2]);
        Assert.Equal(7, ((ControlChangeMessage)msgs[0]).ControlNumber);
        Assert.Equal(10, ((ControlChangeMessage)msgs[1]).ControlNumber);
        Assert.Equal(11, ((ControlChangeMessage)msgs[2]).ControlNumber);
    }

    [Fact]
    public void RunningStatus_ProgramChange()
    {
        // PC is a 2-byte message (1 data byte) — running status uses only 1 data byte
        var msgs = Parse(0xC0, 0x05, 0x0A, 0x0F);
        Assert.Equal(3, msgs.Count);
        Assert.All(msgs, m => Assert.IsType<ProgramChangeMessage>(m));
        Assert.Equal(5, ((ProgramChangeMessage)msgs[0]).ProgramNumber);
        Assert.Equal(10, ((ProgramChangeMessage)msgs[1]).ProgramNumber);
        Assert.Equal(15, ((ProgramChangeMessage)msgs[2]).ProgramNumber);
    }

    [Fact]
    public void NewStatusByte_ClearsRunningStatus()
    {
        var msgs = Parse(
            0xB0, 0x07, 0x64,   // CC ch0 #7 = 100
            0x90, 0x3C, 0x7F);  // Note On ch0 (new status, clears CC running status)

        Assert.Equal(2, msgs.Count);
        Assert.IsType<ControlChangeMessage>(msgs[0]);
        Assert.IsType<NoteOnMessage>(msgs[1]);
    }

    // -------------------------------------------------------------------------
    // Split reads
    // -------------------------------------------------------------------------

    [Fact]
    public void SplitRead_MessageSpansTwoCalls()
    {
        var parser = new MidiStreamParser();
        var result = new List<MidiMessage>();

        // First chunk: status + first data byte
        parser.Process([0x90, 0x3C], result.Add);
        Assert.Empty(result); // incomplete

        // Second chunk: last data byte
        parser.Process([0x7F], result.Add);
        Assert.Single(result);
        Assert.IsType<NoteOnMessage>(result[0]);
    }

    [Fact]
    public void SplitRead_RunningStatus_AcrossCalls()
    {
        var parser = new MidiStreamParser();
        var result = new List<MidiMessage>();

        parser.Process([0xB0, 0x07, 0x64], result.Add); // first CC complete
        parser.Process([0x0A], result.Add);              // first byte of second (running status)
        Assert.Single(result);                           // second not yet complete

        parser.Process([0x7F], result.Add);              // second byte
        Assert.Equal(2, result.Count);
    }

    // -------------------------------------------------------------------------
    // SysEx
    // -------------------------------------------------------------------------

    [Fact]
    public void SysEx_BasicRoundtrip()
    {
        var msgs = Parse(0xF0, 0x47, 0x7F, 0x30, 0xF7);
        var msg = Assert.Single(msgs);
        var sysEx = Assert.IsType<SysExMessage>(msg);
        Assert.Equal(new byte[] { 0x47, 0x7F, 0x30 }, sysEx.Data);
    }

    [Fact]
    public void SysEx_EmptyBody()
    {
        var msgs = Parse(0xF0, 0xF7);
        var msg = Assert.Single(msgs);
        var sysEx = Assert.IsType<SysExMessage>(msg);
        Assert.Empty(sysEx.Data);
    }

    [Fact]
    public void SysEx_SplitAcrossCalls()
    {
        var parser = new MidiStreamParser();
        var result = new List<MidiMessage>();

        parser.Process([0xF0, 0x47, 0x7F], result.Add);
        Assert.Empty(result);

        parser.Process([0x30, 0xF7], result.Add);
        Assert.Single(result);
        Assert.IsType<SysExMessage>(result[0]);
    }

    [Fact]
    public void SysEx_RealtimeInterleavedIgnored()
    {
        // F8 (clock) is a real-time message — legal mid-SysEx, must be ignored
        // and the SysEx must still complete correctly.
        var msgs = Parse(0xF0, 0x47, 0xF8, 0x7F, 0xF7);
        var msg = Assert.Single(msgs);
        var sysEx = Assert.IsType<SysExMessage>(msg);
        Assert.Equal(new byte[] { 0x47, 0x7F }, sysEx.Data);
    }

    [Fact]
    public void SysEx_FollowedByNormalMessage()
    {
        var msgs = Parse(0xF0, 0x01, 0xF7, 0xB0, 0x07, 0x64);
        Assert.Equal(2, msgs.Count);
        Assert.IsType<SysExMessage>(msgs[0]);
        Assert.IsType<ControlChangeMessage>(msgs[1]);
    }

    [Fact]
    public void SysEx_ClearsRunningStatus()
    {
        // CC running status should be cleared after SysEx
        var msgs = Parse(
            0xB0, 0x07, 0x64,       // CC establishes running status
            0xF0, 0x01, 0xF7,       // SysEx clears it
            0x0A, 0x7F);            // orphaned data bytes — should be discarded

        Assert.Equal(2, msgs.Count);
        Assert.IsType<ControlChangeMessage>(msgs[0]);
        Assert.IsType<SysExMessage>(msgs[1]);
    }

    // -------------------------------------------------------------------------
    // Real-time messages
    // -------------------------------------------------------------------------

    [Fact]
    public void Realtime_MidMessage_Ignored()
    {
        // F8 inserted between status and data bytes — must not corrupt the message
        var msgs = Parse(0x90, 0xF8, 0x3C, 0x7F);
        var msg = Assert.Single(msgs);
        Assert.IsType<NoteOnMessage>(msg);
    }

    // -------------------------------------------------------------------------
    // Reset
    // -------------------------------------------------------------------------

    [Fact]
    public void Reset_ClearsState()
    {
        var parser = new MidiStreamParser();
        var result = new List<MidiMessage>();

        parser.Process([0x90, 0x3C], result.Add); // partial message
        parser.Reset();
        parser.Process([0x7F], result.Add);        // orphaned data byte after reset
        Assert.Empty(result);
    }
}
