# Monitor Display Switcher (`DisplayInputSwitcher`)

A lightweight, single-file (`~196 KB`), zero-dependency universal portable Windows desktop application (`AnyCPU`: native `x64`, `ARM64`, and `x86`) for switching external monitor **HDMI**, **DisplayPort**, and **USB-C** inputs instantly over **DDC/CI (`Dxva2.dll`)**.

---

## Highlights

- **Universal Monitor Input Auto-Detection** — Automatically reads each monitor's VESA EDID (`ManufacturerCode`, `MonitorName`, `SerialNumber`) and MCCS `vcp(...)` capability table to resolve the right VCP feature code and input values out of the box—no manual lookup needed across:
  - **Standard VESA MCCS Monitors** (*Dell, ASUS, MSI, Gigabyte, BenQ, Samsung, Acer, HP, AOC, ViewSonic*): `VCP 0x60` (`15` = DP 1, `16` = DP 2, `17` = HDMI 1, `18` = HDMI 2, `19` = HDMI 3, `27` = USB-C)
  - **LG Compact DDC/CI Monitors** (*LG UltraGear / UltraWide*): `VCP 0x60` (`1` = HDMI, `3` = DisplayPort)
  - **LG Gen5 Extended OSD Monitors**: `VCP 0xF4` or `0x60` (`144` = HDMI 1, `145` = HDMI 2, `208` = DisplayPort, `209` = USB-C)
  - **3+ Port Monitors**: Automatically displays a 1-click port chip bar and tray menu items when a display exposes more than two inputs (e.g., `HDMI 1`, `HDMI 2`, `DP 1`, `USB-C`).
  - **Per-Monitor Saved Profiles & 1-Click Presets**: Customize or swap (`⇄`) `VCP`, `HDMI`, and `DP` codes independently per monitor (keyed by EDID serial / device ID).
- **Universal Windows Portability (`AnyCPU`)** — Compiles natively using the built-in Windows `.NET Framework 4.x` compiler (`csc.exe` + WPF DirectX vector UI) with zero external DLLs or installation required across `x64`, `ARM64`, and `x86` Windows devices.
- **Normalized 1-Byte `VCP 0x60` / `0xF4` Boundary & Index-`0` Handle Ownership** — Masks input replies (`& 0xFF`) consistently across UI reads, quick-toggles, and hotkeys while preserving raw `uint32` diagnostic metadata, and properly owns `Dxva2` 0-based `PHYSICAL_MONITOR` handles (`0x0`, `0x1`, ...).
- **Global Keyboard Shortcuts (Blind Switch-Back Ready)** — Customize shortcuts directly inside Settings:
  - `Ctrl+Alt+1` — Switch to **HDMI**
  - `Ctrl+Alt+2` — Switch to **DisplayPort**
  - `Ctrl+Alt+S` — **Toggle** `HDMI ⇄ DisplayPort`
- **Dark System Tray Integration** — Left-click tray icon to open, middle-click to toggle inputs, or right-click for a dark quick-switch context menu, **Always on Top** pin, and **Exit Completely**.
- **Collapsible Quick Controls Drawer** — Live debounced hardware sliders for **Brightness (`VCP 10`)**, **Speaker Volume (`VCP 62`)**, and **Audio Mute (`VCP 8D`)**.

---

## Hardware Requirement

External monitors must have **DDC/CI** enabled in their built-in physical OSD menu (enabled by default on almost all modern monitors) over a direct HDMI, DisplayPort, or USB-C DisplayPort Alt-Mode connection. Internal laptop eDP panels do not expose input-switching over DDC/CI and are automatically skipped in favor of external displays.

---

## Project Structure

```text
DisplayInputSwitcher/
├── build.ps1                         # Generates icon, compiles AnyCPU portable .exe, runs self-test
├── scripts/
│   └── generate_icon.py              # Pure-Python multi-size (16..256px) ICO generator
└── src/
    ├── app.manifest                  # Per-Monitor V2 High-DPI & Windows compatibility manifest
    ├── Program.cs                    # Single-instance IPC, CLI mode, and self-test runner
    ├── Core/
    │   ├── DdcCiModels.cs            # Domain models, result types, and per-monitor config schema
    │   ├── EdidParser.cs             # VESA 128-byte EDID parser (PnP ID, Name, Serial)
    │   ├── InputProfileResolver.cs   # Universal VESA / LG Compact / LG Gen5 VCP input resolver
    │   └── MccsCapabilitiesParser.cs # VESA MCCS vcp(...) discrete capabilities parser
    ├── Native/
    │   └── NativeMethods.cs          # Win32 Dxva2.dll, User32.dll, and Graphics I2C error codes
    ├── Services/
    │   ├── ConfigService.cs          # Atomic portable JSON config & per-monitor profile store
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

`DisplayInputSwitcher.exe` also supports headless fast-switching flags with automatic monitor scheme detection:

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
