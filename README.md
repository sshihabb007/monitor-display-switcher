# Monitor Display Switcher (`DisplayInputSwitcher`)

A lightweight, single-file (`~176 KB`), zero-dependency portable Windows x64 desktop application for switching external monitor **HDMI** and **DisplayPort** inputs instantly over **DDC/CI (`Dxva2.dll`)**.

---

## Highlights

- **True Single-File Portable `.exe`** — Compiles natively using the built-in Windows .NET Framework x64 compiler (`csc.exe` + WPF DirectX vector UI) with zero external DLLs or installation required.
- **Smart Hardware & VESA EDID Discovery** — Correlates `EnumDisplayMonitors`, `EnumDisplayDevicesW`, and Windows Registry 128-byte EDID descriptors (`0xFC` Monitor Name + `0xFF` Serial Number). Automatically prioritizes external DDC/CI-capable monitors (such as **LG UltraGear**) ahead of internal laptop eDP panels.
- **1-Click HDMI & DisplayPort Cards + Instant Swap (`⇄`)** — Pre-configured for `VCP 0x60` values `1` (`HDMI`) and `3` (`DisplayPort`), with a center `⇄` button to swap mappings and full support for custom VCP `60` codes (`0–255`).
- **Normalized 1-Byte `VCP 0x60` Boundary** — Masks `VCP 0x60` replies (`& 0xFF`) consistently across UI reads, quick-toggles, and hotkeys while preserving raw `uint32` diagnostic metadata, avoiding high-byte status mismatches on LG/Dell/BenQ scalers.
- **Global Keyboard Shortcuts (Blind Switch-Back Ready)** — Customize shortcuts directly inside Settings:
  - `Ctrl+Alt+1` — Switch to **HDMI**
  - `Ctrl+Alt+2` — Switch to **DisplayPort**
  - `Ctrl+Alt+S` — **Toggle** `HDMI ⇄ DisplayPort`
- **Dark System Tray Integration** — Left-click tray icon to open, middle-click to toggle inputs, or right-click for a dark quick-switch context menu, **Always on Top** pin, and **Exit Completely**.
- **Collapsible Quick Controls Drawer** — Live debounced hardware sliders for **Brightness (`VCP 10`)**, **Speaker Volume (`VCP 62`)**, and **Audio Mute (`VCP 8D`)**.

---

## Project Structure

```text
DisplayInputSwitcher/
├── build.ps1                         # Generates icon, compiles x64 portable .exe, runs self-test
├── scripts/
│   └── generate_icon.py              # Pure-Python multi-size (16..256px) ICO generator
└── src/
    ├── app.manifest                  # Per-Monitor V2 High-DPI Win32 manifest
    ├── Program.cs                    # Single-instance IPC, CLI mode, and self-test runner
    ├── Core/
    │   ├── DdcCiModels.cs            # Domain models, result types, and portable config schema
    │   ├── EdidParser.cs             # VESA 128-byte EDID parser (PnP ID, Name, Serial)
    │   └── MccsCapabilitiesParser.cs # VESA MCCS vcp(...) discrete capabilities parser
    ├── Native/
    │   └── NativeMethods.cs          # Win32 Dxva2.dll, User32.dll, and Graphics I2C error codes
    ├── Services/
    │   ├── ConfigService.cs          # Atomic portable JSON config & optional startup entry
    │   ├── DdcCiService.cs           # Serialized I2C bus engine, retry, and handle recovery
    │   ├── GlobalHotkeyManager.cs    # Win32 RegisterHotKey & WM_DISPLAYCHANGE hook
    │   └── MonitorDiscoveryService.cs# HMONITOR + PHYSICAL_MONITOR + Registry EDID correlation
    └── UI/
        ├── MainWindow.cs             # Borderless dark rounded WPF card UI
        ├── ModernDarkSlider.cs       # Custom vector dark slider control
        └── TrayIconManager.cs        # Dynamic badge NotifyIcon & dark context menu
```

---

## Building from Source

Requirements: Windows 10/11 (uses built-in `.NET Framework 4.x` `csc.exe`) and Python 3 (for initial `.ico` generation if `assets\app.ico` is not yet present).

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

- Output binary: `dist\DisplayInputSwitcher.exe`
- Pass `-SkipDesktopCopy` to build without copying the `.exe` to Desktop:
  ```powershell
  powershell -ExecutionPolicy Bypass -File .\build.ps1 -SkipDesktopCopy
  ```

---

## Command-Line Automation Flags

`DisplayInputSwitcher.exe` also supports headless fast-switching flags:

```powershell
.\DisplayInputSwitcher.exe --hdmi                  # Switch target monitor to HDMI
.\DisplayInputSwitcher.exe --dp                    # Switch target monitor to DisplayPort
.\DisplayInputSwitcher.exe --toggle                # Toggle between HDMI and DisplayPort
.\DisplayInputSwitcher.exe --status --out info.txt # Query live read-only DDC/CI snapshot
.\DisplayInputSwitcher.exe --minimized             # Start silently in System Tray
```

---

## Developer

**Mehedi Hasan Shihab** ([@sshihabb007](https://github.com/sshihabb007))
