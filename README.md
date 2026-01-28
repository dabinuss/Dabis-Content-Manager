# Dabis Content Manager

> Desktop tool for content creators – upload, schedule and optimize videos with local AI.

![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white)
![Platform](https://img.shields.io/badge/Platform-Windows-0078D6?logo=windows&logoColor=white)
![License](https://img.shields.io/badge/License-MIT-green)

---

## 📋 Overview

**Dabis Content Manager** is a Windows desktop application that streamlines the upload workflow for YouTube videos. Instead of juggling multiple browser tabs, you handle everything in one app – from selecting the video file to scheduling the upload with thumbnail and playlist assignment.

The app runs in **portable mode** by default – all data stays in the application folder, so you can run it from a USB drive or any location without installation.

Optionally, a **local AI model** helps you create titles, descriptions, tags and even chapter timestamps. The AI runs entirely on your machine, so no data is sent to external servers.

---

## ✨ Features

| Feature | Description |
|--------|-------------|
| **Portable mode** | Run from any folder – no installation required |
| **YouTube upload** | Upload videos directly from the app, including thumbnail |
| **Scheduled publishing** | Set date and time for the release with visual picker |
| **Chapter generation** | Automatic timestamps/chapters from transcription |
| **Transcription** | Local speech-to-text with Whisper (offline) |
| **AI suggestions** | Generate titles, descriptions and tags (local, offline) |
| **Presets** | Save and reuse upload configurations |
| **Templates** | Reusable description templates with placeholders |
| **Multi-upload** | Queue multiple videos with fast-fill workflow |
| **Channel profile** | Store language, tone and target audience for better AI suggestions |
| **Upload history** | Overview of all uploads with status and direct link |
| **Encrypted credentials** | OAuth tokens encrypted with Windows DPAPI |

---

## 🖼️ Screenshots

<img width="1369" height="1034" alt="grafik" src="https://github.com/user-attachments/assets/b54594cf-c286-4c86-a36f-ef4903a08815" />

---

## 🚀 Installation

### Requirements

- Windows 10/11
- .NET 9 Runtime ([download](https://dotnet.microsoft.com/download/dotnet/9.0))

### Download

1. Download the latest release from the [Releases page](https://github.com/dabinuss/Dabis-Content-Manager/releases)
2. Extract the archive to a folder of your choice
3. Start `DCM.App.exe`

The app runs in **portable mode** – all settings and data are stored in the application folder.

### Build from source

```bash
git clone https://github.com/dabinuss/Dabis-Content-Manager.git
cd Dabis-Content-Manager
dotnet build -c Release
```

---

## ⚙️ Setup

### 🔑 YouTube API (included)

The release comes with YouTube API credentials included – no setup required. Just connect your account in the app.

<details>
<summary>Use your own API credentials (optional)</summary>

1. Go to the [Google Cloud Console](https://console.cloud.google.com/)
2. Create a new project or select an existing one
3. Enable **YouTube Data API v3**
4. Create **OAuth 2.0 credentials** (Desktop application)
5. Download the JSON file
6. Rename it to `youtube_client_secrets.json`
7. Place it in the `DabisContentManager` folder next to the app

</details>

### 🎙️ Set up transcription (optional)

The app can transcribe your videos locally using Whisper.

1. In the app, go to **Settings → General**
2. Select a Whisper model size (small recommended for most users)
3. The model downloads automatically on first use

### 🤖 Set up local AI (optional)

The AI features require a GGUF model running locally on your machine.

1. Download a compatible GGUF model (e.g. from [Hugging Face](https://huggingface.co/models?search=gguf))
2. In the app, open **Settings → AI / LLM**
3. Select mode: **Local (GGUF)**
4. Set the path to the model file
5. Save the settings

> **Note:** AI suggestions work best with a transcript. Without one, rule-based fallback suggestions are used.

---

## 📖 Usage

### First start

1. Connect your account: **Accounts** tab → *Connect to YouTube*
2. Select a video: **New Upload** tab → choose video file
3. Enter metadata: title, description, tags, visibility
4. Optional: add thumbnail, choose playlist, schedule publishing
5. Start upload: click **Start Upload**

### Using presets

1. Configure your upload settings (visibility, playlist, tags, etc.)
2. Save as preset for quick reuse
3. Set a default preset to auto-apply on new uploads

### Using templates

1. Go to the **Templates** tab → create a new template
2. Use placeholders such as:
   - `{{TITLE}}` – video title
   - `{{TAGS}}` – tags as comma-separated list
   - `{{HASHTAGS}}` – tags as hashtags
   - `{{DATE}}` – planned publication date
   - `{{PLAYLIST}}` – playlist name
   - `{{VISIBILITY}}` – visibility
   - `{{YEAR}}`, `{{MONTH}}`, `{{DAY}}` – current date
3. Apply a template during upload to auto-fill the description

### Transcription & AI suggestions

1. Add a video and click **Transcribe** to generate a transcript locally
2. Click **Suggest** for title, description, tags or chapters
3. Review, edit and accept the suggestions

> **Tip:** Use the chapter generation feature to automatically create timestamps from your transcript.

---

## 🛠️ Tech stack

- **Framework:** .NET 9, WPF
- **YouTube API:** Google.Apis.YouTube.v3
- **Transcription:** Whisper.net (local, offline)
- **Local AI:** LLamaSharp with Vulkan backend
- **Persistence:** JSON files in the application folder (portable)  

---

## 👤 Author

**dabinuss** – [@dabinuss](https://github.com/dabinuss)

---

## ⭐ Support

If you find this project useful, consider leaving a star on GitHub!

---

## 📄 License

This project is licensed under the MIT License.
