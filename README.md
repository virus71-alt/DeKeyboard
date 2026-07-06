<div align="center">

<img src="docs/icon.png" alt="Dekeyboard" width="120" height="120" />

# Dekeyboard

**Disable your laptop's built-in keyboard with a hotkey — perfect for 360° convertibles in tablet mode.**

A tiny, silent Windows tray app that turns **only** the internal keyboard (and optionally the touchpad) on/off, so your palms stop typing garbage when the screen is folded back.

![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D6?logo=windows&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)
![UI](https://img.shields.io/badge/UI-WPF%20%2B%20WinForms%20tray-5C2D91)
![Build](https://img.shields.io/badge/build-passing-brightgreen)
![License](https://img.shields.io/badge/license-MIT-yellow)
![Admin](https://img.shields.io/badge/requires-Administrator-red)

</div>

> [!NOTE]
> Built for 360° convertibles such as the **iLife ZEDNote CX5** whose firmware doesn't auto-disable the keyboard when folded into tablet mode. It works on **any** Windows 10/11 laptop, though.

---

## Table of contents

- [Features](#features)
- [Quick demo](#quick-demo)
- [Install & run](#install--run)
- [How it works](#how-it-works)
- [How the internal keyboard is identified](#how-the-internal-keyboard-is-identified)
- [Hotkeys & configuration](#hotkeys--configuration)
- [Tray menu](#tray-menu)
- [Build from source](#build-from-source)
- [Package as a single EXE](#package-as-a-single-exe)
- [Project structure](#project-structure)
- [Troubleshooting](#troubleshooting)
- [FAQ](#faq)
- [Verified test output](#verified-test-output)
- [License](#license)

---

## Features

| | Feature |
|---|---|
| ✅ | **`Win`+`5`** disables **only** the built-in keyboard |
| ✅ | **`Win`+`6`** re-enables it |
| 🔒 | **Auto-lock when folded to tablet (360°)**, auto-unlock in laptop mode |
| 🛡️ | **Fail-safe:** always boots with the keyboard **ON** — a disable can never survive a restart |
| ✅ | USB & Bluetooth keyboards are **never** touched (incl. USB-HID, via device-tree check) |
| ✅ | Auto-detects the internal keyboard (no manual setup) |
| ✅ | Runs silently in the **system tray** with a custom icon |
| ✅ | **Starts with Windows** (elevated, no login UAC prompt) |
| ✅ | Toast **notifications**: *"Laptop keyboard disabled / enabled"* |
| ✅ | Global hotkeys that work in **any** app |
| ✅ | No Device Manager, **no reboot**, no driver uninstall — fully reversible |
| ✅ | Auto-requests **administrator** elevation |
| ✅ | Errors handled gracefully and **logged** to a file |
| 🎁 | **Bonus:** toggle from the tray (menu + double-click) |
| 🎁 | **Bonus:** live keyboard status in the menu |
| 🎁 | **Bonus:** optional **sound** on toggle |
| 🎁 | **Bonus:** fully **configurable hotkeys** |
| 🎁 | **Bonus:** optionally disable the **touchpad** together with the keyboard |
| 🛟 | **Safety:** re-enables the keyboard automatically on exit — you can't get locked out |

---

## Quick demo

```text
  +-----------------------------------------------+
  |  Fold screen back -> tablet mode              |
  |                                               |
  |        press  Win + 5                          |
  |            v                                  |
  |   [x]  "Laptop keyboard disabled"             |
  |        (palms can't type anymore)             |
  |                                               |
  |        press  Win + 6                          |
  |            v                                  |
  |   [/]  "Laptop keyboard enabled"              |
  +-----------------------------------------------+
```

---

## Install & run

### Option A — grab the prebuilt EXE

1. Download **`Dekeyboard.exe`** from the [Releases](../../releases) page.
2. Double-click it → accept the **UAC** prompt.
3. A tray icon appears. Press **`Win`+`5`** / **`Win`+`6`**. Done.

> The self-contained EXE bundles the .NET runtime, so nothing else needs installing.

### Option B — build it yourself

See [Build from source](#build-from-source).

---

## How it works

```mermaid
flowchart LR
    A["Win + 5 / Win + 6"] -->|WH_KEYBOARD_LL hook| B[HotkeyService]
    B --> C[DeviceService]
    C -->|"classify by bus"| D{Internal?}
    D -->|"ACPI / PNP0303 / internal HID"| E["Internal (toggle)"]
    D -->|"USB / Bluetooth / USB-HID"| F["External (ignored)"]
    E --> K{Device<br/>disableable?}
    K -->|yes| G["SetupAPI: disable device node"]
    K -->|"no (convertible)"| L["Input-block: hook swallows<br/>physical keystrokes"]
    G --> I[TrayService]
    L --> I
    I --> J["Toast + sound + status"]
```

**Two Windows internals do the heavy lifting:**

<details>
<summary><b>1. Global hotkeys via a low-level keyboard hook</b> (click to expand)</summary>

<br/>

The Windows shell **reserves** `Win`+`<number>` (they switch taskbar apps), so the usual `RegisterHotKey(MOD_WIN, '5')` **fails**. Instead we install a **`WH_KEYBOARD_LL`** hook that sees every keystroke *before* the shell, fires our action, and **suppresses** the keystroke so the taskbar doesn't react. This makes `Win`+`5`/`Win`+`6` reliable — and every hotkey remains configurable.

</details>

<details>
<summary><b>2. Enable/disable via the supported SetupAPI</b> (click to expand)</summary>

<br/>

`DeviceControl` calls the exact sequence Device Manager itself uses:

```text
SetupDiSetClassInstallParams(DIF_PROPERTYCHANGE, DICS_DISABLE | DICS_ENABLE, DICS_FLAG_GLOBAL)
SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, ...)
```

This is a **first-party Microsoft API**, is fully **reversible**, does **not** uninstall the driver, and needs **no reboot**. (The same thing is possible with the shipped `pnputil /disable-device "<InstanceId>"` — see the [FAQ](#faq).)

</details>

---

## Auto-lock in tablet mode + fail-safe

Two features make this safe to leave running for someone else (e.g. a kid using the laptop):

**Auto-lock on fold.** With `AutoFoldDetection` on (default), Dekeyboard locks the
keyboard when you fold to **360° / tablet** and unlocks it back in **laptop mode**. No
hotkey needed; `Win`+`5` / `Win`+`6` still work as manual overrides.

It reads the **physical device pose from the accelerometer**
(`Windows.Devices.Sensors.SimpleOrientationSensor`, the same sensor that rotates your
screen). Laptop mode reads `NotRotated`; folding to tablet gives `Faceup` (laid flat) or
a rotated pose — a **stable** signal that stays put while folded. A short **debounce**
means a momentary rotation during the fold motion can't flip-flop the keyboard.

> Machines without an accelerometer projection fall back to **display orientation**
> (`EnumDisplaySettings`). Note the display value snaps back to landscape after Windows
> finishes auto-rotating, so the accelerometer path is strongly preferred.
>
> The firmware fold bit (`SM_CONVERTIBLESLATEMODE`) is deliberately *not* used — many
> convertibles (e.g. the CX5) never report it, which is the same reason Windows doesn't
> auto-disable the keyboard on those devices in the first place.
>
> **Verify on your device:** fold it and watch `%APPDATA%\Dekeyboard\log.txt` for a
> `Posture settled -> TABLET` line (and `-> laptop` when you unfold). The startup line
> also tells you which signal is active (`via accelerometer` or
> `via display-orientation fallback`). If neither ever fires, use `Win`+`5`.

**Fail-safe: you can't get locked out.**
- Disabling uses the **non-persistent input-block** method by default (`PreferInputBlock`),
  which dies with the app — so a **reboot always brings the keyboard back**.
- On every startup Dekeyboard **re-enables** the internal keyboard if a previous session
  (or an older build) left it disabled at the device level. Boot = keyboard on, always.
- If the app is closed or crashes, the keyboard is instantly restored.

> **Recovery, just in case:** your convertible has a touchscreen, so you can always
> re-enable via touch — long-press **Start → Device Manager → Keyboards →** long-press
> **Standard PS/2 Keyboard → Enable device** — or plug in a USB keyboard (never blocked).

---

## Two ways to turn the keyboard off

Dekeyboard picks the method automatically, per device:

| Method | When it's used | What it does | Caveat |
|---|---|---|---|
| **Device node** (preferred) | The device reports `DN_DISABLEABLE` | Disables the device node via SetupAPI — exactly like Device Manager | Only works if the device *can* be disabled |
| **Input block** (fallback) | The device is **not** disableable, or SetupAPI refuses | A low-level hook swallows physical keystrokes | Blocks **all** physical keyboards while active; on-screen / touch keyboards keep working |

> [!IMPORTANT]
> Many convertibles — including the **iLife ZEDNote CX5** and the machine this was tested on — mark the built-in keyboard as **non-disableable** (in Device Manager, right-click → *Disable device* is greyed out). On those, `SetupDiCallClassInstaller` fails, so Dekeyboard automatically switches to **input-block mode**. The notification will read *"Laptop keyboard disabled (input-block mode)"*.
>
> **In input-block mode:**
> - Every **physical** keyboard is blocked (the hook can't isolate one device at the input layer), but the **on-screen / touch keyboard still works** — exactly what you want in tablet mode.
> - You can always re-enable with **`Win`+`6`** (checked before the block) **or** by tapping the **tray icon** with touch.
> - If the app exits or crashes, the hook is removed and the keyboard is instantly restored.
>
> Set `"AllowInputBlockFallback": false` in `config.json` to disable this behavior (then a non-disableable keyboard just reports an error instead).

---

## How the internal keyboard is identified

Every keyboard — built-in, USB, or Bluetooth — lives under the **Keyboard** setup class. The app enumerates that class and marks a device **external** when either of these is true:

1. its own **enumerator** is `USB` or `BTH…` (a plain USB / Bluetooth keyboard), **or**
2. an **ancestor in the device tree** sits on the USB or Bluetooth bus — this catches
   **USB-HID** keyboards, which report enumerator `HID` but whose parent is `USB`.

> Step 2 uses CfgMgr32 (`CM_Get_Parent` / `CM_Get_Device_ID`) to walk up the device tree. It's what lets Dekeyboard keep an **internal** I²C/HID keyboard while still ignoring an **external** USB-HID keyboard, even though both share the `HID` enumerator.

Among the remaining **internal** candidates it prefers, in order:

| Priority | Match | Typical device |
|---|---|---|
| 1 | hardware id contains **`PNP0303`** | Standard PS/2 keyboard |
| 2 | enumerator **`ACPI`** | Built-in keyboard controller |
| 3 | enumerator **`HID`** (not behind USB/BT) | I²C keyboard on 2-in-1s |
| 4 | first remaining candidate | fallback |

Every candidate is logged with an `internal` / `EXTERNAL` tag and the final pick is shown, so you can always verify what happened. The touchpad is found the same way under the **Mouse** class, preferring a device described as a *"touch pad"*.

> [!TIP]
> **Auto-detection wrong?** Open `%APPDATA%\Dekeyboard\config.json`, set
> `"KeyboardInstanceId": "ACPI\\...\\..."` (and `"TouchpadInstanceId"`) to the exact id
> from the log, and restart. Your explicit choice always wins.

<details>
<summary>⚙️ <b>Technical note: the class-GUID gotcha</b></summary>

<br/>

The Keyboard/Mouse **device setup class** GUIDs end in **`BFC1`**, not `BFC8`:

```
GUID_DEVCLASS_KEYBOARD = {4D36E96B-E325-11CE-BFC1-08002BE10318}
GUID_DEVCLASS_MOUSE    = {4D36E96F-E325-11CE-BFC1-08002BE10318}
```

Using `BFC8` makes `SetupDiGetClassDevs` return **zero** devices with no error. This was caught and fixed during on-device testing (see [Verified test output](#verified-test-output)).

</details>

---

## Hotkeys & configuration

Config lives at **`%APPDATA%\Dekeyboard\config.json`** (created on first run). Edit it, then restart the app — or use the tray menu for the common toggles.

```jsonc
{
  // Disable the internal keyboard
  "DisableHotkey": { "Win": true, "Ctrl": false, "Alt": false, "Shift": false, "Key": "5" },
  // Enable it again
  "EnableHotkey":  { "Win": true, "Ctrl": false, "Alt": false, "Shift": false, "Key": "6" },

  "PlaySound": true,                    // sound on toggle
  "DisableTouchpadWithKeyboard": false, // also disable the touchpad
  "AllowInputBlockFallback": true,      // block keystrokes when the device can't be disabled
  "PreferInputBlock": true,             // SAFETY: use non-persistent blocking, never a hard device disable
  "AutoFoldDetection": true,            // auto lock/unlock when folding to tablet mode
  "SuppressHotkeyKeystroke": true,      // swallow the key so the taskbar ignores Win+5/6

  "KeyboardInstanceId": null,           // set to pin a specific keyboard
  "TouchpadInstanceId": null            // set to pin a specific touchpad
}
```

| Setting | Type | Default | Meaning |
|---|---|---|---|
| `DisableHotkey` / `EnableHotkey` | object | `Win+5` / `Win+6` | Modifier flags + a `Key` (`"5"`, `"K"`, `"F9"`, …) |
| `PlaySound` | bool | `true` | Play a system sound on each toggle |
| `DisableTouchpadWithKeyboard` | bool | `false` | Disable the internal touchpad alongside the keyboard |
| `AllowInputBlockFallback` | bool | `true` | If the keyboard can't be device-disabled, block hardware keystrokes instead ([details](#two-ways-to-turn-the-keyboard-off)) |
| `PreferInputBlock` | bool | `true` | **Safety.** Always use the non-persistent input-block method so a disable can't survive a reboot. Set `false` for a hard device-node disable |
| `AutoFoldDetection` | bool | `true` | Auto lock/unlock the keyboard when folding to / from tablet mode |
| `SuppressHotkeyKeystroke` | bool | `true` | Prevent the hotkey from also reaching other apps |
| `KeyboardInstanceId` | string? | `null` | Force a specific keyboard device instance id |
| `TouchpadInstanceId` | string? | `null` | Force a specific touchpad device instance id |

**Prefer non-reserved hotkeys?** Set e.g. `"Ctrl": true, "Alt": true, "Key": "K"` for `Ctrl`+`Alt`+`K`.

---

## Tray menu

Right-click the tray icon (double-click = quick toggle):

```text
+---------------------------------+
| Status: keyboard enabled        |   <- live status
+---------------------------------+
| Disable laptop keyboard         |   <- manual toggle
+---------------------------------+
| [x] Auto-lock in tablet mode    |   <- fold detection
| [x] Play sound on toggle        |
| [ ] Also disable touchpad       |
| [x] Start with Windows          |
+---------------------------------+
| View log...                     |
| Quit                            |
+---------------------------------+
```

---

## Build from source

**Prerequisites:** [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (Windows).

```powershell
git clone https://github.com/<you>/dekeyboard.git
cd dekeyboard

dotnet restore
dotnet build -c Release

# Run (UAC prompt appears — the app requires administrator)
dotnet run --project Dekeyboard
```

Or open **`Dekeyboard.sln`** in Visual Studio 2022 (17.8+) with the *.NET desktop development* workload and press **F5**.

---

## Package as a single EXE

**Self-contained** (no runtime needed on the target, ~138 MB):

```powershell
dotnet publish Dekeyboard -c Release -r win-x64 `
  -p:PublishSingleFile=true --self-contained true `
  -p:IncludeNativeLibrariesForSelfExtract=true
```

**Framework-dependent** (tiny, needs the .NET 8 Desktop Runtime installed):

```powershell
dotnet publish Dekeyboard -c Release -r win-x64 `
  -p:PublishSingleFile=true --self-contained false
```

Output: `Dekeyboard\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\Dekeyboard.exe`
On ARM convertibles, use `-r win-arm64`.

---

## Project structure

```
Dekeyboard/                         (repo root)
├── Dekeyboard.sln
├── README.md · LICENSE · .gitignore
├── docs/
│   └── icon.png                    # icon used in this README
└── Dekeyboard/
    ├── Dekeyboard.csproj           # .NET 8 WPF + WinForms tray, requires admin
    ├── Dekeyboard.ico              # app + embedded tray icon
    ├── app.manifest                # requireAdministrator + PerMonitorV2
    ├── appsettings.json            # config template (live config is in %APPDATA%)
    ├── App.xaml(.cs)               # headless composition root, elevation, wiring
    ├── Configuration/
    │   └── AppConfig.cs            # JSON config: hotkeys, sound, touchpad, overrides
    ├── Interop/
    │   ├── NativeMethods.cs        # SetupAPI + CfgMgr32 + low-level keyboard hook
    │   └── DeviceControl.cs        # enumerate, device-tree walk, enable/disable
    └── Services/
        ├── Logger.cs               # thread-safe file logger
        ├── DeviceService.cs        # identifies + toggles keyboard/touchpad, safety re-enable
        ├── FoldDetectionService.cs # auto lock/unlock on tablet-mode fold
        ├── HotkeyService.cs        # global hotkeys + input-block via WH_KEYBOARD_LL
        ├── StartupService.cs       # "start with Windows" via elevated Scheduled Task
        └── TrayService.cs          # NotifyIcon, menu, notifications, sound, icon
```

**Runtime data (never in the repo):** `%APPDATA%\Dekeyboard\` → `config.json`, `log.txt`.

---

## Troubleshooting

<details>
<summary><b>Hotkeys don't do anything</b></summary>

- Check `%APPDATA%\Dekeyboard\log.txt` — the line `Hotkeys active: …` confirms the hook installed.
- Another app may hold a conflicting low-level hook. Try different keys (e.g. `Ctrl`+`Alt`+`K`).
- Make sure the app is actually running (tray icon present) and elevated.
</details>

<details>
<summary><b>It toggled the wrong keyboard / touchpad</b></summary>

- Open the log — it lists every keyboard with an `internal` / `EXTERNAL` tag and which one was selected.
- Copy the correct instance id into `KeyboardInstanceId` (and `TouchpadInstanceId`) in `config.json`, then restart.
</details>

<details>
<summary><b>Auto-start shows a UAC prompt at login</b></summary>

It shouldn't — auto-start uses a *"Run with highest privileges"* Scheduled Task that starts elevated silently. If you see a prompt, toggle **Start with Windows** off and on again in the tray menu to recreate the task.
</details>

<details>
<summary><b>"Could not disable keyboard: SetupDiCallClassInstaller failed"</b></summary>

This means Windows won't disable that keyboard's device node — the device is **non-disableable** (common on convertibles; *Disable device* is greyed out in Device Manager). Dekeyboard normally handles this automatically by switching to [input-block mode](#two-ways-to-turn-the-keyboard-off). If you see this error, `AllowInputBlockFallback` is `false` in your `config.json` — set it to `true` and restart.
</details>

<details>
<summary><b>Keyboard still types after being "disabled"</b></summary>

If you're in **device-node** mode and a few I²C/HID keyboards keep sending input (a firmware quirk), set `"AllowInputBlockFallback": true` and, if needed, pin the keyboard so it uses input blocking. In **input-block** mode all physical keys are already suppressed at the OS input layer.
</details>

---

## FAQ

<details>
<summary><b>Does this uninstall my keyboard driver?</b></summary>
No. It flips the device node's enabled/disabled state — the same reversible action as Device Manager's right-click → Disable. Nothing is uninstalled and no reboot is needed.
</details>

<details>
<summary><b>Will it disable my USB or Bluetooth keyboard?</b></summary>
In <b>device-node</b> mode, no — only the identified internal keyboard is disabled; USB / Bluetooth (incl. USB-HID) devices are excluded. In <b>input-block</b> fallback mode, the hook can't isolate a single device, so <b>all</b> physical keyboards are blocked while active (on-screen / touch keyboards still work). That mode only kicks in when the internal keyboard can't be device-disabled — the exact situation where you're using touch anyway.
</details>

<details>
<summary><b>Why does it need administrator rights?</b></summary>
Changing a device's enabled state through SetupAPI requires elevation. The app requests it automatically (UAC) and, for auto-start, uses an elevated Scheduled Task so you're not prompted at every login.
</details>

<details>
<summary><b>Can I use PnPUtil instead?</b></summary>
Yes — the same effect is achievable with the built-in CLI:

```powershell
pnputil /disable-device "ACPI\PNP0303\4&xxxx&0"
pnputil /enable-device  "ACPI\PNP0303\4&xxxx&0"
```

Dekeyboard uses the in-process SetupAPI path by default (faster, no external process). Get instance ids with `pnputil /enum-devices /class Keyboard`.
</details>

<details>
<summary><b>What if the app crashes while the keyboard is disabled?</b></summary>
On normal exit it auto-re-enables the keyboard. If it's force-killed while disabled, just relaunch and press the enable hotkey, or re-enable the device in Device Manager (you have the touchscreen / an external keyboard to do so).
</details>

---

## Verified test output

Real disable/enable cycle on a Windows 11 laptop (from `log.txt`), showing the
automatic fallback when the internal keyboard is non-disableable:

```log
[INFO ] === Dekeyboard starting ===
[INFO ] Hotkeys active: disable='Win + 5', enable='Win + 6'.
[INFO ] Found 2 keyboard device(s):
[INFO ]     - EXTERNAL | HID Keyboard Device    | enum=HID  | id=HID\VID_3151&PID_5031&MI_01&COL03\7&1B5E007A&0&0002
[INFO ]     - internal | Standard PS/2 Keyboard | enum=ACPI | id=ACPI\1025171E\4&33EE29D&0
[INFO ] Selected internal keyboard: Standard PS/2 Keyboard [ACPI\1025171E\4&33EE29D&0]
[INFO ] Dekeyboard ready (running in tray).
... Win+5 ...
[INFO ] Keyboard 'ACPI\1025171E\4&33EE29D&0' disableable=False.
[INFO ] Keyboard held off via input-block fallback (hardware keys suppressed).
... Win+6 ...
[INFO ] Input-block fallback lifted; hardware keys restored.
```

✔️ Builds clean (`0 warnings, 0 errors`) · ✔️ elevates via UAC · ✔️ installs the global hook · ✔️ device-tree walk tags the USB-HID keyboard **EXTERNAL** and picks the **internal** PS/2 keyboard · ✔️ detects the non-disableable device and **falls back to input-block mode**, with `Win`+`6` still able to re-enable.

---

## License

[MIT](LICENSE) © 2026 Rahul Singh

<div align="center">
<sub>Made for convertibles that forgot how to fold. If it saved your tablet-mode sanity, ⭐ the repo.</sub>
</div>
