#   <img src="assets/main.png" width="40" style="vertical-align: middle; margin-right: 10px;" alt="Droply Logo" /> Droply

---

**Droply** is a lightweight, minimalist Windows utility designed for instant file sharing. Drag, drop, and share—it's that simple. Powered by advanced WPF capabilities and native Win32 tracking, it docks perfectly onto your Windows taskbar with fluid animations. Break past standard limits with a dynamic algorithm capable of handling massive payloads up to **100GB**.

---

## 📸 Preview

### Core Workflow
| Idle State | Uploading | Success |
| :---: | :---: | :---: |
| ![Idle State](assets/1.png) | ![Uploading](assets/2.png) | ![Success](assets/3.png) |

### Context Menu & Settings Panel
| Context Menu | Settings (Dark Mode) | Settings (Light Mode) |
| :---: | :---: | :---: |
| <img src="assets/4.png" width="250" alt="Context Menu" /> | <img src="assets/5.png" width="250" alt="Settings Dark" /> | <img src="assets/6.png" width="250" alt="Settings Light" /> |

---

## ✨ Features & Optimizations

* **Drag & Drop Simplicity**: Just drag any file onto the app icon docked right above your Windows taskbar.
* **Intelligent File Routing (Up to 100GB)**: The application dynamically calculates your file size before transmission and automatically selects the most optimized API to ensure speed and stability, bypassing traditional 2GB limits entirely.
* **Instant Sharing**: Automatically uploads files and copies the secure download link straight to your clipboard.
* **Smart Frame Throttling (High CPU Optimization)**: Taskbar position recalculations are strictly restricted to 30Hz (33ms intervals). This prevents high-refresh-rate monitors (144Hz to 360Hz+) from causing unnecessary CPU spikes during render updates.
* **Dynamic Cross-Fade Transitions**: Swapping between window states (*Idle*, *Uploading*, *Success*, *Error*) uses automated opacity cross-fade sequencing coupled with a custom `CubicEase` width expansion curve.
* **Organic Window Lifecycle**: The Settings panel opens with a modern slide-in effect complete with a subtle elastic bounce (`BackEase`). Closing operations are intercepted to execute an elegant slide-down fade-out sequence prior to window destruction.
* **Re-engineered Fluent ToggleSwitches**: Checkboxes and toggle elements glide linearly using `TranslateTransform` structures combined with progressive color-fade interpolation instead of rigid state jumps.
* **Progressive Hover Layers**: Interactive elements and control buttons utilize isolated `HoverLayer` architectures that gradually transition opacity on cursor enter/leave events.
* **Discord Webhook Integration**: Link an optional Discord webhook to instantly log your uploads with clean, automatic embed cards inside your designated server.
* **Flexible Startup Control**: Toggle the "Launch at startup" parameter inside the settings UI to seamlessly update the Windows CurrentVersion Registry keys.
* **Strict Null-Safety Architecture**: Fully optimized for `.NET 10` with explicit `<Nullable>enable</Nullable>` safety compliancy, guaranteeing zero compile-time warnings and maximizing execution robustness.

---

## 🔀 Smart Routing Algorithm

Droply uses a multi-tiered backend approach to guarantee your file is uploaded using the best possible service based on its exact byte size:

| File Size | Service Used | Technology / Method |
| :--- | :--- | :--- |
| **0 MB – 2 GB** | Gofile.io | Standard Multipart POST |
| **2 GB – 25 GB** | Storage.to | Cloudflare R2 Presigned Multipart |
| **25 GB – 100 GB** | Pixeldrain.com | Direct binary stream PUT |

---

## 🚀 How to Use & Configure

1. **Launch** the application.
2. **Right-click** the main Droply box to open the **Paramètres** (Settings) context menu.
3. **Configure your preferences**:
   * Check **Lancer au démarrage** to register the application inside the Windows Startup routine.
   * Toggle **Mode Clair** to hot-swap interface layouts and runtime styles instantly without graphical hitches.
   * Paste your Discord webhook link into the **Discord Webhook** text box for secure transaction logging.
4. **Drag & Drop** any file onto the **Droply** launcher to transmit it.
5. Once complete, the URL is written to your clipboard and localized animations report the operation outcome!

### Discord Integration Preview
<img src="assets/7.png" width="600" alt="Discord Webhook Notification Preview" />

---

## 🛠 Tech Stack

* **Language**: C# 13 / .NET 10 Windows SDK
* **Framework**: WPF (Windows Presentation Foundation) with advanced XAML Storyboards
* **Native Subsystem**: Win32 Interop (`user32.dll` via P/Invoke for system tray monitoring & layout pinning)
* **API Framework**: Dynamic Payload Routing (Gofile.io, Storage.to, Pixeldrain.com), Multipart Stream Uploads & Discord Webhook Channels
* **Design Philosophy**: Microsoft Fluent Design Guidelines (Asynchronous state loops, eased transitions)

---

## 📦 Installation

1. Download the latest pre-compiled `Droply.exe` package from the [Releases page](https://github.com/legralltitouan/Droply/releases).
2. Place the standalone executable in any preferred system directory.
3. Execute `Droply.exe` to dock the utility.
4. *(Optional)* Use the internal Settings framework to automate system boot launch properties.

---

## 🤝 Contributing

Contributions are welcome! If you have optimization proposals or encounter bug trends:

1. Open a tracking [Issue](https://github.com/legralltitouan/Droply/issues).
2. Fork this software repository.
3. Initialize an isolated branch for your proposed feature modification.
4. Submit a formalized Pull Request for review.

---

## 📝 License

This project is licensed under the terms of the **MIT License**. Check out the `LICENSE` documentation for detailed clauses.

**Copyright (c) 2026 legralltitouan**
*Note: Non-commercial use only. Any modification or distribution of this software requires written permission from legralltitouan.*
