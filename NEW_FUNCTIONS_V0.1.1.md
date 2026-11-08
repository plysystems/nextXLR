# NEXT : XLR – Funktions- & Entwicklungsplanung v0.1.1

> **Projekt:** NEXT : XLR  
> **Version:** 0.1.1  
> **Status:** In Planung / Entwicklung  
> **Entwickler:** plysystems  
> **Ziel:** Erweiterung der Mischpult-Flexibilität, native Stream Deck Integration, tiefgreifende Windows-Lautstärkeverwaltung, professionelle Studio-DSPs und erweiterte Creator-Workflows.

---

## 📑 Inhaltsverzeichnis

1. [Übersicht & Kernziele für Version 0.1.1](#1-übersicht--kernziele-für-version-011)
2. [Modul 1: Windows Lautstärkemixer & App-Audio-Routing](#2-modul-1-windows-lautstärkemixer--app-audio-routing)
3. [Modul 2: Elgato Stream Deck Integration & Plugin](#3-modul-2-elgato-stream-deck-integration--plugin)
4. [Modul 3: Mixer Pages (Virtuelle Fader-Bänke & Hardware-Attach)](#4-modul-3-mixer-pages-virtuelle-fader-bänke--hardware-attach)
5. [Modul 4: VST3- & Studio-Plugin-Optimierung (6 integrierte DSP-Plugins)](#5-modul-4-vst3---studio-plugin-optimierung-6-integrierte-dsp-plugins)
6. [Modul 5: Weitere geplante Funktionen für v0.1.1](#6-modul-5-weitere-geplante-funktionen-für-v011)
7. [Architektur, Schnittstellen & Datenmodelle](#7-architektur-schnittstellen--datenmodelle)
8. [Phasen- & Umsetzungs-Roadmap](#8-phasen---umsetzungs-roadmap)

---

## 1. Übersicht & Kernziele für Version 0.1.1

Mit Version `0.1.0-alpha` wurden Stabilität, Performance, Zero-Latency-Routing für Bluetooth/Wireless sowie die Grundlagen für VST3/CLAP-Hosting und ein robustes Crash-Handling etabliert.

**Version 0.1.1** fokussiert sich auf die praktische Workflow-Optimierung im Streaming- und Creator-Alltag:
* **Entlastung der physischen 4 Fader:** Durch **Mixer Pages** können mit den 4 physischen Hardware-Fadern nun doppelt oder dreifach so viele Audioquellen direkt bedient werden.
* **Kein Wechsel mehr in die Windows-Einstellungen:** Der integrierte **Windows Lautstärkemixer** erlaubt das direkte Zuweisen und Auspegeln einzelner Windows-Programme direkt in NEXT : XLR.
* **Nahtlose Hardware-Erweiterung via Stream Deck:** Volle Unterstützung für Tasten, Profile und die Dials des Stream Deck +.
* **Out-of-the-Box Broadcast Sound:** 6 sofort einsatzbereite, hochwertige DSP-Effekte direkt im Mikrofon-Signalweg ohne Drittanbieter-Installation.

---

## 2. Modul 1: Windows Lautstärkemixer & App-Audio-Routing

### 2.1 Problemstellung
In Windows 10/11 ist das Zuweisen von Anwendungen zu bestimmten GoXLR-Geräten (System, Game, Chat, Music) umständlich über die tief verschachtelten Windows-Systemeinstellungen gelöst. Spiele oder Apps „vergessen“ ihr Audio-Gerät nach Updates oder Neustarts häufig und landen wieder ungewollt auf dem System-Kanal.

### 2.2 Technische Architektur
* **Backend (`mixforge` / Windows Core Audio API):**
  * Anbindung an `IAudioSessionManager2` und `IAudioSessionEnumerator` zur lückenlosen Auflistung aller Audio-erzeugenden Prozesse.
  * Verwendung von `IAudioPolicyConfigFactory` / `IPolicyConfig` zur dynamischen Neuzuweisung des Standard-Wiedergabe- und Aufnahmegeräts pro Prozess-ID (`PID`).
  * Überwachung von Session-Events (`IAudioSessionEvents`) zur sofortigen Erkennung neu gestarteter Spiele oder Medienplayer.
* **Frontend (WinUI 3 unter `Routing` -> `Windows Lautstärkemixer`):**
  * Modernes, übersichtliches Listen- und Kachellayout.
  * Automatische Extraktion des offiziellen Windows-Programmicons und des Prozessnamens (z.B. Spotify, Discord, Call of Duty, Chrome).
  * Direkter Fader (0 % bis 100 %) und Mute-Schalter pro Applikation.
  * Live-Peak-Meter pro Applikation für sofortige visuelle Rückmeldung, welches Programm gerade Ton ausgibt.

```
┌────────────────────────────────────────────────────────────────────────┐
│ 🔀 Windows Lautstärkemixer                                             │
├────────────────────────────────────────────────────────────────────────┤
│ 🎮 Cyberpunk2077.exe  [Game ▾]    [Lautstärke: 85%] [Mute] [■■■■□□]    │
│ 🎧 Discord.exe        [Chat ▾]    [Lautstärke: 70%] [Mute] [■■□□□□]    │
│ 🎵 Spotify.exe        [Music ▾]   [Lautstärke: 55%] [Mute] [■■■■■□]    │
│ 🌐 Chrome.exe         [System ▾]  [Lautstärke: 40%] [Mute] [■□□□□□]    │
└────────────────────────────────────────────────────────────────────────┘
```

### 2.3 Intelligente Auto-Routing-Regeln (Persistent Mapping)
* **Auto-Kategorisierung:**
  * Bekannte VoIP-Programme (Discord, Teams, Skype, Zoom, TeamSpeak) werden automatisch dem Kanal **Chat** vorgeschlagen/zugewiesen.
  * Bekannte Media-Player (Spotify, Apple Music, Tidal, VLC, Chrome/YouTube) erhalten standardmäßig **Music**.
  * Vollbildanwendungen und Spiele (Steam, Battle.net, Epic Games) werden standardmäßig auf **Game** geroutet.
* **Regel-Persistenz:** Gespeichert in `%LOCALAPPDATA%\NextXLR\app_routing.json`, bleibt auch nach Windows-Updates zuverlässig erhalten.

---

## 3. Modul 2: Elgato Stream Deck Integration & Plugin

### 3.1 Architektur & Verbindung
* **Lokaler High-Speed IPC-Server:**
  * Der Rust Audio Daemon `mixforge` stellt einen leichtgewichtigen, lokalen WebSocket-Server (`ws://127.0.0.1:54202`) bereit.
  * Alternativer Named-Pipe Transport (`\\.\pipe\nextxlr-streamdeck`) für extrem niedrige Latenzen (< 2 ms) ohne Netzwerk-Overhead.
* **Offizielles Plugin-Paket (`com.plysystems.nextxlr.streamDeckPlugin`):**
  * Unterstützt Stream Deck (6, 15, 32 Tasten), Stream Deck Mini, Stream Deck Neo, Stream Deck Mobile und **Stream Deck +**.

### 3.2 Tasten-Aktionen (Keypad)
1. **Fader Level & Kanal-Mute:**
   * Mute Toggle für jeden Kanal (Mic, Chat, Music, Game, etc.).
   * Mute-to-Target Aktionen:
     * *Mute to All* (komplett stumm).
     * *Mute to Stream* (Community hört nichts, im Kopfhörer bleibt es hörbar).
     * *Mute to Phones* (nur für den eigenen Kopfhörer stummschalten).
   * Dynamische Status-Icons: Anzeige des aktuellen Lautstärkepegels und Stummschaltzustands direkt auf der Taste.
2. **Mixer Page Switch (Bank-Umschaltung):**
   * Taste schaltet zwischen **Seite 1** und **Seite 2** um.
   * Icon zeigt aktiv: `PAGE 1: MIC/CHAT/MUS/SYS` bzw. `PAGE 2: GAME/CONS/LINE/SAMP`.
3. **Sampler & Soundboard Trigger:**
   * Direkte Tasten für Bank A, B, C und Pad 1–4.
   * Start / Stop / Fade-Out mit animierter Abspielanzeige auf der Taste.
4. **Effekt- & Voice-Morphing Toggle:**
   * Schnelles Aktivieren/Deaktivieren von Megafon, Roboter, Hardtune, KI-Rauschunterdrückung oder Stimm-Personas.
5. **Censor / Bleep Button:**
   * Sofortiges Überblenden mit Bleep-Ton und Muten des Mikrofons im Stream, solange gedrückt gehalten wird.
6. **Profil-Schnellauswahl:**
   * 1-Klick-Wechsel zwischen vorbereiteten Profilen (z.B. „Gaming Live“, „Podcast Solo“, „Late Night Quiet“).

### 3.3 Stream Deck + (Dials & Touch Strip)
* **Rotary Dials (Drehregler):**
  * Feinfühlige Lautstärkeregelung für Fader, Submixes oder VST-Parameter.
  * Drücken des Drehreglers = Mute Toggle.
* **Touch Strip Display:**
  * Live-Stereo-VU-Meter in Echtzeit.
  * Anzeige von Kanalname, Lautstärke in dB und aktuellem Routing.
  * Horizontale Wischgeste über den Touch-Strip schaltet nahtlos zwischen **Mixer Page 1** und **Page 2** um.

---

## 4. Modul 3: Mixer Pages (Virtuelle Fader-Bänke & Hardware-Attach)

### 4.1 Konzept & Notwendigkeit
Die physische Hardware von GoXLR und GoXLR Mini besitzt **4 motorisierte bzw. manuelle Schieberegler**. Moderne Setups erfordern jedoch das simultane Abmischen von bis zu 8 oder mehr separaten Kanälen.

**Mixer Pages** lösen diese Beschränkung elegant durch Umschalten der Fader-Zuordnung in zwei Bänken:

| Hardware-Fader | Seite 1 (Fokus: Voice & Media) | Seite 2 (Fokus: Gaming & Inputs) | Seite 3 (Benutzerdefiniert) |
| :--- | :--- | :--- | :--- |
| **Fader 1** | **Mikrofon** (Mic) | **Game** (PC-Spiele) | Frei belegbar |
| **Fader 2** | **Chat** (Discord, TS) | **Console** (Optisch / Konsole) | Frei belegbar |
| **Fader 3** | **Musik** (Spotify etc.) | **Line In** (Zweit-PC, Handy) | Frei belegbar |
| **Fader 4** | **System** (Windows-Sounds) | **Samples** (Soundboard) | Frei belegbar |

### 4.2 Hardware-Verhalten beim Seitenwechsel
* **GoXLR (Full / Motorfader):**
  * Bei Seitenwechsel fahren alle 4 Motorfader automatisch und geräuschlos auf die exakte Position der neu gewählten Seite.
  * Die kleinen Displays über den Fadern aktualisieren sofort Kanalname, Icon und Fader-Pegel.
* **GoXLR Mini (Manuelle Fader – Soft-Takeover / Catch-Modus):**
  * Da die Fader des Mini keinen Motor besitzen, verhindert ein intelligenter **Soft-Takeover-Algorithmus** Pegelsprünge:
    1. Nach dem Umschalten bleibt die Lautstärke des neuen Kanals exakt auf ihrem virtuellen Wert fixiert.
    2. Der physische Fader übernimmt die Lautstärkeregelung erst in dem Moment, in dem er die aktuelle Position des virtuellen Reglers kreuzt ("Catch").
    3. Die RGB-Beleuchtung des Faders blinkt dezent nach oben oder unten, um anzuzeigen, in welche Richtung der Regler bewegt werden muss, um den Kanal einzufangen.

### 4.3 Bedienung & Umschalt-Trigger
* **Hardware-Tastenkombination:**
  * Druck auf den Button über Mute in Kombination mit dem `!@#$?*`-Censor-Button schaltet die Seite um.
  * Alternativ: Doppelbelegung der Sampler-Bank-Tasten am großen GoXLR.
* **Interaktive Fläche in der Desktop-App:**
  * Im Header des Mixers befindet sich ein prominenter Umschalter:
    `[ Seite 1: Mic / Chat / Musik / System ]` ⇄ `[ Seite 2: Game / Konsole / Line In / Sampler ]`.
* **Stream Deck:**
  * Dedizierte Taste oder Wischgeste auf dem Stream Deck +.

---

## 5. Modul 4: VST3- & Studio-Plugin-Optimierung (6 integrierte DSP-Plugins)

### 5.1 Konzept
Zusätzlich zum Scannen und Laden beliebiger externer VST3- und CLAP-Plugins erhält NEXT : XLR **6 native, hochoptimierte Broadcast-Plugins**, die ohne jede Zusatzinstallation direkt im Mikrofon-Rack einsatzbereit sind:

```
┌────────────────────────────────────────────────────────────────────────┐
│ 🎛️ NEXT : XLR Studio DSP Rack (Mikrofon-Kette)                          │
├────────────────────────────────────────────────────────────────────────┤
│ 1. [EQ]        NEXT : Pro-EQ 6          (Chirurgische Frequenzanpassung)│
│ 2. [DE-ESS]    NEXT : Precision DeEsser (S-Laut- und Zischbändigung)   │
│ 3. [COMP]      NEXT : Broadcast Opto    (Warme, weiche Stimmkontrolle) │
│ 4. [WARMTH]    NEXT : Tube & Tape Sat   (Röhren-Präsenz & Broadcast-Vibe)│
│ 5. [GATE]      NEXT : Smart Expander    (Transparente Rauschelaminierung)│
│ 6. [LIMITER]   NEXT : True-Peak Master  (Clipping-Schutz & Loudness)   │
└────────────────────────────────────────────────────────────────────────┘
```

### 5.2 Die 6 integrierten Studio-Plugins im Detail

#### 1. NEXT : Pro-EQ 6 (Parametrischer Broadcast-Equalizer)
* **Funktion:** 6-Band Präzisions-EQ zur Optimierung von Stimmfarben und Beseitigung störender Raumfrequenzen.
* **Bänder:**
  * Band 1: Einstellbarer High-Pass / Low-Cut Filter (20 Hz bis 300 Hz, 12/24 dB Flankensteilheit).
  * Band 2–4: Vollparametrische Glockenfilter mit variabler Güte (Q: 0.2 bis 12.0) für Grundtonwärme und Mittenbereinigung.
  * Band 5: High-Shelf für weiche Höhenanhebung (3 kHz bis 12 kHz).
  * Band 6: Air-Band (12 kHz bis 20 kHz) für modernen "Radio-Glow".
* **Visualisierung:** Integrierter 60-FPS Echtzeit-Spektrumanalysator (FFT) mit Vorher/Nachher-Kurve.

#### 2. NEXT : Broadcast Opto-Compressor
* **Funktion:** Warme, musikalische Dynamikkontrolle nach dem Vorbild legendärer optischer Studio-Kompressoren (LA-2A / Tube-Tech).
* **Besonderheit:** Programmadaptive Release-Kurve – bei kurzen Transienten reagiert er schnell, bei langen Vokalen weich und unhörbar.
* **Regler:** Threshold, Ratio (1.5:1 bis 10:1), Make-Up Gain, Dry/Wet Mix (für Parallelkompression), analoges VU-Meter für Gain-Reduction.

#### 3. NEXT : Precision Voice De-Esser
* **Funktion:** Zähmt aggressive S-, Sch- und Z-Laute, die bei Kondensatormikrofonen oder heller Stimmcharakteristik im Stream schmerzen.
* **Features:**
  * Einstellbares Frequenzband (4 kHz bis 9 kHz).
  * *Listen-Button:* Isoliert ausschließlich die störenden Zischlaute zum perfekten Einstellen der Filterfrequenz.
  * Dynamische Absenkung nur dann, wenn ein Zischlaut tatsächlich auftritt (keine dumpfe Gesamtstimme).

#### 4. NEXT : Tube & Tape Saturation (Analog Warmth & Exciter)
* **Funktion:** Verleiht dünnen oder sterilen Mikrofonen (z.B. Headsets oder günstigen USB-Mics) echte Röhrenwärme und satten Körper.
* **Technologie:** Erzeugung musikalischer harmonischer Obertöne 2. und 3. Ordnung mit Bandsättigungs-Kompression.
* **Regler:** Drive (Sättigung 0–100%), Warmth (Bassfundament), Air (Oberton-Glanz), Tape Speed (15 ips / 30 ips Simulation).

#### 5. NEXT : Smart Downward Expander & Gate
* **Funktion:** Im Gegensatz zu harten Noise-Gates, die Wortenden unnatürlich "abschneiden", senkt ein Downward Expander den Pegel unterhalb des Thresholds sanft und natürlich ab.
* **Features:**
  * Hysteresis-Regler (verhindert Flattern bei Grenzwerten).
  * Hold- und weiche Release-Kurve für ein natürliches Ausklingen von Atem- und Wortenden.
  * Integrierter Sidechain-Filter (ignoriert dumpfes Rumpeln oder Mausklicks beim Auslösen).

#### 6. NEXT : True-Peak Broadcast Limiter
* **Funktion:** Das finale Glied in der Mikrofonkette. Verhindert jedes digitale Übersteuern (Clipping) im Stream oder Discord, selbst wenn vor Freude ins Mikrofon geschrien wird.
* **Parameter:** Ceiling (-0.1 dB bis -1.5 dB True-Peak), Release-Kurve, Lookahead (1 ms bis 3 ms), EBU R128 Loudness-Meter (LUFS).

### 5.3 UX-Erweiterungen für das Plugin-Hosting
* **Drag-and-Drop Reordering:** Plugins im Rack können per Maus einfach in der Reihenfolge nach oben oder unten verschoben werden.
* **Slot-Bypass & Master-Bypass:** Jeder Slot und das gesamte Rack können mit einem Klick auf Bypass geschaltet werden für schnelle A/B-Hörvergleiche.
* **Performance-Monitor:** Live-Anzeige der DSP-Last in Prozent und Latenz in Millisekunden / Samples pro Plugin.
* **Preset-Bibliothek:** Sofortige Speicherung von Vorlagen als `.json` (z.B. "Shure SM7B Broadcast Chain", "Rode PodMic Warmth", "Elgato Wave 3 Clarity").

---

## 6. Modul 5: Weitere geplante Funktionen für v0.1.1

### 6.1 Automatischer Update-Prüfer & Commit-Hash Updater
* **Hintergrund:** Schnelle Iteration und nahtlose Verteilung von Fixes an Tester und Endnutzer.
* **Funktion in den Einstellungen:**
  * Automatische Abfrage im Hintergrund gegen die GitHub Releases API (`https://api.github.com/repos/plysystems/nextXLR/releases`).
  * Abgleich der Version und des eingebetteten Git-Commit-Hashes (`GitBuildInfo.CommitHash`).
  * Anzeige des Update-Status in den Einstellungen:
    * Grün: *"NEXT : XLR ist auf dem neuesten Stand (Build: `c6c9485`)"*
    * Blau: *"Neues Update verfügbar: v0.1.1 (Build: `a1b2c3d`) – [Jetzt aktualisieren]"*
  * 1-Klick-Download und automatischer Start des Installers mit Erhalt aller Benutzerprofile.

### 6.2 Optimierte Bluetooth- & Wireless-Profile
* **Auto-Discovery & Profilerstellung:**
  * Automatische Erkennung, wenn bekannte Bluetooth-Kopfhörer (z.B. Apple AirPods, Sony WH-1000XM, Bose QC) gekoppelt werden.
  * Automatisches Aktivieren des Wireless-Audio-Routings mit den vorab eingestellten Puffergrößen.
* **Einstellbarer Jitter-Puffer:**
  * Schieberegler in den Einstellungen für Bluetooth-Latenz (15 ms bis 60 ms):
    * *Gaming-Modus:* 15–20 ms (ultrageringe Verzögerung für schnelle Reaktionen).
    * *Stabilitäts-Modus:* 40–50 ms (höhere Puffer-Sicherheit gegen Windows-Bluetooth-Aussetzer).

### 6.3 Visuelle Sampler-Wellenform & Trimming
* **Waveform-Editor in der Sampler-Page:**
  * Beim Anklicken eines Pads wird die Audio-Wellenform visualisiert.
  * Start- und Endpunkt können per Maus-Ziehpunkten visuell verschoben werden, um Stille am Anfang abzuschneiden.
  * Fade-In und Fade-Out Marker direkt in der Wellenform.

### 6.4 Profil-Auto-Backup & Global Hotkeys
* **Automatisches Rollback-Backup:**
  * Vor jeder Profil-Überschreibung wird automatisch eine Sicherung im Ordner `%LOCALAPPDATA%\NextXLR\backups\` angelegt.
* **Globale Windows-Hotkeys:**
  * Frei belegbare Tastenkombinationen für:
    * Mikrofon global stummschalten (Mute).
    * Mixer Page wechseln.
    * Sampler Pad 1–4 abspielen.
    * Profil wechseln.

---

## 7. Architektur, Schnittstellen & Datenmodelle

### 7.1 Datenmodell Mixer Pages (`protocol / lib.rs` & `MainViewModel.cs`)

```json
{
  "active_page": 0,
  "pages": [
    {
      "page_index": 0,
      "name": "Standard / Media",
      "fader_assignments": [
        { "fader_index": 0, "channel": "Mic" },
        { "fader_index": 1, "channel": "Chat" },
        { "fader_index": 2, "channel": "Music" },
        { "fader_index": 3, "channel": "System" }
      ]
    },
    {
      "page_index": 1,
      "name": "Gaming & Inputs",
      "fader_assignments": [
        { "fader_index": 0, "channel": "Game" },
        { "fader_index": 1, "channel": "Console" },
        { "fader_index": 2, "channel": "LineIn" },
        { "fader_index": 3, "channel": "Sample" }
      ]
    }
  ]
}
```

### 7.2 WebSocket-Protokoll für Stream Deck (`mixforge`)

* **Verbindung:** `ws://127.0.0.1:54202/api/v1`
* **Beispiel Request (Page Switch):**
```json
{
  "client": "StreamDeck",
  "command": "SetMixerPage",
  "page_index": 1
}
```
* **Beispiel Broadcast Event:**
```json
{
  "event": "MixerPageChanged",
  "active_page": 1,
  "faders": [
    { "index": 0, "channel": "Game", "volume": 0.82, "muted": false },
    { "index": 1, "channel": "Console", "volume": 0.65, "muted": false },
    { "index": 2, "channel": "LineIn", "volume": 0.0, "muted": true },
    { "index": 3, "channel": "Sample", "volume": 0.70, "muted": false }
  ]
}
```

---

## 8. Phasen- & Umsetzungs-Roadmap

```
┌────────────────────────────────────────────────────────────────────────┐
│ MEILENSTEIN 1: Mixer Pages Architektur & UI-Integration                │
│ • Definition der Seitenstruktur im Protocol & MainViewModel           │
│ • Soft-Takeover für GoXLR Mini & Motor-Recall für GoXLR Full           │
│ • Umschalt-UI im Mixer-Header & Hardware-Button-Trigger                │
├────────────────────────────────────────────────────────────────────────┤
│ MEILENSTEIN 2: Elgato Stream Deck Plugin & IPC-Bridge                  │
│ • Lokaler WebSocket/IPC-Dienst in mixforge                             │
│ • Offizielles .streamDeckPlugin Paket mit Icons und Aktionen           │
│ • Unterstützung für Dials und Touch-Strip auf Stream Deck +           │
├────────────────────────────────────────────────────────────────────────┤
│ MEILENSTEIN 3: Windows Lautstärkemixer & App-Routing                   │
│ • Core Audio Session Enumerator & Policy-Config-Integration            │
│ • WinUI 3 UI-Ansicht unter Routing mit App-Icons und Fadern           │
│ • Persistent Mapping (Auto-Zuweisung für Games, VoIP, Music)          │
├────────────────────────────────────────────────────────────────────────┤
│ MEILENSTEIN 4: Die 6 integrierten Studio-DSP-Plugins                   │
│ • Pro-EQ 6, Opto-Compressor, De-Esser, Saturation, Expander, Limiter   │
│ • Vollwertige UI im Studio Plugin-Editor mit Real-Time-Reglern         │
│ • Drag & Drop Reordering und Preset-Bibliothek                         │
├────────────────────────────────────────────────────────────────────────┤
│ MEILENSTEIN 5: Update-Prüfung, Bluetooth-Profile & Release v0.1.1      │
│ • Commit-Hash Update-Checker in den Einstellungen                      │
│ • Feinschliff Bluetooth Puffer & Profil-Backups                        │
│ • Build, Packaging & Bereitstellung von Version 0.1.1                  │
└────────────────────────────────────────────────────────────────────────┘
```

---
*© plysystems. Alle Rechte vorbehalten.*
