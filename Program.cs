// ===================================================================================
// MyDisplaySwitch – Windows CCD display configuration utility
//
// To add a "Toggle Displays" shortcut to the Windows Start Menu:
//
//   1. Build/publish the project:
//        dotnet publish -c Release -r win-x64 --self-contained false
//
//   2. Open the Start Menu Programs folder:
//        - Press Win+R, type:  shell:programs   and press Enter
//        - (This opens %APPDATA%\Microsoft\Windows\Start Menu\Programs)
//
//   3. Right-click in the folder → New → Shortcut
//        - Target:   "C:\repos\MyDisplaySwitch\bin\Release\net10.0\win-x64\publish\MyDisplaySwitch.exe" toggle
//        - Name:     Toggle Displays
//
//   4. (Optional) Set a keyboard shortcut:
//        - Right-click the shortcut → Properties → Shortcut tab
//        - Click the "Shortcut key" field and press your desired combo (e.g. Ctrl+Alt+D)
//        - Set "Run" to "Minimized" to avoid a console flash
//
//   5. The shortcut now appears in Start Menu search and can be pinned to Start or Taskbar.
//
// ===================================================================================

using System;
using System.Diagnostics;
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
    private const uint SDC_TOPOLOGY_EXTEND = 0x00000004;
    private const uint SDC_TOPOLOGY_SUPPLIED = 0x00000010;

    // Mode info types
    private const uint DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE = 1;
    private const uint DISPLAYCONFIG_MODE_INFO_TYPE_TARGET = 2;

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
    /// Toggle between single-monitor and all-monitors modes.
    /// If 3 active monitors: switch to only the display matching "UID4" (DELL UID4165).
    /// If 1 active monitor: enable all extended and make "UID83" (laptop UID8388688) the primary.
    /// Ideal for a Start Menu shortcut or hotkey — see top-of-file comments for setup instructions.
    /// </summary>
    public static void Toggle()
    {
        var (activePaths, _) = GetConfig(QDC_ONLY_ACTIVE_PATHS);
        int activeCount = activePaths.Length;
        Log($"Toggle: {activeCount} active monitor(s)");

        if (activeCount >= 3)
        {
            EnableOnlyByTargetDevicePathSubstring("UID4");
            Console.WriteLine("Toggled to single display (UID4165).");
        }
        else
        {
            EnableAllExtended();
            SetPrimary("UID83");
            Console.WriteLine("Toggled to all displays with UID8388688 as primary.");
        }
    }

    /// <summary>
    /// Enable only targets whose friendly name contains <paramref name="friendlyNameSubstring"/> (case-insensitive).
    /// Disables all other active paths.
    /// </summary>
    public static void EnableOnly(string friendlyNameSubstring)
    {
        // Ensure all connected displays are active so we can find them all
        EnableAllExtended();

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

        NormalizeSourcePositions(paths, modes);
        ApplySuppliedConfig(paths, modes);
    }

    /// <summary>
    /// Enable all connected displays in extended mode.
    /// Step 1: enter extend mode via the topology database (may not include all monitors).
    /// Step 2: detect any missing connected targets and add them with cloned mode data.
    /// </summary>
    public static void EnableAllExtended()
    {
        // Step 1 – enter extend mode from the database (works even if not all monitors are included)
        int rc = SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero, SDC_TOPOLOGY_EXTEND | SDC_APPLY);
        if (rc != 0) throw new System.ComponentModel.Win32Exception(rc);

        // Step 2 – discover all available targets
        var (allPaths, _) = GetConfig(QDC_ALL_PATHS);
        var availableTargets = new System.Collections.Generic.HashSet<(uint low, int high, uint id)>();
        foreach (var p in allPaths)
        {
            if (p.targetInfo.targetAvailable &&
                TryGetTargetName(p.targetInfo.adapterId, p.targetInfo.id, out _))
            {
                availableTargets.Add((p.targetInfo.adapterId.LowPart, p.targetInfo.adapterId.HighPart, p.targetInfo.id));
            }
        }

        // Step 3 – get the current active config (has valid mode indices)
        var (activePaths, activeModes) = GetConfig(QDC_ONLY_ACTIVE_PATHS);
        var activeTargets = new System.Collections.Generic.HashSet<(uint low, int high, uint id)>();
        var usedSources = new System.Collections.Generic.HashSet<(uint low, int high, uint id)>();
        foreach (var p in activePaths)
        {
            activeTargets.Add((p.targetInfo.adapterId.LowPart, p.targetInfo.adapterId.HighPart, p.targetInfo.id));
            usedSources.Add((p.sourceInfo.adapterId.LowPart, p.sourceInfo.adapterId.HighPart, p.sourceInfo.id));
        }

        var missingTargets = availableTargets.Except(activeTargets).ToList();
        if (missingTargets.Count == 0) return; // all connected monitors are already active

        // Step 4 – find the right edge of the current desktop and a template for cloning mode data
        int rightEdge = 0;
        DISPLAYCONFIG_MODE_INFO templateSourceMode = default;
        DISPLAYCONFIG_MODE_INFO templateTargetMode = default;
        bool hasTemplate = false;

        foreach (var p in activePaths)
        {
            uint si = p.sourceInfo.modeInfoIdx;
            uint ti = p.targetInfo.modeInfoIdx;
            if (si < (uint)activeModes.Length && activeModes[si].infoType == DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE)
            {
                var sm = activeModes[si].u.sourceMode;
                int edge = sm.position.x + (int)sm.width;
                if (edge > rightEdge) rightEdge = edge;
                if (!hasTemplate) templateSourceMode = activeModes[si];
            }
            if (ti < (uint)activeModes.Length && activeModes[ti].infoType == DISPLAYCONFIG_MODE_INFO_TYPE_TARGET)
            {
                if (!hasTemplate) templateTargetMode = activeModes[ti];
            }
            if (si < (uint)activeModes.Length && ti < (uint)activeModes.Length)
                hasTemplate = true;
        }

        if (!hasTemplate) return; // no template available – cannot build mode data

        // Step 5 – for each missing target, clone real mode data from the template
        var pathsList = new System.Collections.Generic.List<DISPLAYCONFIG_PATH_INFO>(activePaths);
        var modesList = new System.Collections.Generic.List<DISPLAYCONFIG_MODE_INFO>(activeModes);

        foreach (var missing in missingTargets)
        {
            foreach (var p in allPaths)
            {
                var tk = (p.targetInfo.adapterId.LowPart, p.targetInfo.adapterId.HighPart, p.targetInfo.id);
                var sk = (p.sourceInfo.adapterId.LowPart, p.sourceInfo.adapterId.HighPart, p.sourceInfo.id);

                if (tk == missing && !usedSources.Contains(sk))
                {
                    var newPath = p;
                    newPath.flags |= DISPLAYCONFIG_PATH_ACTIVE;

                    // Fix potentially invalid enum values on inactive paths
                    if (newPath.targetInfo.rotation == 0)
                        newPath.targetInfo.rotation = 1; // DISPLAYCONFIG_ROTATION_IDENTITY
                    if (newPath.targetInfo.scaling == 0)
                        newPath.targetInfo.scaling = 1; // DISPLAYCONFIG_SCALING_IDENTITY

                    // Clone source mode from template, placed to the right of existing desktop
                    var srcMode = templateSourceMode;
                    srcMode.id = newPath.sourceInfo.id;
                    srcMode.adapterId = newPath.sourceInfo.adapterId;
                    srcMode.u.sourceMode.position.x = rightEdge;
                    srcMode.u.sourceMode.position.y = 0;
                    newPath.sourceInfo.modeInfoIdx = (uint)modesList.Count;
                    modesList.Add(srcMode);

                    // Clone target mode from template
                    var tgtMode = templateTargetMode;
                    tgtMode.id = newPath.targetInfo.id;
                    tgtMode.adapterId = newPath.targetInfo.adapterId;
                    newPath.targetInfo.modeInfoIdx = (uint)modesList.Count;
                    modesList.Add(tgtMode);

                    rightEdge += (int)srcMode.u.sourceMode.width;

                    pathsList.Add(newPath);
                    usedSources.Add(sk);
                    break;
                }
            }
        }

        // Diagnostic: show what we're about to apply
        Log($"\nEnableAllExtended: {activePaths.Length} active + {missingTargets.Count} missing targets");
        foreach (var m in missingTargets)
            Log($"  Missing: adapterId=({m.low},{m.high}) targetId={m.id}");

        // Step 6 – apply with fully populated mode data; SDC_ALLOW_CHANGES adjusts
        //          cloned modes if the new target's native resolution differs.
        ApplySuppliedConfig(pathsList.ToArray(), modesList.ToArray());
    }

    /// <summary>
    /// Set the primary display to the one whose device path contains <paramref name="devicePathSubstring"/>.
    /// The primary display is the one whose source mode position is (0,0).
    /// </summary>
    public static void SetPrimary(string devicePathSubstring)
    {
        var (paths, modes) = GetConfig(QDC_ONLY_ACTIVE_PATHS);

        // Find the source mode index for the target display
        int primarySourceModeIdx = -1;
        for (int i = 0; i < paths.Length; i++)
        {
            var t = paths[i].targetInfo;
            if (!TryGetTargetName(t.adapterId, t.id, out var name)) continue;

            if (!string.IsNullOrEmpty(name.monitorDevicePath) &&
                name.monitorDevicePath.IndexOf(devicePathSubstring, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                primarySourceModeIdx = (int)paths[i].sourceInfo.modeInfoIdx;
                break;
            }
        }

        if (primarySourceModeIdx < 0 || primarySourceModeIdx >= modes.Length)
            throw new InvalidOperationException($"No active target matched device path containing '{devicePathSubstring}'.");

        if (modes[primarySourceModeIdx].infoType != DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE)
            throw new InvalidOperationException("Matched path does not have a valid source mode.");

        // The offset needed to move the target display's source position to (0,0)
        int offsetX = modes[primarySourceModeIdx].u.sourceMode.position.x;
        int offsetY = modes[primarySourceModeIdx].u.sourceMode.position.y;

        // Shift all source modes by the offset so the target becomes (0,0)
        for (int i = 0; i < modes.Length; i++)
        {
            if (modes[i].infoType == DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE)
            {
                modes[i].u.sourceMode.position.x -= offsetX;
                modes[i].u.sourceMode.position.y -= offsetY;
            }
        }

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

    private static void Log(string message)
    {
        Trace.WriteLine(message);
        Console.WriteLine(message);
    }

    /// <summary>
    /// Shift all source mode positions so that the top-left of the active desktop is at (0,0).
    /// MSDN requires (0,0) to be covered by at least one active source.
    /// </summary>
    private static void NormalizeSourcePositions(DISPLAYCONFIG_PATH_INFO[] paths, DISPLAYCONFIG_MODE_INFO[] modes)
    {
        int minX = int.MaxValue, minY = int.MaxValue;
        for (int i = 0; i < paths.Length; i++)
        {
            if ((paths[i].flags & DISPLAYCONFIG_PATH_ACTIVE) == 0) continue;
            uint si = paths[i].sourceInfo.modeInfoIdx;
            if (si >= (uint)modes.Length) continue;
            if (modes[si].infoType != DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE) continue;
            if (modes[si].u.sourceMode.position.x < minX) minX = modes[si].u.sourceMode.position.x;
            if (modes[si].u.sourceMode.position.y < minY) minY = modes[si].u.sourceMode.position.y;
        }
        if (minX == int.MaxValue || (minX == 0 && minY == 0)) return;

        Log($"NormalizeSourcePositions: shifting by ({-minX},{-minY})");
        for (int i = 0; i < modes.Length; i++)
        {
            if (modes[i].infoType == DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE)
            {
                modes[i].u.sourceMode.position.x -= minX;
                modes[i].u.sourceMode.position.y -= minY;
            }
        }
    }

    private static void DumpConfig(string label, DISPLAYCONFIG_PATH_INFO[] paths, DISPLAYCONFIG_MODE_INFO[] modes)
    {
        Log($"\n=== {label} === paths={paths.Length} modes={modes.Length}");
        for (int i = 0; i < paths.Length; i++)
        {
            var p = paths[i];
            bool active = (p.flags & DISPLAYCONFIG_PATH_ACTIVE) != 0;
            TryGetTargetName(p.targetInfo.adapterId, p.targetInfo.id, out var name);
            Log($"  Path[{i}]: active={active} srcId={p.sourceInfo.id} srcModeIdx={p.sourceInfo.modeInfoIdx:X8} srcStatus={p.sourceInfo.statusFlags:X8}"
                + $" tgtId={p.targetInfo.id} tgtModeIdx={p.targetInfo.modeInfoIdx:X8}"
                + $" outTech={p.targetInfo.outputTechnology} rot={p.targetInfo.rotation} scl={p.targetInfo.scaling}"
                + $" refresh={p.targetInfo.refreshRate.Numerator}/{p.targetInfo.refreshRate.Denominator}"
                + $" scanLine={p.targetInfo.scanLineOrdering} avail={p.targetInfo.targetAvailable} tgtStatus={p.targetInfo.statusFlags:X8}"
                + $" flags={p.flags:X8}"
                + $" name={name.monitorFriendlyDeviceName}");
        }
        for (int i = 0; i < modes.Length; i++)
        {
            var m = modes[i];
            if (m.infoType == DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE)
            {
                var s = m.u.sourceMode;
                Log($"  Mode[{i}]: SOURCE id={m.id} {s.width}x{s.height} fmt={s.pixelFormat} pos=({s.position.x},{s.position.y})");
            }
            else if (m.infoType == DISPLAYCONFIG_MODE_INFO_TYPE_TARGET)
            {
                var t = m.u.targetMode.targetVideoSignalInfo;
                Log($"  Mode[{i}]: TARGET id={m.id} active={t.activeSize.cx}x{t.activeSize.cy} total={t.totalSize.cx}x{t.totalSize.cy} pixRate={t.pixelRate} vsync={t.vSyncFreq.Numerator}/{t.vSyncFreq.Denominator}");
            }
            else
            {
                Log($"  Mode[{i}]: type={m.infoType} id={m.id}");
            }
        }
    }

    private static void ApplySuppliedConfig(DISPLAYCONFIG_PATH_INFO[] paths, DISPLAYCONFIG_MODE_INFO[] modes)
    {
        var hPaths = GCHandle.Alloc(paths, GCHandleType.Pinned);
        var hModes = GCHandle.Alloc(modes, GCHandleType.Pinned);
        try
        {
            IntPtr pPaths = hPaths.AddrOfPinnedObject();
            IntPtr pModes = hModes.AddrOfPinnedObject();

            DumpConfig("ApplySuppliedConfig", paths, modes);

            // SetDisplayConfig applies only paths that are marked active when using supplied config. [1](https://learn.microsoft.com/en-us/answers/questions/3983440/disable-one-monitor-on-three-monitor-setups)
            int rc = SetDisplayConfig(
                (uint)paths.Length, pPaths,
                (uint)modes.Length, pModes,
                SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES);

            if (rc != 0)
            {
                Log($"SetDisplayConfig FAILED: rc={rc} (0x{rc:X8})");
                throw new System.ComponentModel.Win32Exception(rc);
            }
        }
        finally
        {
            hPaths.Free();
            hModes.Free();
        }
    }
    public static void EnableOnlyByTargetDevicePathSubstring(string needle)
    {
        // Ensure all connected displays are active so we can find them all
        EnableAllExtended();

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

        NormalizeSourcePositions(paths, modes);
        ApplySuppliedConfig(paths, modes);
    }
    private static void PrintUsage()
    {
        Console.WriteLine("Usage: MyDisplaySwitch <command> [args]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  list                          List all active display targets");
        Console.WriteLine("  extend                        Enable all displays in extended mode");
        Console.WriteLine("  only <devicePathSubstring>    Enable only the display whose device path contains the substring");
        Console.WriteLine("  primary <devicePathSubstring> Set the primary display to the one whose device path contains the substring");
        Console.WriteLine("  toggle                        Toggle between single monitor (UID4165) and all 3 with UID8388688 as primary");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  MyDisplaySwitch list");
        Console.WriteLine("  MyDisplaySwitch extend");
        Console.WriteLine("  MyDisplaySwitch only UID8517");
        Console.WriteLine("  MyDisplaySwitch primary UID8388688");
        Console.WriteLine();
        Console.WriteLine("Start Menu shortcut for 'toggle':");
        Console.WriteLine("  1. Press Win+R → type 'shell:programs' → Enter");
        Console.WriteLine("  2. Right-click → New → Shortcut");
        Console.WriteLine("     Target: \"<path-to>\\MyDisplaySwitch.exe\" toggle");
        Console.WriteLine("  3. (Optional) Right-click shortcut → Properties → set a Shortcut key (e.g. Ctrl+Alt+D)");
        Console.WriteLine("     Set 'Run' to 'Minimized' to avoid a console flash.");

    }
    /*
Targets:
  Path#0:   [\\?\DISPLAY#SDC4187#4&1a73ae31&0&UID8388688#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}]
  Path#1: DELL U2717D  [\\?\DISPLAY#DEL40EA#4&1a73ae31&0&UID4165#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}]
  Path#2: DELL U2717D  [\\?\DISPLAY#DEL40EA#4&1a73ae31&0&UID8517#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}]     
     */
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        try
        {
            switch (args[0].ToLowerInvariant())
            {
                case "list":
                    ListTargets();
                    break;

                case "extend":
                    EnableAllExtended();
                    Console.WriteLine("All displays enabled in extended mode.");
                    break;

                case "toggle":
                    Toggle();
                    break;

                case "only":
                    if (args.Length < 2)
                    {
                        Console.Error.WriteLine("Error: 'only' requires a device path substring argument.");
                        PrintUsage();
                        return 1;
                    }
                    EnableOnlyByTargetDevicePathSubstring(args[1]);
                    Console.WriteLine($"Enabled only display matching '{args[1]}'.");
                    break;

                case "primary":
                    if (args.Length < 2)
                    {
                        Console.Error.WriteLine("Error: 'primary' requires a device path substring argument.");
                        PrintUsage();
                        return 1;
                    }
                    SetPrimary(args[1]);
                    Console.WriteLine($"Set primary display to the one matching '{args[1]}'.");
                    break;

                default:
                    Console.Error.WriteLine($"Unknown command: {args[0]}");
                    PrintUsage();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 2;
        }

        return 0;
    }
}