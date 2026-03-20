# MyDisplaySwitch

A Windows command-line utility for switching display configurations using the [CCD (Connecting and Configuring Displays)](https://learn.microsoft.com/en-us/windows-hardware/drivers/display/ccd-apis) API.

Useful for quickly toggling between a single-monitor setup and a multi-monitor extended desktop — especially via a Start Menu shortcut or keyboard hotkey.

On my adjustable height desk, I have a laptop with two external monitors on monitor arms. Normally, I have the laptop screen as the main display (with the taskbar).
When I ride my exercise bike daily, I can raise the desk and turn the right external monitor on its arm to face the bike so I can view the screen and use my external wireless mouse/keyboard/earbuds while riding.
For years, this worked fine. I could set the screen content and be very careful while riding, knowing that e.g. alt-tab would show on the screen I couldn't see, and that I had no access to the task bar.

With this utility, I can quickly toggle between all displays (in my normal work mode) and only the right most display being used (exercise mode) with a single Windows shortcut,
without fiddling with the Windows display settings. 
I know the Windows key + P shortcut allows to switch between display modes, but it doesn't allow me to specify which monitor to use.
Now while exercising I have full access to the taskbar and can move my mouse and alt-tab on the bike without worrying about which screen is active.

## Requirements

- Windows 10/11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

## Build

```powershell
dotnet build -c Release
```

Or publish a self-contained single-file executable:

```powershell
dotnet publish -c Release -r win-x64 --self-contained false
```

The output will be in `bin\Release\net10.0\win-x64\publish\`.

## Usage

```
MyDisplaySwitch <command> [args]
```

### Commands

| Command | Description |
|---------|-------------|
| `list` | List all active display targets (friendly name + device path) |
| `extend` | Enable all connected displays in extended mode |
| `only <substring>` | Enable only the display whose device path contains `<substring>`, disable all others |
| `primary <substring>` | Set the primary display (position 0,0) to the one whose device path contains `<substring>` |
| `toggle` | Toggle between single-monitor and all-monitors mode (see below) |

### Examples

```powershell
# List active displays and their device paths
MyDisplaySwitch list

# Enable all displays in extended mode
MyDisplaySwitch extend

# Keep only the display whose path contains "UID8517", disable the rest
MyDisplaySwitch only UID8517

# Make the display whose path contains "UID8388688" the primary
MyDisplaySwitch primary UID8388688

# Toggle: 3 monitors → single (UID4165); 1 monitor → all 3 with UID8388688 as primary
MyDisplaySwitch toggle
```

### Finding your display identifiers

Run `MyDisplaySwitch list` to see output like:

```
Targets:
  Path#0:   [\\?\DISPLAY#SDC4187#4&1a73ae31&0&UID8388688#{...}]
  Path#1: DELL U2717D  [\\?\DISPLAY#DEL40EA#4&1a73ae31&0&UID4165#{...}]
  Path#2: DELL U2717D  [\\?\DISPLAY#DEL40EA#4&1a73ae31&0&UID8517#{...}]
```

Use any unique substring from the device path (e.g. `UID8517`, `DEL40EA`, `SDC4187`) as the `<substring>` argument.

## Toggle behavior

The `toggle` command checks how many monitors are currently active:

- **3 or more active** → switches to only the display matching `UID4` (DELL UID4165)
- **1 or 2 active** → enables all displays in extended mode and sets `UID83` (laptop UID8388688) as primary

Edit the `Toggle()` method in `Program.cs` to customize the substrings for your own hardware.

## Adding a Start Menu shortcut

1. **Publish** the project (see Build section above)

2. **Open the Start Menu Programs folder:**
   - Press `Win+R`, type `shell:programs`, press Enter
   - This opens `%APPDATA%\Microsoft\Windows\Start Menu\Programs`

3. **Create a shortcut:**
   - Right-click in the folder → **New** → **Shortcut**
   - Target: `"C:\path\to\MyDisplaySwitch.exe" toggle`
   - Name: `Toggle Displays`

4. **(Optional) Set a keyboard shortcut:**
   - Right-click the shortcut → **Properties** → **Shortcut** tab
   - Click the **Shortcut key** field and press your desired combo (e.g. `Ctrl+Alt+D`)
   - Set **Run** to **Minimized** to avoid a console window flash

5. The shortcut now appears in Start Menu search and can be pinned to Start or Taskbar.

## How it works

The utility uses the Windows CCD API via P/Invoke:

- [`QueryDisplayConfig`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-querydisplayconfig) — enumerates display paths and modes
- [`SetDisplayConfig`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setdisplayconfig) — applies display configuration changes
- [`DisplayConfigGetDeviceInfo`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-displayconfiggetdeviceinfo) — retrieves friendly names and device paths

Key operations:
- **Extend** uses `SDC_TOPOLOGY_EXTEND` to activate displays from the Windows topology database, then detects and adds any missing connected monitors
- **Only** deactivates unwanted paths and normalizes source positions so the remaining display is at (0,0)
- **Primary** shifts all source mode positions to place the target display at the desktop origin

## License

MIT
