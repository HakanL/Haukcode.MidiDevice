namespace Haukcode.MidiDevice.Internal.Windows;

/// <summary>
/// Event-driven "this device went away" for WinMM ports, keyed by the PnP
/// interface path the port was enumerated with.
///
/// WinMM itself says nothing when a USB MIDI device is unplugged: the input
/// callback gets no MIM_CLOSE / MIM_ERROR, and after a replug the old handle
/// accepts every call yet never delivers data again. Polling the device list
/// can't catch a replug faster than the poll interval either (a hand replug
/// measured 0.8 s). So we ask the PnP manager: one CM_Register_Notification
/// per interface class, and every interface removal is matched against the
/// watched paths by device instance.
/// </summary>
internal static class WinMmRemovalWatcher
{
    private static readonly object LockObject = new();
    private static readonly Dictionary<Guid, nint> Registrations = [];
    private static readonly Dictionary<object, (string InstanceKey, Action OnRemoved)> Watchers = [];

    // Static so the delegate outlives every registration (which are never
    // unregistered: CM_Unregister_Notification can't be called from the
    // callback, and one registration per interface class is cheap).
    private static readonly CfgMgrNative.CM_NOTIFY_CALLBACK Callback = OnNotify;

    /// <summary>
    /// Call <paramref name="onRemoved"/> (on a PnP thread) when the device
    /// behind <paramref name="interfacePath"/> is removed. Returns null when
    /// the path can't be parsed or the notification can't be registered —
    /// the caller then relies on its other signals.
    /// </summary>
    public static IDisposable? Watch(string interfacePath, Action onRemoved)
    {
        if (!TryParse(interfacePath, out var classGuid, out var instanceKey))
            return null;

        lock (LockObject)
        {
            if (!Registrations.ContainsKey(classGuid))
            {
                if (!Register(classGuid, out var handle))
                    return null;

                Registrations[classGuid] = handle;
            }

            var token = new object();
            Watchers[token] = (instanceKey, onRemoved);

            return new WatchHandle(token);
        }
    }

    private static bool Register(Guid classGuid, out nint handle)
    {
        handle = nint.Zero;
        try
        {
            var filter = new CfgMgrNative.CM_NOTIFY_FILTER
            {
                cbSize     = (uint)Marshal.SizeOf<CfgMgrNative.CM_NOTIFY_FILTER>(),
                FilterType = CfgMgrNative.CM_NOTIFY_FILTER_TYPE_DEVICEINTERFACE,
                ClassGuid  = classGuid,
            };

            return CfgMgrNative.CM_Register_Notification(ref filter, nint.Zero, Callback, out handle)
                == CfgMgrNative.CR_SUCCESS;
        }
        catch
        {
            // cfgmgr32 without CM_Register_Notification (pre-Windows 8).
            return false;
        }
    }

    private static uint OnNotify(nint hNotify, nint context, int action, nint eventData, uint eventDataSize)
    {
        if (action != CfgMgrNative.CM_NOTIFY_ACTION_DEVICEINTERFACEREMOVAL || eventData == nint.Zero)
            return CfgMgrNative.ERROR_SUCCESS;

        try
        {
            var link = Marshal.PtrToStringUni(eventData + CfgMgrNative.EventDataSymbolicLinkOffset);
            if (link == null || !TryParse(link, out _, out var instanceKey))
                return CfgMgrNative.ERROR_SUCCESS;

            List<Action> removed;
            lock (LockObject)
            {
                removed = Watchers.Values
                    .Where(w => string.Equals(w.InstanceKey, instanceKey, StringComparison.OrdinalIgnoreCase))
                    .Select(w => w.OnRemoved)
                    .ToList();
            }

            foreach (var onRemoved in removed)
            {
                try { onRemoved(); } catch { }
            }
        }
        catch
        {
            // Never let an exception escape into the PnP callback thread.
        }

        return CfgMgrNative.ERROR_SUCCESS;
    }

    /// <summary>
    /// "\\?\usb#vid_..&amp;mi_00#8&amp;1cbb8ba4&amp;0&amp;0000#{6994ad04-...}\global" →
    /// class {6994ad04-...} and instance "\\?\usb#vid_..&amp;mi_00#8&amp;1cbb8ba4&amp;0&amp;0000".
    /// Matching on the instance rather than the whole path makes the
    /// reference-string suffix and letter case irrelevant.
    /// </summary>
    internal static bool TryParse(string interfacePath, out Guid classGuid, out string instanceKey)
    {
        classGuid   = Guid.Empty;
        instanceKey = string.Empty;

        var hash = interfacePath.LastIndexOf("#{", StringComparison.Ordinal);
        if (hash <= 0) return false;

        var close = interfacePath.IndexOf('}', hash);
        if (close < 0) return false;

        if (!Guid.TryParse(interfacePath.AsSpan(hash + 1, close - hash), out classGuid))
            return false;

        instanceKey = interfacePath[..hash];

        return true;
    }

    private sealed class WatchHandle(object token) : IDisposable
    {
        public void Dispose()
        {
            lock (LockObject)
                Watchers.Remove(token);
        }
    }
}

/// <summary>P/Invoke for the cfgmgr32.dll PnP notification API (Windows 8+).</summary>
internal static class CfgMgrNative
{
    internal const uint CR_SUCCESS    = 0;
    internal const uint ERROR_SUCCESS = 0;

    internal const int CM_NOTIFY_FILTER_TYPE_DEVICEINTERFACE  = 0;
    internal const int CM_NOTIFY_ACTION_DEVICEINTERFACEREMOVAL = 1;

    // CM_NOTIFY_EVENT_DATA: FilterType(4) + Reserved(4) + ClassGuid(16), then SymbolicLink.
    internal const int EventDataSymbolicLinkOffset = 24;

    /// <summary>
    /// CM_NOTIFY_FILTER with the DeviceInterface arm of its union. The union's
    /// largest arm is WCHAR InstanceId[200], so the struct is 16 + 400 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 416)]
    internal struct CM_NOTIFY_FILTER
    {
        [FieldOffset(0)]  public uint cbSize;
        [FieldOffset(4)]  public uint Flags;
        [FieldOffset(8)]  public int  FilterType;
        [FieldOffset(12)] public uint Reserved;
        [FieldOffset(16)] public Guid ClassGuid;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate uint CM_NOTIFY_CALLBACK(nint hNotify, nint context, int action, nint eventData, uint eventDataSize);

    [DllImport("cfgmgr32.dll")]
    internal static extern uint CM_Register_Notification(
        ref CM_NOTIFY_FILTER pFilter, nint pContext, CM_NOTIFY_CALLBACK pCallback, out nint pNotifyContext);
}
