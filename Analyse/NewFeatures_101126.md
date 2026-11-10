# NEXT : XLR – Feature-Roadmap & Erweiterungsvorschläge (15 Features) 🚀
### Strategische Innovationsvorschläge für Audio, Hardware, Automatisierung & Integration
**Dokumenten-ID:** `NF-101126`  
**Datum:** 10. November 2026  
**Zielsystem:** NEXT : XLR (WinUI 3 + Rust `mixforged` Audio-Engine)

---

## 📋 Übersicht der vorgeschlagenen Features

| # | Feature-Name | Kategorie | Priorität | Kern-Nutzen |
|---|---|---|---|---|
| 01 | **KI-Rauschunterdrückung (DeepFilterNet / RNNoise)** | Audio / DSP | **Hoch** | Tastaturgeklapper & Lüfterlärm ohne Stimmverlust eliminieren |
| 02 | **Intelligentes Auto-Ducking (Sidechain)** | Audio / DSP | **Hoch** | Automatische Musikabsenkung bei Sprache |
| 03 | **Echtzeit-Spektrumanalysator & RTA-EQ-Visualizer** | UI/UX & DSP | **Hoch** | Sichtbare Frequenzen für intuitives Einstellen des Equalizers |
| 04 | **Automatischer Profilwechsel (App-Watcher)** | Automation | **Hoch** | Spiel-/App-spezifische Presets schalten vollautomatisch um |
| 05 | **Native OBS Studio WebSocket v5 Integration** | Integration | **Hoch** | Zwei-Wege-Sync von Mute-Zuständen, Szenen und Stream-Status |
| 06 | **Sampler Waveform-Editor & Drag-and-Drop** | UI/UX & Sampler | **Mittel** | Sound-Schnipsel direkt in der App zuschneiden und normalisieren |
| 07 | **Animierte Scribble-Strip GIFs & Live-Widgets** | Hardware & UI | **Mittel** | Pixel-GIFs, Spotify-Titel & Discord-Avatare auf den OLEDs |
| 08 | **Touch-optimierte Web-Fernbedienung (PWA)** | Remote & UI | **Mittel** | Smartphone/Tablet als kabelloses Zusatz-Bedienfeld nutzen |
| 09 | **Echtzeit-KI-Stimmenwandler (DirectML / RVC)** | Audio / DSP | **Mittel** | Verwandlung in beliebige Charakter-Stimmen mit niedriger Latenz |
| 10 | **Visueller Kompressions- & Gain-Reduction-Graph** | UI/UX & DSP | **Mittel** | Scrollender Live-Pegelverlauf für perfekte Dynamik-Justierung |
| 11 | **Gehörschutz & Broadcast-True-Peak-Limiter (LUFS)**| Audio & Safety | **Mittel** | Schutz vor Ohrenschmerzen durch Trolle + normgerechter Stream |
| 12 | **Virtueller Multikanal-Audio-Treiber** | Windows Engine | **Niedrig** | Zusätzliche virtuelle Kanäle unabhängig von physischer Hardware |
| 13 | **Cloud- & Community-Profil-Hub** | Cloud & Social | **Niedrig** | One-Click-Download & Upload von Mikrofon- und Farb-Presets |
| 14 | **Twitch Chat- & Discord-Event-Trigger** | Automation | **Niedrig** | Kanalpunkte & Bits triggern Samples und Spracheffekte |
| 15 | **MIDI Learn & Controller-Erweiterung** | Hardware & I/O | **Niedrig** | Launchpads, Faderports & Fußschalter nativ einbinden |

---

## 🛠️ Detaillierte Ausarbeitung der 15 Features

---

### Feature 01: KI-basierte Rauschunterdrückung (DeepFilterNet / RNNoise)
* **Kategorie:** Audio / DSP
* **Priorität:** **Hoch** (Sehr starker Marktvorteil gegenüber Original GoXLR-App)
* **Kurzbeschreibung:** Einbindung eines modernen Neuronalen Netzes zur Entfernung von Hintergrundgeräuschen in Echtzeit (mechanische Schalter, PC-Lüfter, Straßengeräusche, Mausklicks).
* **Benutzer-Nutzen:**
  * Das klassische analoge Noise Gate schneidet oft leise Endungen von Worten ab ("Abhacken") oder öffnet sich versehentlich bei lautem Tippen.
  * Das KI-Filter unterscheidet zwischen menschlicher Stimme und Störgeräuschen: Die Stimme bleibt vollkommen natürlich, während Tastaturklappern selbst beim Sprechen unhörbar wird.
* **Technische Umsetzungsskizze:**
  * Integration des Open-Source-Modells `DeepFilterNet` oder `RNNoise` als DSP-Node direkt im Rust-Backend (`mixforged`).
  * Nutzung von SIMD-Optimierungen (AVX2/FMA) für Intel/AMD-Prozessoren.
  * Latenzbudget: < 10 ms (Puffergröße 480 Samples bei 48 kHz).
  * Ein einfacher Schieberegler in der WinUI 3 `MicrophonePage` ("KI-Rauschfilter: 0 % bis 100 %").

---

### Feature 02: Intelligentes Auto-Ducking (Dynamische Sidechain-Pegelabsenkung)
* **Kategorie:** Audio / DSP & Automation
* **Priorität:** **Hoch**
* **Kurzbeschreibung:** Automatische temporäre Absenkung bestimmter Tonquellen (z. B. Musik, Spiel-Sound), sobald auf einem priorisierten Kanal gesprochen wird (Mikrofon oder Discord-Chat).
* **Benutzer-Nutzen:**
  * Streamer und Podcaster müssen die Musik nicht mehr manuell am Fader herunterziehen, wenn sie sprechen.
  * Gespräche im Sprachchat bleiben immer glasklar verständlich, selbst mitten in epischen, lauten Spielszenen.
* **Technische Umsetzungsskizze:**
  * Sidechain-Detektor im Routing-Mixer des Rust-Daemons:
    * Trigger-Kanal: `Mic` oder `Chat` (RMS-Pegel über einstellbarem Schwellenwert, z. B. -35 dBFS).
    * Ziel-Kanäle: Frei wählbar (z. B. `Music`, `Game`).
    * Parameter: *Duck Amount* (-6 dB bis -30 dB), *Attack* (10–50 ms), *Hold* (200–800 ms), *Release* (300–1200 ms).
  * Exponentielle Glättung ohne Knacksen bei Pegelwechseln.

---

### Feature 03: Echtzeit-Spektrumanalysator & RTA-Visualizer im Mikrofon-EQ
* **Kategorie:** UI/UX & DSP
* **Priorität:** **Hoch**
* **Kurzbeschreibung:** Ein interaktiver Echtzeit-Frequenzgraph (Real-Time Analyzer / RTA), der im Hintergrund des Equalizers die Frequenzen der eigenen Stimme visualisiert.
* **Benutzer-Nutzen:**
  * Bisher müssen Nutzer blind an Bändern schrauben. Mit dem RTA sehen sie genau, bei welcher Frequenz die eigene Stimme dröhnt (z. B. 150 Hz) oder wo zischende Konsonanten sitzen (z. B. 6 kHz).
  * Drastisch vereinfachte und professionelle Klangeinstellung auch für Audio-Einsteiger.
* **Technische Umsetzungsskizze:**
  * FFT-Analyse (1024 oder 2048 Bins) im Rust-Thread (`rustfft`) oder über C# Task.
  * Bündelung der Bins in 64 logarithmische Terz-Bänder (20 Hz bis 20 kHz).
  * Übertragung mit 30–60 FPS über Shared Memory / Named Pipe an das WinUI 3 Frontend.
  * Hardwarebeschleunigtes Rendering mittels `Microsoft.Graphics.Win2D` (`CanvasControl`) als sanfte Farbverlauf-Kurve hinter den EQ-Schiebereglern.

---

### Feature 04: Automatischer Profilwechsel basierend auf aktivem Fenster (App-Watcher)
* **Kategorie:** Automation & Profile
* **Priorität:** **Hoch**
* **Kurzbeschreibung:** Vollautomatischer Profil- und Routing-Wechsel je nachdem, welches Spiel oder welche Anwendung gerade im Windows-Vordergrund aktiv ist.
* **Benutzer-Nutzen:**
  * Startet der Nutzer ein kompetitives Game (z. B. *Counter-Strike 2*), wechselt das GoXLR auf das "Esports"-Profil (Fader 1 = Mic, Fader 2 = Game, schärferes Gate, Höhenanhebung).
  * Startet er *DaVinci Resolve* oder *Premiere Pro*, wird das "Editing"-Profil geladen (alle Sounds auf Monitor-Kopfhörer ohne DSP-Verzerrer).
  * Kein manuelles Umschalten mehr nötig.
* **Technische Umsetzungsskizze:**
  * Hintergrund-Thread in C# registriert Windows Event Hook `SetWinEventHook` (`EVENT_SYSTEM_FOREGROUND`).
  * Ermittlung des Prozessnamens via `GetWindowThreadProcessId` und `Process.GetProcessById()`.
  * Konfigurierbare Mapping-Tabelle in `ProfilesPage`: `cs2.exe -> Profile "Esports"`, `discord.exe -> Profile "Chat Only"`.
  * Sanfter Überblend-Modus (Fade-Out/Fade-In) der Fader- und Routing-Werte beim Profilwechsel.

---

### Feature 05: Native OBS Studio WebSocket v5 Integration (Zwei-Wege-Sync)
* **Kategorie:** Integration & Streaming
* **Priorität:** **Hoch**
* **Kurzbeschreibung:** Direkte, native Verbindung zwischen NEXT : XLR und OBS Studio über das standardisierte `obs-websocket`-Protokoll.
* **Benutzer-Nutzen:**
  * Drückt man am GoXLR auf "Mute to Stream", wird die Audioquelle in OBS ebenfalls sichtbar stummgeschaltet.
  * Der aktuelle Stream-Status (Live, Aufnahme, Pause) kann direkt auf den LEDs oder Scribble Strips des GoXLR angezeigt werden (z. B. rotes Blinken bei aktivem Live-Stream).
  * Szenenwechsel in OBS können automatisch Routing-Änderungen im GoXLR auslösen (z. B. Pausen-Szene mutet automatisch das Mikrofon).
* **Technische Umsetzungsskizze:**
  * Implementierung eines leichtgewichtigen WebSocket-Clients in C# unter Nutzung von `System.Net.WebSockets`.
  * Verbindungsaufbau zu `ws://localhost:4455` mit Passwort-Authentifizierung (SHA256 Challenge/Response nach RFC).
  * Bidirektionale Event-Verarbeitung: `SourceMuteStateChanged` -> GoXLR LED-Update; GoXLR Button-Event -> `SetInputMute`.

---

### Feature 06: Integrierter Sampler-Waveform-Editor mit Drag & Drop
* **Kategorie:** UI/UX & Sampler
* **Priorität:** **Mittel**
* **Kurzbeschreibung:** Ein komfortabler In-App-Editor für Soundboard-Samples mit visueller Wellenform, Schnittfunktion, Normalisierung und Lautstärkeanpassung.
* **Benutzer-Nutzen:**
  * Audio-Dateien (MP3, WAV, OGG, FLAC) können einfach aus dem Windows Explorer auf ein Pad gezogen werden.
  * Unnötige Stille am Anfang des Samples kann direkt mit zwei Schiebereglern weggeschnitten werden.
  * "Normalisieren"-Button bringt alle Sounds auf den gleichen perfekten Pegel, sodass kein Sample zu leise oder ohrenbetäubend laut ist.
* **Technische Umsetzungsskizze:**
  * Waveform-Generator in C# (Dekodierung via `NAudio` / Media Foundation).
  * Interaktives Win2D Canvas mit Start- und End-Markern zum Verschieben mit der Maus.
  * Automatisches Abspeichern als optimiertes 48 kHz / 16-Bit Stereo WAV im Anwendungsdatenordner.

---

### Feature 07: Animierte Scribble-Strip GIFs & Live-Widgets für OLED-Displays
* **Kategorie:** Hardware & UI
* **Priorität:** **Mittel**
* **Kurzbeschreibung:** Unterstützung für animierte GIFs (z. B. 10 FPS Pixel-Art), Spotify-Songtitel und Discord-Sprecheranzeigen auf den kleinen Displays über den Fadern.
* **Benutzer-Nutzen:**
  * Verleiht dem Schreibtisch einen extrem individuellen, lebendigen Look.
  * Zeigt nützliche Live-Infos: Statt nur "Music" scrollt der aktuelle Track-Titel ("Queen – Bohemian Rhapsody") über das Display.
  * Bei Discord-Sprechen leuchtet das Avatar-Icon des aktuell sprechenden Freundes auf.
* **Technische Umsetzungsskizze:**
  * GIF-Frame-Dekoder zerlegt kurze GIFs in 128x64 Pixel 1-Bit Monochrom-Bitmaps.
  * Ratenbegrenzte USB-Übertragung (z. B. 10–12 FPS) über den existierenden Control-Transfer-Endpunkt (`UpdateScribbleStrip`).
  * Anbindung an die Windows `GlobalSystemMediaTransportControlsSessionManager` API zum Auslesen von Spotify/Apple Music Metadaten.

---

### Feature 08: Touch-optimierte Web-Fernbedienung (PWA für Handy & Tablet)
* **Kategorie:** Remote & UI
* **Priorität:** **Mittel**
* **Kurzbeschreibung:** Eine progressive Web-App (PWA), die auf jedem Smartphone, iPad oder Zweit-PC im lokalen Netzwerk ohne Installation im Browser geöffnet werden kann.
* **Benutzer-Nutzen:**
  * Macht das alte Tablet oder Smartphone zum vollwertigen Touch-Mixer und Soundboard.
  * Couch-Gaming: Lautstärke vom Sofa aus anpassen, ohne an den PC gehen zu müssen.
  * Streamer können ihr Soundboard auf das iPad auslagern und die GoXLR-Tasten für andere Aktionen freihalten.
* **Technische Umsetzungsskizze:**
  * Der integrierte HTTP-Server auf Port 48500 liefert eine minimalistische, hochperformante Single-Page-App (HTML5/Canvas/CSS) aus.
  * Zwei-Wege-Kommunikation in Echtzeit über den bestehenden WebSocket (`ws://<pc-ip>:48500/ws`).
  * Autorisierung über einen einfachen 4-stelligen PIN-Code oder Token.

---

### Feature 09: Echtzeit-KI-Stimmenwandler (DirectML / RVC Live Inference)
* **Kategorie:** Audio / DSP
* **Priorität:** **Mittel**
* **Kurzbeschreibung:** Verwandlung der eigenen Stimme in Echtzeit in beliebige Charaktere (z. B. Roboter-Mecha, Fantasy-Zwerg, Ork, weibliche/männliche Stimmfarben) unter Einsatz von Deep Learning (RVC - Retrieval-based Voice Conversion).
* **Benutzer-Nutzen:**
  * Weit überlegene Qualität gegenüber klassischem Pitch-Shifting (kein "Chipmunk"-Effekt).
  * Riesiger Spaßfaktor für Streamer, Rollenspieler (GTA RP / D&D) und Content Creator.
* **Technische Umsetzungsskizze:**
  * Ausführung kompakter ONNX-Modelle über Microsoft DirectML (unterstützt NVIDIA, AMD und Intel GPUs).
  * Audio-Buffer-Größe: 256–512 Samples für ultra-niedrige Inferenzzeit.
  * Umschaltbar über den Effekt-Bereich von NEXT : XLR oder per Tastendruck auf dem Stream Deck.

---

### Feature 10: Visueller Kompressions- & Gain-Reduction-History-Graph
* **Kategorie:** UI/UX & DSP
* **Priorität:** **Mittel**
* **Kurzbeschreibung:** Ein kontinuierlich scrollender Pegelgraph im Mikrofon-Setup, der den Originalpegel und die durch den Kompressor weggedrückte Lautstärke (Gain Reduction) visuell darstellt.
* **Benutzer-Nutzen:**
  * Der Kompressor ist für 90 % aller Heimanwender das am schwersten verständliche Werkzeug.
  * Durch den Graphen sieht der Nutzer auf einen Blick: *„Wenn ich schreie, drückt der Kompressor 6 dB herunter (roter Balken), und die Lautstärke bleibt im grünen Bereich.“*
  * Verhindert Verzerrungen und Clipping schon vor der Aufnahme.
* **Technische Umsetzungsskizze:**
  * Ringpuffer im C#/DSP-Layer für Input-Level, Output-Level und Gain-Reduction (100 Messpunkte pro Sekunde).
  * Win2D Canvas-Rendering mit weicher Kurvenglättung und Farbcodierung (Grün = Nutzsignal, Rot = Dämpfung).

---

### Feature 11: Gehörschutz & Broadcast-True-Peak-Limiter (EBU R128 / LUFS)
* **Kategorie:** Audio & Safety
* **Priorität:** **Mittel**
* **Kurzbeschreibung:** Ein zweistufiger Sicherheits-Limiter: Ein Hard-Clip-Schutz für Kopfhörer (Gehörschutz) und ein normgerechter Broadcast-Limiter nach EBU R128 / ITU-R BS.1770 für den Stream-Ausgang.
* **Benutzer-Nutzen:**
  * **Gehörschutz:** Schützt Gamer vor extremen Lautstärkespitzen (z. B. Bug in einem Early-Access-Spiel, ohrenbetäubender Schrei eines Discord-Trolls, laute Flashbang-Granate).
  * **Stream-Qualität:** Stellt sicher, dass YouTube- oder Twitch-Streams exakt bei -14 LUFS bzw. -1 dB True Peak ausgesteuert sind – kein Übersteuern, kein "zu leiser" Stream.
* **Technische Umsetzungsskizze:**
  * Zero-Latency Lookahead-Limiter (1–2 ms Ringpuffer) im Kopfhörer-Output von `mixforged`.
  * EBU R128 Loudness-Meter im Broadcast-Stream-Pfad.
  * Einstellbare Sicherheitsgrenze in dBFS (z. B. Maximalpegel am Kopfhörer nie über -6 dB).

---

### Feature 12: Virtueller Multikanal-Audio-Treiber (Virtual Audio Cable Engine)
* **Kategorie:** Windows Engine
* **Priorität:** **Niedrig** (Hoher Entwicklungsaufwand, aber enorme Funktionserweiterung)
* **Kurzbeschreibung:** Erstellung von zusätzlichen, rein virtuellen Windows-Audiogeräten (z. B. "NEXT Soundboard", "NEXT Browser", "NEXT Aux"), ähnlich wie bei Elgato Wave Link.
* **Benutzer-Nutzen:**
  * Nutzer des GoXLR Mini (das physisch weniger Regler/Kanäle hat) können beliebig viele virtuelle Unterkanäle erzeugen und mischen.
  * Selbst Nutzer ganz ohne GoXLR-Hardware könnten NEXT : XLR als universellen Software-Mixer für Windows nutzen.
* **Technische Umsetzungsskizze:**
  * Integration eines virtuellen Audio-Treibers (z. B. über das Microsoft Virtual Audio Device Sample / AudioProcessingObject APO oder Partnerschaft mit VB-Audio Cable API).
  * Nahtlose Anbindung an die interne Routing-Matrix in `mixforged`.

---

### Feature 13: Cloud- & Community-Profil-Hub mit One-Click-Import
* **Kategorie:** Cloud & Social
* **Priorität:** **Niedrig**
* **Kurzbeschreibung:** Eine Online-Bibliothek innerhalb der App, in der Creator ihre Equalizer-Presets für beliebte Mikrofone (Shure SM7B, Rode PodMic, Elgato Wave DX) und bunte Beleuchtungs-Themen teilen können.
* **Benutzer-Nutzen:**
  * Einsteiger müssen nicht stundenlang Frequenzen probieren: Mikrofon auswählen, Preset anklicken, fertig ist der perfekte Studio-Sound.
  * Community-Austausch von kreativen Sampler- und Beleuchtungskonfigurationen.
* **Technische Umsetzungsskizze:**
  * Schlanke REST-API (z. B. Cloudflare Workers oder GitHub Releases/Gist-Backend).
  * Galerie-Ansicht in WinUI 3 mit Suchfeld, Tags (z. B. `#ShureSM7B`, `#CyberpunkRGB`) und Like-System.
  * Kryptografische Signaturprüfung und strikte JSON-Schema-Validierung zur Abwehr manipulierter Dateien vor dem Import.

---

### Feature 14: Twitch Chat- & Discord-Event-Trigger (Interaktives Streaming)
* **Kategorie:** Integration & Automation
* **Priorität:** **Niedrig**
* **Kurzbeschreibung:** Zuschauer auf Twitch können über Kanalpunkte (Channel Points) oder Chat-Befehle bestimmte GoXLR-Aktionen für einige Sekunden auslösen.
* **Benutzer-Nutzen:**
  * Höhere Zuschauerbindung und Unterhaltungswert im Stream.
  * Beispiele: Zuschauer löst für 500 Kanalpunkte "Roboter-Stimme für 15 Sekunden" aus, oder lässt ein lustiges Sample abspielen.
* **Technische Umsetzungsskizze:**
  * Twitch EventSub WebSocket-Client (`channel.channel_points_custom_reward_redemption.add`).
  * Event-Dispatcher in C# aktiviert den entsprechenden DSP-Effekt und setzt einen asynchronen Timer zur automatischen Deaktivierung zurück.
  * "Cooldown"- und "Panik-Button"-Funktion, damit Streamer Trolle jederzeit sofort unterbinden können.

---

### Feature 15: MIDI Learn & Controller-Erweiterung (Launchpad, Loupedeck & Co.)
* **Kategorie:** Hardware & I/O
* **Priorität:** **Niedrig**
* **Kurzbeschreibung:** Volle Unterstützung für externe MIDI-Controller (z. B. Novation Launchpad, Korg nanoKONTROL, Behringer X-Touch, MIDI-Fußschalter).
* **Benutzer-Nutzen:**
  * Gitarristen oder Sim-Racer können Mute- und Effektfunktionen mit den Füßen über ein MIDI-Pedal steuern, ohne die Hände vom Lenkrad zu nehmen.
  * Zusätzliche Fader für Nutzer, denen 4 physische Schieberegler nicht ausreichen.
* **Technische Umsetzungsskizze:**
  * Integration von `NAudio.Midi` zur Erkennung aller angeschlossenen USB-MIDI-Eingabegeräte.
  * Komfortabler "MIDI Learn"-Modus in den Einstellungen: Funktion in der Software anklicken, physischen Knopf/Fader am MIDI-Gerät betätigen – fertig verknüpft.
  * Unterstützung für MIDI-CC (Fader/Drehregler) und Note-On/Off (Buttons mit LED-Feedback).

---

## 🎯 Empfohlene Umsetzungs-Reihenfolge (Roadmap)

```mermaid
flowchart TD
    subgraph Phase 1: Sofortiger Audio-Vorsprung (Q1)
        F1["01: KI-Rauschunterdrückung (DeepFilterNet)"]
        F2["02: Auto-Ducking (Sidechain)"]
        F3["03: RTA-EQ-Visualizer"]
    end

    subgraph Phase 2: Workflow & Streaming-Exzellenz (Q2)
        F4["04: App-Auto-Profilwechsler"]
        F5["05: Native OBS WebSocket v5 Sync"]
        F6["06: Sampler Waveform-Editor"]
        F10["10: Visueller Kompressor-Graph"]
    end

    subgraph Phase 3: Hardware-Erweiterung & Personalisierung (Q3)
        F7["07: Animierte Scribble-Strip GIFs"]
        F8["08: Touch Web-Fernbedienung (PWA)"]
        F11["11: True-Peak- & Gehörschutz-Limiter"]
        F15["15: MIDI Learn Engine"]
    end

    subgraph Phase 4: KI & Community-Ökosystem (Q4)
        F9["09: KI-Voice-Changer (RVC)"]
        F12["12: Virtueller Multikanal-Treiber"]
        F13["13: Cloud Profil-Hub"]
        F14["14: Twitch Event-Trigger"]
    end

    Phase 1 --> Phase 2 --> Phase 3 --> Phase 4
```

---
*Bericht erstellt für plysystems — NEXT : XLR Projektleitung.*
