using System;
using System.Linq;
using System.Runtime.InteropServices;

public static class CcdDisplaySwitcher
{
    // QueryDisplayConfig flags
    private const uint QDC_ALL_PATHS = 0x00000001;
    private const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;

    // SetDisplayConfig flags
    private const uint SDC_APPLY = 0x00000080;
    private const uint SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x00000020;
    private const uint SDC_ALLOW_CHANGES = 0x00000400;

    // Path flags
    private const uint DISPLAYCONFIG_PATH_ACTIVE = 0x00000001;
    private const uint DISPLAYCONFIG_PATH_MODE_IDX_INVALID = 0xFFFFFFFF;

    // DisplayConfigGetDeviceInfo packet types
    private const uint DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 0x00000002;

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(
        uint flags,
        out uint numPathArrayElements,
        out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        IntPtr pathArray,
        ref uint numModeInfoArrayElements,
        IntPtr modeInfoArray,
        IntPtr currentTopologyId /* optional */);

    [DllImport("user32.dll")]
    private static extern int SetDisplayConfig(
        uint numPathArrayElements,
        IntPtr pathArray,
        uint numModeInfoArrayElements,
        IntPtr modeInfoArray,
        uint flags);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);

    // ---- CCD structs (enough to marshal correctly for typical scenarios) ----

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;

        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public uint scanLineOrdering;
        [MarshalAs(UnmanagedType.Bool)] public bool targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags; // includes DISPLAYCONFIG_PATH_ACTIVE
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_2DREGION
    {
        public uint cx;
        public uint cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO
    {
        public ulong pixelRate;
        public DISPLAYCONFIG_RATIONAL hSyncFreq;
        public DISPLAYCONFIG_RATIONAL vSyncFreq;
        public DISPLAYCONFIG_2DREGION activeSize;
        public DISPLAYCONFIG_2DREGION totalSize;
        public uint videoStandard;
        public uint scanLineOrdering;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_TARGET_MODE
    {
        public DISPLAYCONFIG_VIDEO_SIGNAL_INFO targetVideoSignalInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_SOURCE_MODE
    {
        public uint width;
        public uint height;
        public uint pixelFormat;
        public POINTL position;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTL
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_MODE_INFO
    {
        public uint infoType;
        public uint id;
        public LUID adapterId;

        // union - allocate largest
        public DISPLAYCONFIG_MODE_INFO_UNION u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct DISPLAYCONFIG_MODE_INFO_UNION
    {
        [FieldOffset(0)] public DISPLAYCONFIG_TARGET_MODE targetMode;
        [FieldOffset(0)] public DISPLAYCONFIG_SOURCE_MODE sourceMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public uint type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    // Target name packet (friendly name + device path). Docs say it’s used to obtain friendly names/device paths. [3](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-displayconfiggetdeviceinfo)
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;

        public uint flags;
        public uint outputTechnology;

        public ushort edidManufactureId;
        public ushort edidProductCodeId;

        public uint connectorInstance;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string monitorFriendlyDeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string monitorDevicePath;
    }

    // ---- Public helpers ----

    public static void ListTargets()
    {
        var (paths, modes) = GetConfig(QDC_ALL_PATHS);

        Console.WriteLine("Targets:");
        for (int i = 0; i < paths.Length; i++)
        {
            if ((paths[i].flags & DISPLAYCONFIG_PATH_ACTIVE) == 0) continue;
            if (!TryGetTargetName(paths[i].targetInfo.adapterId, paths[i].targetInfo.id, out var name)) continue;
            Console.WriteLine($"  Path#{i}: {name.monitorFriendlyDeviceName}  [{name.monitorDevicePath}]");
        }
    }

    /// <summary>
    /// Enable only targets whose friendly name contains <paramref name="friendlyNameSubstring"/> (case-insensitive).
    /// Disables all other active paths.
    /// </summary>
    public static void EnableOnly(string friendlyNameSubstring)
    {
        var (paths, modes) = GetConfig(QDC_ONLY_ACTIVE_PATHS);

        // Decide which paths to keep active based on target friendly name
        bool anyKept = false;
        for (int i = 0; i < paths.Length; i++)
        {
            var t = paths[i].targetInfo;
            if (!TryGetTargetName(t.adapterId, t.id, out var name))
            {
                paths[i].flags &= ~DISPLAYCONFIG_PATH_ACTIVE;
                paths[i].sourceInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
                paths[i].targetInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
                continue;
            }

            bool keep = !string.IsNullOrWhiteSpace(name.monitorFriendlyDeviceName) &&
                        name.monitorFriendlyDeviceName.IndexOf(friendlyNameSubstring, StringComparison.OrdinalIgnoreCase) >= 0;

            if (keep)
            {
                paths[i].flags |= DISPLAYCONFIG_PATH_ACTIVE;
                anyKept = true;
            }
            else
            {
                paths[i].flags &= ~DISPLAYCONFIG_PATH_ACTIVE;
                paths[i].sourceInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
                paths[i].targetInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
            }
        }

        if (!anyKept)
            throw new InvalidOperationException($"No active targets matched '{friendlyNameSubstring}'.");

        ApplySuppliedConfig(paths, modes);
    }

    // ---- Internal plumbing ----

    private static (DISPLAYCONFIG_PATH_INFO[] paths, DISPLAYCONFIG_MODE_INFO[] modes) GetConfig(uint qdcFlags)
    {
        int rc = GetDisplayConfigBufferSizes(qdcFlags, out uint pathCount, out uint modeCount);
        if (rc != 0) throw new System.ComponentModel.Win32Exception(rc);

        var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

        var hPaths = GCHandle.Alloc(paths, GCHandleType.Pinned);
        var hModes = GCHandle.Alloc(modes, GCHandleType.Pinned);
        try
        {
            IntPtr pPaths = hPaths.AddrOfPinnedObject();
            IntPtr pModes = hModes.AddrOfPinnedObject();

            uint pc = pathCount, mc = modeCount;
            rc = QueryDisplayConfig(qdcFlags, ref pc, pPaths, ref mc, pModes, IntPtr.Zero);
            if (rc != 0) throw new System.ComponentModel.Win32Exception(rc);

            // QueryDisplayConfig may update counts; trim arrays if needed
            if (pc != pathCount) paths = paths.Take((int)pc).ToArray();
            if (mc != modeCount) modes = modes.Take((int)mc).ToArray();

            return (paths, modes);
        }
        finally
        {
            hPaths.Free();
            hModes.Free();
        }
    }

    private static bool TryGetTargetName(LUID adapterId, uint targetId, out DISPLAYCONFIG_TARGET_DEVICE_NAME result)
    {
        result = new DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                adapterId = adapterId,
                id = targetId
            }
        };

        int rc = DisplayConfigGetDeviceInfo(ref result);
        // DisplayConfigGetDeviceInfo returns ERROR_INVALID_PARAMETER for inactive/disconnected
        // paths returned by QDC_ALL_PATHS, and ERROR_ACCESS_DENIED outside a console session.
        return rc == 0;
    }

    private static void ApplySuppliedConfig(DISPLAYCONFIG_PATH_INFO[] paths, DISPLAYCONFIG_MODE_INFO[] modes)
    {
        var hPaths = GCHandle.Alloc(paths, GCHandleType.Pinned);
        var hModes = GCHandle.Alloc(modes, GCHandleType.Pinned);
        try
        {
            IntPtr pPaths = hPaths.AddrOfPinnedObject();
            IntPtr pModes = hModes.AddrOfPinnedObject();

            // SetDisplayConfig applies only paths that are marked active when using supplied config. [1](https://learn.microsoft.com/en-us/answers/questions/3983440/disable-one-monitor-on-three-monitor-setups)
            int rc = SetDisplayConfig(
                (uint)paths.Length, pPaths,
                (uint)modes.Length, pModes,
                SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES);

            if (rc != 0) throw new System.ComponentModel.Win32Exception(rc);
        }
        finally
        {
            hPaths.Free();
            hModes.Free();
        }
    }
    public static void EnableOnlyByTargetDevicePathSubstring(string needle)
    {
        var (paths, modes) = GetConfig(QDC_ONLY_ACTIVE_PATHS);

        bool anyKept = false;

        for (int i = 0; i < paths.Length; i++)
        {
            var t = paths[i].targetInfo;
            if (!TryGetTargetName(t.adapterId, t.id, out var name))
            {
                paths[i].flags &= ~DISPLAYCONFIG_PATH_ACTIVE;
                paths[i].sourceInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
                paths[i].targetInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
                continue;
            }

            bool keep = !string.IsNullOrEmpty(name.monitorDevicePath) &&
                        name.monitorDevicePath.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

            if (keep)
            {
                paths[i].flags |= DISPLAYCONFIG_PATH_ACTIVE;
                anyKept = true;
            }
            else
            {
                paths[i].flags &= ~DISPLAYCONFIG_PATH_ACTIVE;
                paths[i].sourceInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
                paths[i].targetInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
            }
        }

        if (!anyKept)
            throw new InvalidOperationException($"No target matched device path containing '{needle}'.");

        ApplySuppliedConfig(paths, modes);
    }
    public static int Main(string[] args)
    {
        CcdDisplaySwitcher.ListTargets();
        EnableOnlyByTargetDevicePathSubstring("UID8517");

        /*
Targets:
  Path#0:   [\\?\DISPLAY#SDC4187#4&1a73ae31&0&UID8388688#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}]
  Path#1: DELL U2717D  [\\?\DISPLAY#DEL40EA#4&1a73ae31&0&UID4165#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}]
  Path#2: DELL U2717D  [\\?\DISPLAY#DEL40EA#4&1a73ae31&0&UID8517#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}]         
         */
        return 0;

    }
}

// Example usage:
// CcdDisplaySwitcher.ListTargets();
// CcdDisplaySwitcher.EnableOnly("DELL"); // or "LG", "U2720Q", etc.