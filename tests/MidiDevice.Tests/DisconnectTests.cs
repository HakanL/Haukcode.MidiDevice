using Haukcode.MidiDevice.Internal.Windows;

namespace MidiDevice.Tests;

public class DisconnectTests
{
    private const string LpdPath =
        @"\\?\usb#vid_09e8&pid_004c&mi_00#8&1cbb8ba4&0&0000#{6994ad04-93ef-11d0-a3cc-00a0c9223196}\global";

    // -------------------------------------------------------------------------
    // DisconnectSignal
    // -------------------------------------------------------------------------

    [Fact]
    public void Signal_RaisesOnce()
    {
        var signal = new DisconnectSignal();
        int count = 0;
        signal.Raised += () => count++;

        signal.Raise();
        signal.Raise();

        Assert.Equal(1, count);
        Assert.True(signal.IsRaised);
    }

    [Fact]
    public void Signal_SubscriberExceptionIsContained()
    {
        var signal = new DisconnectSignal();
        signal.Raised += () => throw new InvalidOperationException();

        signal.Raise();

        Assert.True(signal.IsRaised);
    }

    // -------------------------------------------------------------------------
    // Windows interface-path parsing
    // -------------------------------------------------------------------------

    [Fact]
    public void TryParse_SplitsClassAndInstance()
    {
        Assert.True(WinMmRemovalWatcher.TryParse(LpdPath, out var classGuid, out var instanceKey));

        Assert.Equal(new Guid("6994ad04-93ef-11d0-a3cc-00a0c9223196"), classGuid);
        Assert.Equal(@"\\?\usb#vid_09e8&pid_004c&mi_00#8&1cbb8ba4&0&0000", instanceKey);
    }

    [Fact]
    public void TryParse_NotificationLinkMatchesEnumeratedPath()
    {
        // The PnP notification may differ in case and reference string.
        var link = @"\\?\USB#VID_09E8&PID_004C&MI_00#8&1cbb8ba4&0&0000#{6994AD04-93EF-11D0-A3CC-00A0C9223196}\wave";

        Assert.True(WinMmRemovalWatcher.TryParse(LpdPath, out _, out var enumerated));
        Assert.True(WinMmRemovalWatcher.TryParse(link, out _, out var notified));
        Assert.Equal(enumerated, notified, ignoreCase: true);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("")]
    [InlineData(@"\\?\usb#no-class-guid")]
    public void TryParse_RejectsNonPaths(string value)
    {
        Assert.False(WinMmRemovalWatcher.TryParse(value, out _, out _));
    }

    [Fact]
    public void InterfacePathOf_StripsPortOrdinal()
    {
        Assert.Equal(LpdPath, WinMmBackend.InterfacePathOf(LpdPath + "|1"));
    }

    [Fact]
    public void InterfacePathOf_IndexIdHasNoPath()
    {
        Assert.Null(WinMmBackend.InterfacePathOf("3"));
    }
}
