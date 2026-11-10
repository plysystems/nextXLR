# NEXT : XLR — Umfassende Software- & Systemanalyse
**Datum:** 10. November 2026  
**Analysierte Version:** v0.1.5-alpha (Commit: `ae703bf`)  
**Autor:** Antigravity (System- & Softwarearchitektur)

---

## 1. Executive Summary & Projektüberblick

**NEXT : XLR** (interner Codename *Mixforge*) ist ein modernes, quelloffenes Open-Source-Ökosystem für Windows 10 und Windows 11 (x64), das als vollwertiger, zukunftssicherer Ersatz für die eingestellte offizielle TC-Helicon GoXLR-Applikation entwickelt wurde. 

Das Projekt löst fundamentale Schwachstellen der Legacy-Software:
* **Entkopplung von Audio-Engine und Benutzeroberfläche**: Die Audio-Verarbeitung und Hardware-Steuerung läuft in einem schlanken, speichersicheren Rust-Daemon (`mixforged`), während das Frontend in moderner **WinUI 3 (Windows App SDK / .NET 8)** mit Fluent Design (Mica, per-monitor DPI, Dark Mode) realisiert ist.
* **Eliminierung von Audioklicks und Latenzen**: Keine Garbage-Collection-Pausen auf dem Audio-Echtzeitpfad durch Auslagerung in Rust (`cpal`, `rubato`, lock-free Atomics).
* **Native Windows-App-Audiointegration**: Echtes Per-App-Routing (Windows Lautstärkemixer) via nativer COM-Schnittstellen (`IAudioPolicyConfigFactory` und `IAudioSessionManager2`) ohne die Notwendigkeit instabiler Drittanbieter-Treiber.
* **Moderne Schnittstellen**: Vollständige Unterstützung für externe Kontrollgeräte (Elgato Stream Deck REST- & WebSocket-APIs auf Port 48500).

---

## 2. System- und Prozessarchitektur

Das Gesamtsystem basiert auf einer sauberen **Multi-Process-Architektur (Clean Architecture)**:

```
 ┌─────────────────────────────────────────────────────────────┐
 │               NextXLR.exe (WinUI 3 / C# .NET 8)             │
 │  Views (XAML) ── ViewModels (CommunityToolkit.Mvvm)        │
 │                      ▲ DispatcherQueue                      │
 │  PipeTransport: \\.\pipe\mixforge (JSON-Lines Protocol)     │
 └──────────────────────────────┬──────────────────────────────┘
                                │ Commands ↓  |  State/Meters ↑
 ┌──────────────────────────────┴──────────────────────────────┐
 │               mixforged.exe (Rust Core Daemon)              │
 │                                                             │
 │  Named Pipe Server ──┐                                      │
 │  WebSocket / REST  ──┼──► Hub (State Owner) ──┬─► HW Thread │
 │  Stream Deck API   ──┘                        └─► Audio RT  │
 │                                                             │
 │  Hardware Layer: goxlr_audioapi_x64.dll (Thesycon Driver)   │
 │  Audio Layer: WASAPI / Realtime DSP Graph                   │
 └─────────────────────────────────────────────────────────────┘
```

### 2.1 Prozess 1: `NextXLR.exe` (Frontend)
* **Technologie**: C# / .NET 8.0, Windows App SDK 1.5/1.6, WinUI 3, CommunityToolkit.Mvvm.
* **Rolle**: Reine Projektion (Presentation Layer). Das UI hält keine eigenen Geschäftsdaten dauerhaft im Cache, sondern empfängt Snapshots vom Hub des Daemons und sendet Benutzerabsichten (*Intents*).
* **Mica-Material & Fluent UI**: Dynamische Anpassung an Windows 11 Designs, 60-FPS Canvas-Pegelanzeigen via Win2D/DispatcherQueue-Timer.
* **Crash-Handling**: Eigener `CrashHandler` mit automatischer Diagnose-Berichterstellung und Stacktrace-Formatierung im AppData-Verzeichnis.

### 2.2 Prozess 2: `mixforged.exe` (Core Daemon)
* **Technologie**: Rust (Edition 2021), Tokio Async Runtime, Axum HTTP/WebSocket-Framework.
* **Rolle**: Einziger Besitzer (*Single Source of Truth*) des gesamten Mixer-Status.
* **Kommunikation**:
  * Lokale WinUI-App: Named Pipe `\\.\pipe\mixforge` (Windows ACL-gesichert, extrem latenzarm).
  * Externe Steuerung: HTTP REST-API & WebSocket auf `127.0.0.1:48500` für Stream Deck, OBS, Mobile Web-Panels.
* **Hardware-Thread**: Dedizierter Thread zur synchronen Abfrage der GoXLR USB-Hardware-Endpunkte bei Tastendruck oder Fader-Bewegung.

---

## 3. Hardware-Kommunikation & Treiberschicht

Das GoXLR und GoXLR Mini sind **keine Standard-USB-HID-Geräte**. Die Kommunikation erfolgt über Vendor-spezifische USB-Endpunkte:

| Eigenschaft | GoXLR (Full) | GoXLR Mini |
|---|---|---|
| **USB Vendor ID (VID)** | `0x1220` (TC-Helicon) | `0x1220` (TC-Helicon) |
| **USB Product ID (PID)** | `0x8FE0` | `0x8FE4` |
| **Fader** | 4 motorisierte Fader | 4 manuelle Fader |
| **Hardware-DSP** | Volles FX-Modul & Sampler | Reines Mischpult & Mic-Gate/EQ/Comp |
| **Bildschirme** | 4 Scribble-Strip OLEDs (128x64) | Keine Scribble-Strips |

### Treiberschnittstelle (`goxlr_audioapi_x64.dll`)
* Die Kommunikation stützt sich auf die DLL des offiziellen Thesycon-Treibers (`CLSID: {024D0372-641F-4B7B-8140-F4DFE458C982}`).
* **Vendor Requests**: Befehle werden via `TUSBAUDIO_ClassVendorRequestOut` und `TUSBAUDIO_ClassVendorRequestIn` an das Gerät gesendet.
* **Ereignisgesteuert**: Über `TUSBAUDIO_RegisterDeviceNotification` wird ein Win32-Event registriert. Das System pollt die Hardware nicht kontinuierlich, sondern wacht nur auf, wenn der Thesycon-Treiber einen Hardware-Zustandswechsel signalisiert.

---

## 4. Detaillierte Modulanalyse

### 4.1 Mixer & Fader-Steuerung
* **Kanal-Matrix**: 8 logische Eingangskanäle (*Mic, Chat, Music, Game, Console, Line In, System, Samples*).
* **Fader-Zuweisung**: Frei konfigurierbare Belegung der physischen Fader A–D über Mixer-Seiten (Page 1 & Page 2).
* **Motorfader-Synchronisation**: Beim Seitenwechsel oder Laden eines Profils sendet der Hub Motor-Positionierbefehle (`SetFaderPosition`) an die Hardware. Die Software verhindert Endlosschleifen durch Auswertung des Hardware-Touch-Status (`FaderTouch { fader, touched }`).
* **Dual-Mix (Submix)**: Unterstützung für unabhängige Lautstärken zwischen *Broadcast Stream Mix* (was der Stream hört) und *Headphones Mix* (was der Streamer auf den Kopfhörern hört).

### 4.2 Mikrofon-Kette & DSP
* **Hardware-Mic-Setup**:
  * Mikrofonauswahl: Dynamic, XLR Condenser (+48V Phantomspeisung aktivierbar), 3.5mm Headset.
  * Gain-Staging mit Live-dB-Anzeige.
* **DSP-Module**:
  * Noise Gate (Threshold, Attenuation, Attack, Release).
  * Equalizer: 4-Band parametrischer EQ (Bass, Low-Mid, High-Mid, Treble) mit Feineinstellung der Eckfrequenzen.
  * Kompressor: Threshold, Ratio, Attack, Release, Make-up Gain mit animierter Gain-Reduction-Anzeige.
  * De-Esser: Gezielte Dämpfung von Zischlauten.
* **VST3-Plugin-Integration (`VstPluginService`)**:
  * Ermöglicht das Laden externer VST3-Audio-Plugins in NextXLR.
  * Isolierte Host-Umgebung zur Vermeidung von Host-Crashes.

### 4.3 Routing-Matrix
* **Tabellen-Routing**: Flexible Matrix-Verbindung zwischen den 8 Eingängen und 5 physischen Ausgängen (*Headphones, Broadcast Stream Mix, Line Out, Chat Mic, Sampler*).
* **Schutz-Logik**: Automatische Deaktivierung zirkulärer Rückkopplungsschleifen (z. B. Sampler darf sich nicht selbst aufnehmen; Chat Mic blockiert bestimmte System-Schleifen).

### 4.4 Windows Lautstärkemixer (App-Audio-Routing)
* **Direktes COM-Interop in .NET 8**: Vollständiger Verzicht auf anfällige Drittanbieter-Bibliotheken. Direkte Ansprache der Windows Core Audio COM-Vtables:
  * `IMMDevice` & `IAudioSessionManager2Native` (`77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F`)
  * `IAudioSessionEnumeratorNative` (`E2F5BB11-0570-40CA-ACDD-3AA01277DEE8`)
  * `IAudioSessionControl2Native` (`bfb7ff88-7239-4fc9-8fa2-07c950be9c6d`)
  * `ISimpleAudioVolumeNative` & `IAudioMeterInformationNative`
* **Persistiertes Policy-Routing**:
  * Nutzung von `IAudioPolicyConfigFactory` (`ab3d4648-e242-459f-b02f-541c70306324`) mit vollqualifizierten Software Device (SWD) Pfaden:  
    `\\?\SWD#MMDEVAPI#{deviceId}#{flowGuid}`.
  * Zuweisung aller drei Windows-Rollen (`ERole.eMultimedia`, `eConsole` und `eCommunications`), wodurch auch Sprach-Apps (Discord, Teams, Zoom) sofort ohne Neustart auf den gewünschten GoXLR-Kanal umspringen.
  * Direkte Spiegelung in die Windows-Registry (`HKCU\Software\Microsoft\Multimedia\Audio\DefaultEndpoint`).
  * Persistente Profilspeicherung in `%LOCALAPPDATA%\NextXLR\app_routing_rules.json`.

### 4.5 Lichtsteuerung & Scribble-Strips
* **Vollständige RGB-Freiheit**: Unabhängige Steuerung aller LEDs (Fader-Hintergrund, Lautstärke-Skalen, Mute-Buttons, FX-Buttons, Logo).
* **Farbsystem**: Hex-Codes, Gradienten und Animations-Presets (Meter-Light, Static, Breath).
* **Scribble Strips (GoXLR Full)**:
  * Übertragung von 128x64 Monochrom-Bitmaps an die kleinen OLED-Displays über den Fadern.
  * Integrierter `IconCatalog` mit über 100 vorkonfigurierten Icons (Gaming, Musik, Chat, Browser, Mikrofone, Emoticons).

### 4.6 Effekte & Sampler
* **Voice FX**: Megaphone, Robot, Pitch Shift, Formant Shift, HardTune / Reverb & Delay Steuerung.
* **Sampler**: 4 Bänke (A, B, C) mit je 4 Sample-Pads. Unterstützung für Fade-in, Fade-out, Loop-Modi und direkte Mikrofon-/Matrix-Aufnahme.

### 4.7 Externe Integrationen
* **Stream Deck REST & WebSocket API**:
  * Dedizierte Endpunkte auf `http://127.0.0.1:48500`:
    * `/api/status`: Kompletter Mixer-Snapshot.
    * `/api/fader`: Absolute und relative Lautstärkeregelung (`delta=+0.05`).
    * `/api/mute`: Stummschaltung & Toggle.
    * `/api/route`: Matrix-Routen dynamisch verbinden/trennen.
    * `/api/bleep` & `/api/mic-mute`: Sofortige Tasten-Aktionen.
    * `/api/profile`: Setups wechseln.
  * Integrierter CORS-Support für Web- und Elgato-Plugins.
* **Bluetooth & Wireless Headset Router (`BluetoothAudioRouter`)**:
  * Erkennt drahtlose Kopfhörer (Bluetooth, 2.4 GHz USB Dongles wie Corsair HS80).
  * Dupliziert den Headphones-Mix in Echtzeit auf das Funk-Headset mit automatischem Latenzausgleich.

---

## 5. Build-, Release- und Deployment-Pipeline

1. **Atomic Single-Commit Policy**:
   * Versionierung, Build-Nummern und Git-Commit-Hashes werden beim Build über den `AssemblyInformationalVersionAttribute` atomar verknüpft. Es existieren keine nachgelagerten "Chore"-Commits, wodurch Versionsmismatch-Schleifen im In-App-Updater verhindert werden.
2. **Code Signing**:
   * Alle Executables (`NextXLR.exe`, `mixforged.exe`, `mixforge.exe`) und der Installer werden mit einem Authenticode-Zertifikat digital signiert (`CN=plysystems`).
3. **In-App Auto-Updater**:
   * Prüft GitHub Releases API direkt auf semantische Versionssprünge (`v0.1.x-alpha`).
   * Download des Setups im Hintergrund mit Live-Prozentanzeige in der InfoBar.
   * Silent Installation und automatischer Neustart der App und Engine.

---

## 6. Stärken & Optimierungspotenziale

### Stärken
1. **Enorme Performance**: Rust als Audio-Kern garantiert < 3 ms Pufferlatenz bei 48 kHz.
2. **Keine Abhängigkeit von TC-Helicon Cloud**: 100 % lokal, datensparsam und ohne Login-Zwang.
3. **Plattformtreue Optik**: Modernstes WinUI 3 Design fügt sich nahtlos in Windows 11 ein.
4. **Resilienz**: Stürzt das UI ab, läuft der Audio-Daemon `mixforged` ununterbrochen weiter.

### Optimierungspotenziale
1. **Virtuelle Audiotreiber**: Derzeit ist das System auf die physischen WDM-Endpunkte des Thesycon-Treibers angewiesen. Ein eigener, signierter virtueller Audiotreiber würde zusätzliche virtuelle Kanäle ermöglichen.
2. **Linux & macOS Kompatibilität**: Der Rust-Daemon ist modular aufgebaut; mit PipeWire- oder CoreAudio-Backends wäre eine Portierung denkbar.
3. **Web-Interface**: Das React-basierte Web-Panel für Mobilgeräte ist vorbereitet, kann aber noch um touch-optimierte Fader erweitert werden.

---

## 7. Fazit

**NEXT : XLR** stellt in der Version `0.1.5-alpha` einen hochentwickelten, stabilen und feature-reichen Meilenstein dar. Die Trennung von C# WinUI 3 und Rust-Daemon hat sich als architektonische Meisterleistung erwiesen, die sowohl audiophile Stabilität als auch höchste UI-Flexibilität garantiert. Das App-Audio-Routing und die Stream-Deck-Integration machen die Software zu einem überlegenen Ersatz für die veraltete Originalanwendung.