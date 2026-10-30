<div align="center">

# Monitor Display Switcher

**Universal Windows DDC/CI HDMI, DisplayPort & USB-C Input Switcher + Hardware Brightness & Volume Controller**

Developed by **[Mehedi Hasan Shihab](https://sshihabb007.github.io/)** (`@sshihabb007`)

[![Latest Release](https://img.shields.io/github/v/release/sshihabb007/monitor-display-switcher?label=Release&color=7C5CFF&style=for-the-badge&logo=github)](https://github.com/sshihabb007/monitor-display-switcher/releases/latest)
[![Build & Release](https://img.shields.io/github/actions/workflow/status/sshihabb007/monitor-display-switcher/build.yml?branch=main&label=Build&color=10B981&style=for-the-badge&logo=githubactions&logoColor=white)](https://github.com/sshihabb007/monitor-display-switcher/actions/workflows/build.yml)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20(x64%20%7C%20ARM64%20%7C%20x86)-06B6D4?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/sshihabb007/monitor-display-switcher/releases/latest)
[![Zero Dependencies](https://img.shields.io/badge/.NET%204.x-Zero%20Dependencies-6366F1?style=for-the-badge&logo=dotnet&logoColor=white)](#build-from-source)
[![License: MIT](https://img.shields.io/badge/License-MIT-F59E0B?style=for-the-badge)](LICENSE)

<br />

[![Download Setup Installer](https://img.shields.io/badge/%E2%AC%87%20Download%20Setup%20Installer-v1.2.0-7C5CFF?style=for-the-badge&logo=windows11&logoColor=white)](https://github.com/sshihabb007/monitor-display-switcher/releases/latest/download/Monitor-Display-Switcher-Setup-1.2.0.exe)
[![Download Portable EXE](https://img.shields.io/badge/%E2%AC%87%20Download%20Portable%20EXE-v1.2.0-0EA5E9?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/sshihabb007/monitor-display-switcher/releases/latest/download/Monitor-Display-Switcher-Portable-1.2.0.exe)

[**Releases (Setup & Portable)**](https://github.com/sshihabb007/monitor-display-switcher/releases/latest) · [**Key Features**](#key-features) · [**Supported Monitors**](#supported-monitors) · [**CLI Reference**](#cli-reference) · [**Build from Source**](#build-from-source) · [**Developer**](#developer)

</div>

---

## Overview

**Monitor Display Switcher (`DisplayInputSwitcher.exe` v1.2.0)** is a fast, clean-room Windows desktop utility crafted by **Mehedi Hasan Shihab** for switching external monitor **HDMI**, **DisplayPort**, and **USB-C** inputs in one click or hotkey over **DDC/CI (`Dxva2.dll`)**.

Available as both a **Standalone Single-File Portable `.exe` (`~200 KB`)** and an official **Windows Setup Installer `.exe`**, the entire application and dark Setup Wizard compile natively with the inbox Windows `.NET Framework` compiler (`csc.exe`) with **zero external DLLs and zero third-party dependencies**.

---

<a id="key-features"></a>
## ✨ Key Features

| Capability | Details |
| :--- | :--- |
| **🖥️ Universal Monitor VCP Auto-Detection** | Automatically reads VESA EDID (`ManufacturerCode`, `MonitorName`, `SerialNumber`) and MCCS `vcp(...)` capability strings to resolve each monitor's exact VCP input code and values out of the box—no manual VCP lookup needed. |
| **⚡ 1-Click HDMI / DP Cards & Multi-Port Bar** | Glowing cyan/violet active input cards, 1-click `⇄` port swap, per-monitor saved profiles, and an automatic 1-click port chip bar when a monitor exposes 3+ inputs (`HDMI 1`, `HDMI 2`, `DP 1`, `USB-C`). |
| **⌨️ Global Keyboard Shortcuts** | Blind switch-back ready from anywhere in Windows (`Ctrl+Alt+1` HDMI, `Ctrl+Alt+2` DisplayPort, `Ctrl+Alt+S` Toggle)—customizable directly in Settings. |
| **☀️ Hardware Brightness, Volume & Mute** | Collapsible Quick Controls drawer with debounced live DDC/CI sliders for **Brightness (`VCP 10`)**, **Speaker Volume (`VCP 62`)**, and **Audio Mute (`VCP 8D`)**. |
| **🛡️ Normalized I2C Bus & Handle Ownership** | Serialized I2C semaphore, transient retry backoff, 1-byte low-byte normalization (`& 0xFF` on `VCP 0x60` / `0xF4`), and proper `Dxva2` index-`0` (`0x0`) physical monitor handle ownership. |
| **📦 Portable EXE + Full Setup Installer** | Choose the zero-install **Portable `.exe`** or the dark **Setup Wizard** (Desktop/Start Menu shortcuts, optional System Tray startup, and clean `Uninstall.exe` in Windows *Installed Apps*). |

---

<a id="supported-monitors"></a>
## 🔌 Supported Monitors & Hardware Requirements

| Monitor Vendor / Family | Auto-Detected VCP Scheme |
| :--- | :--- |
| **Dell, ASUS, MSI, Gigabyte, BenQ, Samsung, Acer, HP, AOC, ViewSonic** | Standard VESA MCCS `VCP 0x60` (`15` = DP 1, `16` = DP 2, `17` = HDMI 1, `18` = HDMI 2, `19` = HDMI 3, `27` = USB-C) |
| **LG UltraGear / UltraWide (Compact DDC/CI)** | `VCP 0x60` (`1` = HDMI, `3` = DisplayPort) |
| **LG Gen5 Extended OSD** | `VCP 0xF4` / `0x60` (`144` = HDMI 1, `145` = HDMI 2, `208` = DisplayPort, `209` = USB-C) |
| **Custom Vendor Firmware** | Binds automatically to reported MCCS discrete codes + 1-click manual hex `VCP` / `HDMI` / `DP` override per monitor |

> **Requirement:** Ensure **DDC/CI** is enabled in your external monitor's built-in OSD menu (enabled by default on almost all modern monitors) over a direct HDMI, DisplayPort, or USB-C DisplayPort Alt-Mode cable. Internal laptop eDP panels are automatically skipped in favor of external displays.

---

## 📦 Download & Installation

From the [**GitHub Releases**](https://github.com/sshihabb007/monitor-display-switcher/releases/latest) page:

1. **Setup Installer (`Monitor-Display-Switcher-Setup-1.2.0.exe`)**
   - Installs into `%LOCALAPPDATA%\Programs\Monitor Display Switcher` (or any custom folder), creates Desktop & Start Menu shortcuts, optionally starts minimized in the System Tray with Windows, and includes `Uninstall.exe`.
2. **Portable Edition (`Monitor-Display-Switcher-Portable-1.2.0.exe`)**
   - Single-file zero-install `.exe`—place it on your Desktop or USB drive and run directly.

---

<a id="build-from-source"></a>
## 🛠️ Building from Source

Requirements: Windows 10 / 11 (uses inbox `.NET Framework 4.x` `csc.exe`) and Python 3 (if `assets\app.ico` needs initial generation).

```powershell
git clone https://github.com/sshihabb007/monitor-display-switcher.git
cd monitor-display-switcher
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -SkipDesktopCopy
```

Generated outputs in `dist\`:
- `dist\Monitor-Display-Switcher-Portable-1.2.0.exe` — Standalone `AnyCPU` Portable `.exe`
- `dist\Monitor-Display-Switcher-Setup-1.2.0.exe` — Standalone Windows Setup Installer & Uninstaller

---

<a id="cli-reference"></a>
## ⌨️ Command-Line Reference

### Application CLI (`DisplayInputSwitcher.exe`)

```powershell
.\DisplayInputSwitcher.exe --hdmi                  # Switch target monitor to HDMI
.\DisplayInputSwitcher.exe --dp                    # Switch target monitor to DisplayPort
.\DisplayInputSwitcher.exe --toggle                # Toggle between HDMI and DisplayPort
.\DisplayInputSwitcher.exe --status --out info.txt # Query live read-only DDC/CI snapshot
.\DisplayInputSwitcher.exe --minimized             # Start silently in System Tray
```

### Setup Installer CLI (`Monitor-Display-Switcher-Setup-1.2.0.exe`)

```powershell
.\Monitor-Display-Switcher-Setup-1.2.0.exe [--silent] [--install-dir <Path>] [--no-registry] [--uninstall]
```

---

<a id="developer"></a>
## 👨‍💻 About the Developer

**Monitor Display Switcher** is designed, engineered, and maintained by **Mehedi Hasan Shihab** — a **Web & Software Developer** focused on building fast, clean, user-centric desktop tools and full-stack web applications.

| Field | Details |
| :--- | :--- |
| **Developer** | **Mehedi Hasan Shihab** *(Web & Software Developer)* |
| **GitHub** | [@sshihabb007](https://github.com/sshihabb007) |
| **Portfolio** | [https://sshihabb007.github.io/](https://sshihabb007.github.io/) |
| **LinkedIn** | [linkedin.com/in/mehedi-hasan-shihab](https://www.linkedin.com/in/mehedi-hasan-shihab) |
| **Email** | [sshihabb007@gmail.com](mailto:sshihabb007@gmail.com) |
| **Project Repository** | [github.com/sshihabb007/monitor-display-switcher](https://github.com/sshihabb007/monitor-display-switcher) |

<div align="center">

[![Portfolio](https://img.shields.io/badge/Portfolio-sshihabb007.github.io-7C5CFF?style=for-the-badge&logo=googlechrome&logoColor=white)](https://sshihabb007.github.io/)
[![GitHub](https://img.shields.io/badge/GitHub-@sshihabb007-181717?style=for-the-badge&logo=github&logoColor=white)](https://github.com/sshihabb007)
[![LinkedIn](https://img.shields.io/badge/LinkedIn-Mehedi%20Hasan%20Shihab-0A66C2?style=for-the-badge&logo=linkedin&logoColor=white)](https://www.linkedin.com/in/mehedi-hasan-shihab)
[![Email](https://img.shields.io/badge/Email-sshihabb007%40gmail.com-EA4335?style=for-the-badge&logo=gmail&logoColor=white)](mailto:sshihabb007@gmail.com)

</div>

---

## 📄 License

Released under the **[MIT License](LICENSE)** — Copyright © 2026 **Mehedi Hasan Shihab (`sshihabb007`)**.
