# Dabis Content Manager

> Desktop-Tool für Content Creator – Videos hochladen, planen und mit lokaler KI optimieren.

![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white)
![Platform](https://img.shields.io/badge/Platform-Windows-0078D6?logo=windows&logoColor=white)

---

## 📋 Übersicht

Der **Dabis Content Manager** ist eine Windows-Desktop-Anwendung, die den Upload-Workflow für YouTube-Videos vereinfacht. Statt im Browser zwischen Tabs zu wechseln, erledigst du alles in einer App – vom Auswählen der Videodatei bis zum geplanten Upload mit Thumbnail und Playlist-Zuweisung.

Optional unterstützt dich ein **lokales KI-Modell** beim Erstellen von Titeln, Beschreibungen und Tags. Die KI läuft komplett auf deinem Rechner, sodass keine Daten an externe Server gesendet werden.

---

## ✨ Features

| Feature | Beschreibung |
|---------|--------------|
| **YouTube-Upload** | Videos direkt aus der App hochladen, inkl. Thumbnail |
| **Geplante Veröffentlichung** | Datum und Uhrzeit für den Release festlegen |
| **Sichtbarkeit** | Öffentlich, nicht gelistet oder privat |
| **Playlist-Zuweisung** | Video beim Upload einer Playlist hinzufügen |
| **KI-Vorschläge** | Titel, Beschreibung und Tags generieren lassen (lokal, offline) |
| **Templates** | Wiederverwendbare Beschreibungsvorlagen mit Platzhaltern |
| **Kanalprofil** | Sprache, Tonfall und Zielgruppe für bessere KI-Vorschläge hinterlegen |
| **Upload-Historie** | Übersicht aller Uploads mit Status und Direktlink |

---

## 🖼️ Screenshots

*Kommt bald...*

---

## 🚀 Installation

### Voraussetzungen

- Windows 10/11
- .NET 9 Runtime (https://dotnet.microsoft.com/download/dotnet/9.0)
- YouTube API Client-Secrets (siehe Einrichtung)

### Download

1. Lade die neueste Version aus den Releases herunter
2. Entpacke das Archiv in einen Ordner deiner Wahl
3. Starte DCM.App.exe

### Aus Quellcode bauen

git clone https://github.com/dabinuss/Dabis-Content-Manager.git
cd Dabis-Content-Manager
dotnet build -c Release

---

## ⚙️ Einrichtung

### 🔑 YouTube API einrichten

1. Gehe zur Google Cloud Console (https://console.cloud.google.com/)
2. Erstelle ein neues Projekt oder wähle ein bestehendes
3. Aktiviere die YouTube Data API v3
4. Erstelle OAuth 2.0 Anmeldedaten (Desktop-App)
5. Lade die JSON-Datei herunter
6. Benenne sie um in youtube_client_secrets.json
7. Lege sie im App-Datenordner ab: %APPDATA%\DabisContentManager\youtube_client_secrets.json

### 🤖 Lokale KI einrichten (optional)

Die KI-Funktionen benötigen ein GGUF-Modell, das lokal auf deinem Rechner läuft.

1. Lade ein kompatibles GGUF-Modell herunter (z.B. von Hugging Face)
2. Öffne in der App Einstellungen → KI / LLM
3. Wähle Modus: Lokal (GGUF)
4. Setze den Pfad zur Modelldatei
5. Speichere die Einstellungen

Hinweis: Ohne Transkript im Upload-Formular werden regelbasierte Fallback-Vorschläge verwendet. Die KI generiert nur Inhalte, wenn ein Transkript vorhanden ist – so wird Halluzinieren verhindert.

---

## 📖 Nutzung

### Erster Start

1. Konto verbinden: Tab Konten → Mit YouTube verbinden
2. Video auswählen: Tab Neuer Upload → Videodatei wählen
3. Metadaten eingeben: Titel, Beschreibung, Tags, Sichtbarkeit
4. Optional: Thumbnail hinzufügen, Playlist wählen, Veröffentlichung planen
5. Upload starten: Klick auf Upload starten

### Templates nutzen

1. Tab Templates → Neues Template erstellen
2. Platzhalter verwenden:
   - {{TITLE}} – Videotitel
   - {{TAGS}} – Tags als kommaseparierte Liste
   - {{HASHTAGS}} – Tags als Hashtags
   - {{DATE}} – Geplantes Veröffentlichungsdatum
   - {{PLAYLIST}} – Playlist-ID
   - {{VISIBILITY}} – Sichtbarkeit
   - {{YEAR}}, {{MONTH}}, {{DAY}} – Aktuelles Datum
3. Template beim Upload anwenden

### KI-Vorschläge generieren

1. Transkript ins entsprechende Feld einfügen
2. Auf Vorschlagen klicken (bei Titel, Beschreibung oder Tags)
3. Vorschlag übernehmen oder anpassen

---

## 🛠️ Technologien

- Framework: .NET 9, WPF
- YouTube API: Google.Apis.YouTube.v3
- Lokale KI: LLamaSharp mit Vulkan-Backend
- Persistenz: JSON-Dateien im AppData-Ordner

---

## 👤 Autor

**dabinuss**

- GitHub: @dabinuss (https://github.com/dabinuss)

---

## ⭐ Support

Wenn dir das Projekt gefällt, lass einen Stern da!