# Get Over Here!

**Keep your mouse cursor and keyboard focus on your main screen when you tap a secondary touch screen.**

Get Over Here! is a tiny Windows tray app for people who use an Android tablet as a second, touch-enabled
monitor through [SuperDisplay](https://superdisplay.app/). Tap the tablet with your finger, and the cursor and
the keyboard focus snap right back to where they were on your main screen.

---

## The problem

When you touch a secondary touch screen on Windows:

- the mouse cursor **teleports** to the spot you touched, far away from where you were working;
- the window you touched **steals the keyboard focus**, so what you type next goes to the wrong app.

If you use the tablet as a control surface (music player, chat, dashboard, stream deck...) while working on your
main screen, you end up reaching for the mouse and clicking back into your window after every tap.

## What it does

- **Finger taps bring everything back.** As soon as your finger leaves the tablet, the cursor returns to its exact
  previous position and the window you were typing in gets the focus back.
- **Your touch still works.** The tap, scroll or button press still reaches the app on the tablet; only the side
  effects are undone.
- **Mouse, touchpad and pen are left alone.** Only finger input is affected, so you can still move the mouse onto
  the tablet or draw with a stylus as usual.
- **Lives in the notification area** and starts automatically with Windows.
- **Detects the SuperDisplay monitor automatically**, so touches on any other screen are ignored.
- **Optional extras:** an experimental "zero-move" mode that stops the cursor from moving at all, and a
  diagnostic log for troubleshooting.

## Requirements

- Windows 10 or Windows 11 (64-bit)
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64)
- An Android tablet connected as a second monitor with [SuperDisplay](https://superdisplay.app/)

> If no SuperDisplay monitor is detected, the app treats every touch as coming from the secondary screen, so it also
> works on a PC with a single external touch screen.

## Installation

### Option 1: Download the app (recommended)

1. **Install the .NET 10 Desktop Runtime** if you don't have it yet:
   go to the [.NET 10 download page](https://dotnet.microsoft.com/download/dotnet/10.0) and, under
   *.NET Desktop Runtime*, download the **Windows x64** installer.
2. **Download `GetOverHere.exe`** from the [latest release](https://github.com/im-tomasz/get-over-here/releases/latest).
3. **Move it to a permanent folder**, for example `C:\Users\<you>\AppData\Local\GetOverHere\` or
   `C:\Tools\GetOverHere\`. The Windows startup entry points to this file, so avoid leaving it in your Downloads
   folder. (If you move it later, the startup entry is updated the next time you launch it.)
4. **Double-click `GetOverHere.exe`.**
   - Windows SmartScreen may warn you because the executable is not signed. Click **More info** →
     **Run anyway**.
5. A blue icon appears in the notification area (look in the hidden icons `^` if you don't see it) along with
   a welcome notification. **That's it**: tap the tablet and the cursor comes back on its own.

The app registers itself to start with Windows on first launch. You can turn this off from its menu.

### Option 2: Build from source

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and [Git](https://git-scm.com/).
2. Clone the repository:

   ```bash
   git clone https://github.com/im-tomasz/get-over-here.git
   ```

   ```bash
   cd get-over-here
   ```

3. Build a single-file executable:

   ```bash
   dotnet publish GetOverHere -c Release -r win-x64 -p:SelfContained=false -p:PublishSingleFile=true -p:DebugType=none -o GetOverHere/dist
   ```

4. The app is now at `GetOverHere\dist\GetOverHere.exe`. Continue from step 3 of Option 1.

> If the app is already running, quit it first (right-click the icon → **Quit**); otherwise the executable is
> locked and the build fails.

### Uninstalling

1. Right-click the tray icon and untick **Start with Windows**.
2. Right-click again → **Quit**.
3. Delete `GetOverHere.exe` and the `%APPDATA%\GetOverHere` folder (settings and log).

## Usage

- **Double-click** the tray icon to pause or resume the app. The icon turns grey when paused.
- **Hover** over the icon to see whether the app is active and whether the tablet was detected.
- **Right-click** the icon for the menu:

| Menu item | What it does |
|---|---|
| **Enabled** | Master on/off switch |
| **Also restore keyboard focus** | Gives the focus back to the window you were typing in (on by default) |
| **Zero-move mode (experimental)** | Blocks the cursor moves caused by touch, so the cursor never leaves. Modern apps still receive the touch; some older apps may stop responding to it |
| **Start with Windows** | Launches the app when you sign in to Windows |
| **Diagnostic log** | Records every event to `%APPDATA%\GetOverHere\diag.log` |
| **Open log** | Opens the log file |
| **Open settings folder** | Opens `%APPDATA%\GetOverHere` |
| **Quit** | Closes the app |

## Configuration

Settings are stored in `%APPDATA%\GetOverHere\settings.json`. The options above can be changed from the menu; the
timing values can only be edited in the file (quit the app first, then restart it to apply them).

| Setting | Default | Description |
|---|---|---|
| `Enabled` | `true` | Master on/off switch |
| `RestoreFocus` | `true` | Also give the keyboard focus back to the previous window |
| `ZeroMoveMode` | `false` | Experimental: block the cursor moves generated by touch |
| `StartWithWindows` | `true` | Launch at Windows startup |
| `DiagnosticLog` | `false` | Write events to `diag.log` |
| `RestoreDelayMs` | `60` | Delay after the finger is lifted before the cursor comes back |
| `HeldTimeoutMs` | `4000` | Restore after this much silence if the finger release is never seen |
| `RawQuietMs` | `250` | Silence that ends a touch seen only through raw touch-screen reports |
| `FocusGuardMs` | `1500` | How long after a touch a late focus grab by the touched window is still undone |

## How it works

1. **Telling touch apart from the mouse.** A low-level mouse hook (`WH_MOUSE_LL`) inspects every mouse event.
   Windows stamps events generated by a touch screen or a pen with the signature `0xFF5157xx` in `dwExtraInfo`
   (bit `0x80` set = finger). Anything without it is the real mouse.
2. **Knowing when the finger lands.** Those mouse events only arrive when the finger is *lifted*, but the touched
   window grabs the focus as soon as the finger *lands*. So the app also reads the touch screen directly through
   Raw Input, and keeps a timestamped history of foreground windows to find the one that was active just before the
   touch.
3. **Putting everything back.** When the touch ends (60 ms after release, or immediately if you move the real
   mouse), the cursor is moved back to the exact pixel and the original window is brought back to the foreground.
   For 1.5 seconds afterwards, if the touched window tries to grab the focus late, it is taken back again.

For more technical details (code map, design notes), see [GetOverHere/README.md](GetOverHere/README.md).

## Running the tests

The `GetOverHere.Tests` project uses xUnit. From the repository root:

```bash
dotnet test GetOverHere.slnx
```

- **Unit tests** (~150, under a second) replay touch scenarios to the millisecond with a simulated clock, timer and
  windows.
- **Integration tests** (~20, a few seconds) use the real Windows hook, cursor and windows. They briefly move your
  cursor and change the active window. **Quit Get Over Here! before running them**, otherwise it reacts to the
  simulated touches.

To run the unit tests only:

```bash
dotnet test GetOverHere.slnx --filter Category!=Integration
```

## Project structure

```
get-over-here/
├── GetOverHere/          # The tray application (WinForms, .NET 10)
├── GetOverHere.Tests/    # Unit and integration tests (xUnit)
└── GetOverHere.slnx      # Solution file
```

## Known limitations

- A window running **as administrator** cannot get its focus back from a non-admin app (Windows security
  isolation). Run Get Over Here! as administrator too if you need this.
- Some multi-finger gestures (pinch, two-finger scroll) don't generate any mouse event at all. The cursor doesn't
  move in that case, so there is nothing to fix.

## Upgrading from TouchBack

The app was previously called *TouchBack*. On first launch, Get Over Here! imports your old settings from
`%APPDATA%\TouchBack\settings.json` (if it has none of its own yet) and replaces the old startup entry with its own.
The old `TouchBack` folder is left untouched and can be deleted.
