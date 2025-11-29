# Dabis Content Manager

## Überblick
Dabis Content Manager ist eine geplante Windows-11-Desktop-Anwendung, mit der Creator vorhandene Video-Dateien schnell für Uploads auf YouTube und später weitere Plattformen aufbereiten können. Die App soll Titel- und Beschreibungs-Templates, Plattform-spezifische Voreinstellungen sowie den eigentlichen Upload automatisieren, sodass der Nutzer im Idealfall nur Datei, Plattformen und finale Texte auswählt und anschließend auf „Upload“ klickt.

## Fokus und Abgrenzung
- **Fokus:** Einfache UI ohne Overload, YouTube zuerst, direkte Erweiterbarkeit für weitere Plattformen, maximale Automatisierung, Template-gestützte Texte, persistente Logins/Tokens, Planung von Veröffentlichungen und Standard-Settings.
- **Nicht-Ziele:** Kein vollwertiger Video-Editor, keine Cloud-Web-App, kein umfangreiches Plugin- oder Multiuser-System.

## Zielplattform und Technik
- **Betriebssystem:** Windows 11
- **Technologie-Vorschlag:** C# / .NET 8 mit WPF oder WinUI 3
- **Video-Verarbeitung (optional):** FFmpeg über C#-Wrapper (z. B. FFMpegCore)
- **LLM-Anbindung (optional):** Titel-/Textvorschläge über ein gekapseltes Interface
- **Datenhaltung:** JSON-Dateien im User-Verzeichnis (settings.json, accounts.json, templates/*.json, history.json)

## Kern-Features
1. **Video-Quelle & Upload-Projekt**: Datei wählen oder per Drag & Drop; Projektname automatisch; Plattformen und Metadaten hinterlegen.
2. **Plattform-Login & Kontenverwaltung**: OAuth pro Plattform, Token-Speicherung, UI für Verbinden/Neu verbinden/Logout.
3. **Templates**: Pro Plattform definierbare Body-Texte mit Platzhaltern (z. B. {{TITLE}}, {{GAME}}, {{DATE}}, {{CHANNEL_NAME}}, {{HASHTAGS}}) inkl. Default-Settings wie Visibility, Playlist, Tags.
4. **Titelvorschläge**: Optionaler Freitext-Kontext, LLM-Aufruf, Auswahl aus mehreren Vorschlägen oder manuelles Überschreiben.
5. **Plattform-Metadaten (YouTube MVP)**: Titel, Beschreibung, Tags, Kategorie, Sichtbarkeit (öffentlich/nicht gelistet/privat/geplant), Geplante Veröffentlichung, Playlist-Auswahl, optional Thumbnail.
6. **Upload durchführen**: Validierung, Fortschrittsanzeige, Fehlerfeedback, Speichern in Upload-Historie mit Datum/Plattform/Titel/Video-ID.
7. **Upload-Historie**: Liste mit Status, Details pro Eintrag, Filteroptionen.
8. **Einstellungen**: Default-Plattform, zuletzt verwendeter Ordner, Standard-Template pro Plattform, Sprache/Locale, LLM-API-Keys.

## Architektur
- **Schichtenmodell:**
  - Presentation Layer (Views/ViewModels, keine direkten API-Zugriffe)
  - Application Layer (Use-Cases/Services: UploadService, TemplateService, AccountService, TitleSuggestionService)
  - Domain Layer (Modelle & Interfaces wie UploadProject, PlatformTarget, PlatformType, Template, Account)
  - Infrastructure Layer (z. B. YouTubeClient als IPlatformClient, JSON-Storage, LLM-Anbindung)
- **Plattform-Abstraktion:** `IPlatformClient` mit Methoden u. a. `IsAuthenticated()`, `AuthenticateAsync()`, `GetPlaylists()`, `UploadAsync()`, Eigenschaft `Platform` (PlatformType).
- **Datenmodelle (Beispiele):** UploadProject (VideoFilePath, TitleSuggestionContext, Targets), PlatformTarget (PlatformType, SelectedTemplateId, ResolvedTitle/Description, PlatformSpecificSettings), Template (Id, Name, PlatformType, BodyText, DefaultSettings).

## Roadmap (Phasen)
1. Basis & Grundgerüst: Solution-Struktur, Modelle/Interfaces, JSON-Config, Hauptfenster mit Tabs (Neuer Upload, Konten, Templates, Historie).
2. YouTube als erste Plattform: YouTubeClient (Auth, Tokens, UploadAsync, Playlists), UI für Kontenstatus.
3. Upload-Flow (YouTube-only, generisch angelegt): UI-Wizard, Template-Anwendung, UploadService orchestriert.
4. Templates & Platzhalter-Engine: TemplateService für CRUD + Rendern, UI zum Verwalten.
5. Titelvorschläge: Interface + Implementation (z. B. OpenAI), UI-Button und Vorschlagsliste.
6. Upload-Historie: UploadHistoryService, UI-Tabelle mit Filtern.
7. Vorbereitung für weitere Plattformen: generische PlatformType-Liste, Beispiel-Mock-Client.
8. Erweiterung auf weitere Plattformen: TikTok/Instagram-Clients, UI-Felder erweitern, zusätzliche Templates.

## Qualitätsanforderungen
Einfache Bedienung, schnelle Performance (kein Electron), stabile Uploads mit klarem Fehlermeldungen, lokale Token-Speicherung, leichte Erweiterbarkeit über neue `IPlatformClient`-Implementierungen und Templates.
