# 🚀 Droply

<div align="center">

<img src="assets/main.png" width="110" alt="Droply Logo" />

### **Instant file sharing directly from your Windows taskbar.**

Minimal UI. Massive transfers. Multi-device sync.

<p>
  <img src="https://img.shields.io/badge/platform-Windows%2010%20%2F%2011-0078D6?style=for-the-badge" />
  <img src="https://img.shields.io/badge/uploads-Up%20to%20100GB-5E5CE6?style=for-the-badge" />
  <img src="https://img.shields.io/badge/status-Active%20Development-30D158?style=for-the-badge" />
  <img src="https://img.shields.io/badge/license-MIT-black?style=for-the-badge" />
</p>

</div>

---

## ✨ What is Droply?

**Droply** is a lightweight Windows utility designed to make file sharing feel native to the operating system.

Instead of opening browsers, cloud dashboards, or bloated sync clients, Droply lives quietly above your taskbar and lets you share files instantly with a simple drag & drop.

Built for speed, minimalism, and power users.

---

# 🖥️ Preview

<div align="center">

<img src="assets/Render1.jpg" width="32%" />
<img src="assets/Render3.jpg" width="32%" />
<img src="assets/Render2.jpg" width="32%" />

</div>

---

# ⚡ Core Features

## 📂 Instant Drag & Drop Sharing

Drop any file directly onto the Droply taskbar overlay.

The upload starts immediately and the share link is automatically copied to your clipboard once completed.

No tabs.
No login walls.
No unnecessary steps.

---

## 🔄 Magic Portal — Multi-PC Sync

Turn Droply into your personal transfer bridge between computers.

Send a file from your Desktop PC and instantly receive a native Windows notification on your Laptop to download it.

Perfect for:

* Workstation ↔ Laptop workflows
* Fast local migration
* Cross-device media transfer
* Remote productivity setups

---

## 🧠 Smart Upload Routing

Droply automatically selects the best upload infrastructure based on file size.

| File Size     | Provider       | Usage                    |
| ------------- | -------------- | ------------------------ |
| `0 → 2 GB`    | **Gofile**     | Fast everyday transfers  |
| `2 → 25 GB`   | **Storage.to** | Large media & archives   |
| `25 → 100 GB` | **Pixeldrain** | Massive payload delivery |

No manual selection required.

---

## 🎨 Native Windows Experience

Droply is deeply focused on polish and integration.

### Included:

* Fluent Design inspired interface
* Acrylic / modern UI effects
* Smart taskbar docking
* Auto-hide taskbar awareness
* Native Windows toast notifications
* Live language switching
* Dark / Light mode support

---

## 🔔 Discord Integration

Optional Discord integrations are available for:

* Upload logging
* Remote sync
* Share history
* Rich embeds preview

Perfect for teams, private servers, or personal organization.

---

# 🚀 Installation

## Download

Get the latest version from the releases page:

👉 **[Download Droply](https://github.com/legralltitouan/Droply/releases)**

---

## Quick Setup

1. Launch `Droply.exe`
2. Right-click the tray/taskbar icon
3. Open **Settings**
4. Configure:

   * Theme
   * Language
   * Startup behavior
   * Discord integrations

Done.

---

# 🔮 Magic Portal Setup

## 1. Create a Discord Bot

Go to the Discord Developer Portal:

👉 [https://discord.com/developers/applications](https://discord.com/developers/applications)

Create a new application and open the **Bot** tab.

---

## 2. Enable Required Intent

Enable:

✅ `MESSAGE CONTENT INTENT`

This is required for synchronization messages.

---

## 3. Invite the Bot

In:

`OAuth2 → URL Generator`

Select:

* `bot`

Permissions:

* `View Channels`
* `Read Message History`

Then invite the bot to your private server.

---

## 4. Retrieve Channel ID

Enable Discord Developer Mode, then:

`Right Click Channel → Copy Channel ID`

---

## 5. Connect Droply

Inside Droply:

`Settings → Discord Sync`

Paste:

* Bot Token
* Channel ID

Repeat on both computers.

---

# 🧩 Workflow Example

```text
Desktop PC
   ↓
Drag file into Droply
   ↓
Automatic upload
   ↓
Discord sync event
   ↓
Laptop notification
   ↓
One-click download
```

---

# 📸 Screenshots

<div align="center">

### Upload Interface

<img src="assets/4.png" width="220" />

### Magic Portal Notification

<img src="assets/8.png" width="420" />

### Discord Embed Logging

<img src="assets/7.png" width="620" />

</div>

---

# 🛠️ Built For

Droply is especially useful for:

* Developers
* Content creators
* Editors
* IT workflows
* Fast remote transfer
* Large archive sharing
* Multi-device users

---

# 📌 Why Droply Exists

Most file-sharing tools are:

* Too slow
* Too bloated
* Browser dependent
* Filled with ads
* Limited by file size

Droply focuses on one thing:

> **Fast, seamless file transfers directly from Windows.**

---

# 🧱 Project Philosophy

* Minimal friction
* Native feeling UX
* Zero clutter
* High-speed workflows
* Useful automation
* Lightweight footprint

---

# 🤝 Contributing

Contributions are welcome.

If you want to:

* Improve performance
* Add integrations
* Refactor UI
* Report bugs
* Suggest features

Feel free to open:

* Issues
* Pull Requests
* Discussions

---

# 📜 License

MIT License

Copyright © 2026 legralltitouan

---

<div align="center">

### ⭐ If you like Droply, consider starring the repository.

Built with passion for modern Windows workflows.

</div>
