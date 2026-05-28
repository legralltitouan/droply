<div align="center">
  <img src="assets/main.png" width="96" alt="Droply"/>
  
  # Droply
  
  **Drag. Drop. Share.** A minimalist taskbar-docked file-sharing utility for Windows.

  ![.NET](https://img.shields.io/badge/.NET-10-512BD4?style=flat-square&logo=dotnet)
  ![Platform](https://img.shields.io/badge/Windows-11-0078D6?style=flat-square&logo=windows)
  ![License](https://img.shields.io/badge/License-MIT-ED8209?style=flat-square)
</div>

---

## ⚡ What is Droply?

Droply lives just above your Windows taskbar. Drag any file onto it, get a download link copied to your clipboard within seconds. No accounts, no friction.

It auto-picks the **best upload service** for each file and even mirrors uploads to your **other PCs** through a Discord bridge.

## 🎬 Preview

### Core workflow
| Idle | Uploading | Success |
|:---:|:---:|:---:|
| ![Idle](assets/1.png) | ![Uploading](assets/2.png) | ![Success](assets/3.png) |

### Context menu & Settings
| Right-click menu | Settings · Dark | Settings · Light |
|:---:|:---:|:---:|
| <img src="assets/4.png" width="240"/> | <img src="assets/5.png" width="240"/> | <img src="assets/6.png" width="240"/> |

### Discord notification embed
<img src="assets/7.png" width="600"/>

## ✨ Features

### Smart multi-host upload routing
Droply automatically picks the best host based on file size:

| File size | Host | Why |
|---|---|---|
| **≤ 2 GB** | gofile.io | Permanent storage, Discord embed |
| **2 – 25 GB** | storage.to | Cloudflare R2 presigned multipart (real progress bar) |
| **25 – 100 GB** | pixeldrain.com | Only host supporting up to 100 GB |

### Cross-PC sync via Discord
Paste a Discord webhook + bot token + channel ID once. Upload on PC A → toast notification appears on PC B with one-click download. Your own uploads are filtered out by machine name.

### Native taskbar integration
- Pinned at the exact pixel position of your taskbar — works with **auto-hide**, **center alignment**, **DPI scaling**, **multi-monitor**
- 30 Hz throttled position tracking to stay invisible on 144 Hz+ monitors
- Win32 P/Invoke (`user32.dll`) for shell-aware positioning

### Polished WPF UX
- Cross-fade state transitions with cubic-ease width animation
- Custom Fluent toggle switches with `TranslateTransform` glide
- Settings window with elastic `BackEase` slide-in
- Live theme + language switching (FR / EN) without restart

### Optional Discord webhook
Each upload triggers a clean embed in your channel with the host name + link.

### Auto-start & registry-aware
Toggle "Launch at startup" — Droply writes itself into `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

## 🛠 Tech Stack

- **C# 13 / .NET 10** (Windows desktop SDK)
- **WPF** with XAML storyboards & dynamic resources
- **Win32 Interop** via P/Invoke (`Shell_TrayWnd` tracking, `SetWindowPos`, registry)
- **APIs**: Gofile · Pixeldrain · storage.to · Discord (Webhook + Bot v10)

## 🚀 Quick start

1. Download `Droply.exe` from the [Releases](https://github.com/legralltitouan/Droply/releases) page
2. Run it — the icon docks above your taskbar
3. **Right-click** the icon → **Paramètres / Settings** to configure:
   - Launch at startup
   - Light / Dark theme
   - Language (FR / EN)
   - Discord webhook URL
   - (optional) Discord Bot Token + Channel ID for cross-PC sync
4. **Drag** any file onto the icon → link is in your clipboard ✅

### Setting up cross-PC sync
1. Create a bot at https://discord.com/developers/applications
2. Enable **MESSAGE CONTENT INTENT** in the Bot tab
3. Generate an OAuth2 URL with `bot` + `View Channels` + `Read Message History`
4. Invite the bot to your server
5. Copy the Bot Token and the Channel ID (right-click channel → Copy ID with Developer Mode on)
6. Paste both into Droply Settings → save

## 📦 Build from source

```bash
git clone https://github.com/legralltitouan/Droply.git
cd Droply
dotnet build -c Release
