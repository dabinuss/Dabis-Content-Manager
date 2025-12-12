# Dabis Content Manager

> Desktop tool for content creators – upload, schedule and optimize videos with local AI.

![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white)
![Platform](https://img.shields.io/badge/Platform-Windows-0078D6?logo=windows&logoColor=white)

---

## 📋 Overview

**Dabis Content Manager** is a Windows desktop application that streamlines the upload workflow for YouTube videos. Instead of juggling multiple browser tabs, you handle everything in one app – from selecting the video file to scheduling the upload with thumbnail and playlist assignment.

Optionally, a **local AI model** helps you create titles, descriptions and tags. The AI runs entirely on your machine, so no data is sent to external servers.

---

## ✨ Features

| Feature | Description |
|--------|-------------|
| **YouTube upload** | Upload videos directly from the app, including thumbnail |
| **Scheduled publishing** | Set date and time for the release |
| **Visibility** | Public, unlisted or private |
| **Playlist assignment** | Add the video to a playlist during upload |
| **AI suggestions** | Generate titles, descriptions and tags (local, offline) |
| **Templates** | Reusable description templates with placeholders |
| **Channel profile** | Store language, tone and target audience for better AI suggestions |
| **Upload history** | Overview of all uploads with status and direct link |

---

## 🖼️ Screenshots

<img width="1369" height="1034" alt="grafik" src="https://github.com/user-attachments/assets/b54594cf-c286-4c86-a36f-ef4903a08815" />

---

## 🚀 Installation

### Requirements

- Windows 10/11  
- .NET 9 Runtime (https://dotnet.microsoft.com/download/dotnet/9.0)  
- YouTube API client secrets (see setup)

### Download

1. Download the latest version from the Releases page  
2. Extract the archive to a folder of your choice  
3. Start `DCM.App.exe`

### Build from source

    git clone https://github.com/dabinuss/Dabis-Content-Manager.git
    cd Dabis-Content-Manager
    dotnet build -c Release

---

## ⚙️ Setup

### 🔑 Set up the YouTube API

1. Go to the Google Cloud Console (https://console.cloud.google.com/)  
2. Create a new project or select an existing one  
3. Enable **YouTube Data API v3**  
4. Create **OAuth 2.0 credentials** (Desktop application)  
5. Download the JSON file  
6. Rename it to `youtube_client_secrets.json`  
7. Place it in the app data folder:  
   `%APPDATA%\DabisContentManager\youtube_client_secrets.json`

### 🤖 Set up local AI (optional)

The AI features require a GGUF model running locally on your machine.

1. Download a compatible GGUF model (e.g. from Hugging Face)  
2. In the app, open **Settings → AI / LLM**  
3. Select mode: **Local (GGUF)**  
4. Set the path to the model file  
5. Save the settings  

Note: If there is no transcript in the upload form, rule-based fallback suggestions are used. The AI only generates content when a transcript is available – this reduces hallucinations and keeps suggestions closer to the actual video content.

---

## 📖 Usage

### First start

1. Connect your account: **Accounts** tab → *Connect to YouTube*  
2. Select a video: **New Upload** tab → choose video file  
3. Enter metadata: title, description, tags, visibility  
4. Optional: add thumbnail, choose playlist, schedule publishing  
5. Start upload: click **Start Upload**

### Using templates

1. Go to the **Templates** tab → create a new template  
2. Use placeholders such as:
   - `{{TITLE}}` – video title  
   - `{{TAGS}}` – tags as comma-separated list  
   - `{{HASHTAGS}}` – tags as hashtags  
   - `{{DATE}}` – planned publication date  
   - `{{PLAYLIST}}` – playlist ID  
   - `{{VISIBILITY}}` – visibility  
   - `{{YEAR}}`, `{{MONTH}}`, `{{DAY}}` – current date  
3. Apply a template during upload to auto-fill the description

### Generating AI suggestions

1. Paste the transcript into the transcription field  
2. Click **Suggest** for title, description or tags  
3. Review, edit and/or accept the suggestion

---

## 🛠️ Tech stack

- Framework: .NET 9, WPF  
- YouTube API: `Google.Apis.YouTube.v3`  
- Local AI: LLamaSharp with Vulkan backend  
- Persistence: JSON files in the AppData folder  

---

## 👤 Author

**dabinuss**

- GitHub: [@dabinuss](https://github.com/dabinuss)

---

## ⭐ Support

If you like the project, consider leaving a star on GitHub!
