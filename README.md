# NEXT : XLR

[![Release](https://img.shields.io/badge/Release-v0.1.0--alpha-00d2b4.svg)](https://github.com/plysystems/nextXLR/releases)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20x64-blue.svg)](https://github.com/plysystems/nextXLR)
[![License](https://img.shields.io/badge/License-plysystems%20Free%20License-emerald.svg)](LICENSE)
[![Author](https://img.shields.io/badge/Author-plysystems-informational.svg)](https://github.com/plysystems)

> **NEXT : XLR** ist die moderne, modulare Open-Source Audiosteuerung und Routing-Suite für TC-Helicon GoXLR und GoXLR Mini Geräte sowie Standalone-Virtuelle Mixer unter Windows. Entwickelt von **plysystems**.

---

## Inhaltsverzeichnis / Table of Contents
- [Deutsch](#deutsch)
  - [Überblick](#überblick)
  - [Funktionen](#funktionen)
  - [Gaming Beleuchtungs-Themes](#gaming-beleuchtungs-themes)
  - [Installation & Setup](#installation--setup)
  - [Architektur](#architektur)
  - [Bauen aus dem Quellcode](#bauen-aus-dem-quellcode)
  - [Lizenz & Urheberrecht](#lizenz--urheberrecht)
- [English](#english)
  - [Overview](#overview)
  - [Key Features](#key-features)
  - [Gaming Lighting Themes](#gaming-lighting-themes)
  - [Installation & Setup](#installation--setup-1)
  - [Architecture](#architecture)
  - [Building from Source](#building-from-source)
  - [License & Copyright](#license--copyright)

---

# Deutsch

## Überblick
**NEXT : XLR** ersetzt die offizielle GoXLR Windows-Software durch eine performante, zukunftssichere und optisch erstklassige Lösung. Es kombiniert eine extrem latenzarme Rust-Audio-Engine (`mixforge`) mit einer nativen WinUI 3 Benutzeroberfläche in Fluent Design (C# / .NET 8).

Die Suite steuert sowohl die physische GoXLR- und GoXLR Mini-Hardware (Motorfader, Scribble-Displays, RGB-Beleuchtung, Sampler-Pads) als auch sämtliche virtuellen Windows-Audiokanäle.

## Funktionen

### 1. Intelligenter Mixer
- Alle Kanäle nebeneinander in einer aufgeräumten Übersicht (Mikrofon, Chat, Musik, System, Game, Console, Line In, Samples, Kopfhörer, Broadcast Stream, Line Out, Chat Mic, Monitor).
- Responsive Fader mit dynamischer Ausrichtung (horizontales Layout, intelligenter Umbruch bei schmalem Fenster).
- dB-Spitzenpegelanzeige mit Nullpunkt-Stabilisierung und dynamischem Farbverlauf.
- Direkte Zuweisung der Hardware-Fader A, B, C und D zu beliebigen Eingangsquellen.
- Schnelle Stummschaltungssteuerung und Icon-Anzeige.

### 2. Mikrofon-DSP in Studioqualität
- Komplette Signalverarbeitung: Rauschunterdrückung (Noise Gate), Kompressor, De-Esser und vollparametrischer Equalizer (Bass, Mitten, Höhen).
- Mikrofon-Vorauswahl für dynamische Mikrofone, Kondensatormikrofone (+48V Phantomspeisung) und 3.5mm Headsets.
- Echtzeit-Monitoring und Gain-Regelung.

### 3. Flexible Routing-Matrix
- Vollständige Kreuzschiene: Leite jede Audioquelle (Mikrofon, Chat, Musik, Game, Line In, Sampler) unabhängig auf beliebige Ausgänge (Kopfhörer, Stream Mix, Chat Mic, Line Out).

### 4. Sampler & Soundboard
- 4 Bänke (A, B, C) mit interaktiven Sampler-Pads.
- Bearbeiten, Zuschneiden und Löschen von Samples.
- Dedizierte Mute- und Zensur-Tasten (!@#$?* Bleep).

### 5. Gaming Beleuchtungs-Themes
- Vorkonfigurierte und dynamische Farbstile für Hardware-LEDs und Scribble-Displays:
  - **GTA V / GTA VI:** Neon Vice Pink & Cyan
  - **Counter-Strike (CS):** T/CT Tactical Orange & Dark Steel
  - **Valorant:** Radiant Red & Cool Ice
  - **League of Legends (LoL):** Hextech Gold & Arcane Magic Blue
  - **World of Warcraft (WoW):** Allianz Blau & Horde Crimson Red
  - **Cyberpunk / Cyber Glow:** High-Contrast Neon Green & Violet

### 6. Profilverwaltung & Migration
- Eigene Profile im modernen JSON-Format speichern, laden und exportieren.
- **Nahtloser Import bestehender GoXLR-Dateien:**
  - Unterstützt `.goxlr` Profile der alten Original-App.
  - Unterstützt `.goxlrMicProfile` Mikrofon-Profile.

### 7. Treiberinstallation & mixforge Hintergrunddienst
- Digitale USB-Treiber für GoXLR und GoXLR Mini im Lieferumfang enthalten.
- Automatische Registrierung der 8 Windows-Audiokanäle (System, Chat, Game, Music, Line In, Mic, etc.).
- `mixforged.exe` läuft zuverlässig als extrem ressourcenschonender Hintergrunddienst im Windows-Autostart.

---

## Installation & Setup

### Empfohlener Weg: Installationsassistent
1. Lade das neueste Release `NextXLR-Setup-0.1.0-alpha.exe` von den [Releases](https://github.com/plysystems/nextXLR/releases) herunter.
2. Starte die Setup-Datei.
3. Akzeptiere die Lizenzvereinbarung von plysystems.
4. Wähle die gewünschten Optionen:
   - GoXLR Audiotreiber installieren
   - mixforge Hintergrunddienst einrichten
   - Desktop- & Startmenü-Verknüpfung erstellen
5. Klicke auf **Installieren**. Nach Abschluss startet NEXT : XLR automatisch.

Alternativ steht im Repository das Skript `install.bat` für eine automatisierte Installation zur Verfügung.

---

## Architektur
Das Projekt ist modular nach Best Practices aufgebaut:

```
goxlr/
├── crates/
│   ├── protocol/      Wire-Types & IPC-Nachrichtenprotokoll (API v1, JSON)
│   ├── engine/        Real-Time Audio-Engine (WASAPI, cpal, DSP, Lock-free Ringbuffer)
│   ├── usb/           TC-Helicon USB-Treiber, HardwareDevice Abstraktion, Simulator
│   └── daemon/        mixforged: Audio-Host, Named Pipe (\\.\pipe\mixforge) & WebSocket
├── apps/
│   ├── windows/       WinUI 3 Desktop-App (C# / .NET 8 / Fluent Design)
│   └── ui/            Web-Interface (optional)
├── installer/         Windows Installationswizard (C# / .NET 8 WinForms)
├── Assets/Driver/     Signierte TC-Helicon GoXLR Audiotreiber (WDM/ASIO)
└── docs/              Architektur- und Protokolldokumentation
```

---

## Bauen aus dem Quellcode

### Voraussetzungen
- Windows 10 (Build 19041+) oder Windows 11 (x64)
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download)
- [Rust](https://rustup.rs/) (stable, MSVC toolchain) mit C++ Build Tools
- Visual Studio 2022 oder VS Build Tools mit Windows App SDK

### Kompilieren
```powershell
# 1. Daemon (Audio Engine) kompilieren:
cargo build --bin mixforged

# 2. Windows UI-App bauen:
dotnet build apps/windows/NextXLR.csproj

# 3. Setup-Assistenten bauen:
dotnet build installer/NextXLR.Installer.csproj
```

---

## Lizenz & Urheberrecht

**Copyright (c) 2026 plysystems. Alle Rechte vorbehalten.**

Diese Software wird von plysystems unter der folgenden Lizenzvereinbarung bereitgestellt:

### Erlaubt:
- **Private Nutzung:** Uneingeschränkt und kostenfrei.
- **Kommerzielle Nutzung:**
  - Live-Streaming (Twitch, YouTube, Kick usw.)
  - Content Creation (YouTube-Videos, Podcasts, Broadcast-Produktion)
- **Firmeninterne Nutzung:** Einsatz in Unternehmen, Studios und Organisationen.
- **Änderungen am Quellcode:** Anpassung und Weiterentwicklung für eigene Zwecke.
- **Weiterverteilung:** Weitergabe des Originals oder veränderter Versionen.

### Pflicht & Bedingungen:
- Der **Copyright-Hinweis ("Copyright (c) plysystems")** sowie der vollständige Lizenztext müssen in allen Kopien, Distributionen oder wesentlichen Teilen der Software beibehalten werden.
- Die Software wird ohne ausdrückliche oder stillschweigende Gewährleistung bereitgestellt.

---

# English

## Overview
**NEXT : XLR** replaces the official GoXLR Windows application with a robust, modern, and high-performance solution. It pairs an ultra-low-latency Rust audio engine (`mixforge`) with a native WinUI 3 Fluent Design user interface (C# / .NET 8).

The software manages physical TC-Helicon GoXLR and GoXLR Mini hardware (motorized faders, scribble strips, RGB lighting, sample pads) alongside virtual Windows audio endpoints.

## Key Features

### 1. Intelligent Mixer
- All channels displayed side-by-side (Mic, Chat, Music, System, Game, Console, Line In, Samples, Headphones, Broadcast Stream, Line Out, Chat Mic, Monitor).
- Responsive fader layout with automatic reflow on narrow windows.
- Accurate peak dB metering with zero-bleed calibration and smooth gradients.
- Direct assignment of hardware faders A, B, C, and D to any input source.
- Quick mute toggles with clear icon indicators.

### 2. Studio-Grade Microphone DSP
- Comprehensive processing chain: Noise Gate, Compressor, De-Esser, and 3-Band Parametric Equalizer (Bass, Mid, Treble).
- Microphone preset profiles for Dynamic, Condenser (+48V phantom power), and 3.5mm Headset mics.
- Real-time sidetone monitoring and gain staging.

### 3. Full Audio Routing Matrix
- Flexible crossbar routing: route any input (Mic, Chat, Music, Game, Line In, Samples) independently to any output (Headphones, Broadcast Stream, Chat Mic, Line Out).

### 4. Sampler & Soundboard
- 4 sound banks (A, B, C) with interactive pad controls.
- Sample editing, clearing, and assignment.
- Dedicated Mute and Bleep (!@#$?*) swear filter buttons.

### 5. Gaming Lighting Themes
- Pre-configured RGB themes tailored for popular titles:
  - **GTA V / GTA VI:** Neon Vice Pink & Cyan
  - **Counter-Strike (CS):** T/CT Tactical Orange & Dark Steel
  - **Valorant:** Radiant Red & Cool Ice
  - **League of Legends (LoL):** Hextech Gold & Arcane Magic Blue
  - **World of Warcraft (WoW):** Alliance Blue & Horde Crimson
  - **Cyberpunk / Cyber Glow:** Vibrant Neon Green & Violet

### 6. Profile Management & Migration
- Export and import setups in modern JSON format.
- **Native import support for legacy GoXLR configurations:**
  - Import legacy `.goxlr` profile files.
  - Import legacy `.goxlrMicProfile` microphone presets.

### 7. Driver Setup & mixforge Background Service
- Bundles digitally signed GoXLR USB drivers.
- Automated creation and setup of Windows audio endpoints (System, Chat, Game, Music, Line In, Mic, etc.).
- Lightweight background service `mixforged.exe` starts automatically with Windows.

---

## Installation & Setup

### Recommended: Setup Wizard
1. Download `NextXLR-Setup-0.1.0-alpha.exe` from [GitHub Releases](https://github.com/plysystems/nextXLR/releases).
2. Run the installer executable.
3. Review and accept the plysystems License Agreement.
4. Select desired options (Driver installation, background service autostart, desktop shortcuts).
5. Click **Install**. NEXT : XLR is ready to use immediately.

For automated or scripted deployments, run `install.bat` directly from the repository.

---

## Architecture

```
goxlr/
├── crates/
│   ├── protocol/      Wire format & IPC serialization (API v1, JSON)
│   ├── engine/        Real-time audio engine (WASAPI, cpal, DSP, ring buffers)
│   ├── usb/           TC-Helicon USB communication & hardware abstraction
│   └── daemon/        mixforged: audio host with named pipe & WebSocket APIs
├── apps/
│   ├── windows/       Native WinUI 3 desktop client (.NET 8 / C#)
│   └── ui/            Web companion interface
├── installer/         Windows setup wizard (.NET 8 WinForms)
├── Assets/Driver/     Signed TC-Helicon GoXLR USB audio drivers (WDM/ASIO)
└── docs/              Architecture and protocol guides
```

---

## Building from Source

### Prerequisites
- Windows 10 (19041+) or Windows 11 (x64)
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download)
- [Rust](https://rustup.rs/) (stable, MSVC toolchain)
- Visual Studio 2022 or Build Tools with Windows App SDK

### Build Commands
```powershell
# 1. Build the mixforge daemon:
cargo build --bin mixforged

# 2. Build the Windows UI client:
dotnet build apps/windows/NextXLR.csproj

# 3. Build the Installer:
dotnet build installer/NextXLR.Installer.csproj
```

---

## License & Copyright

**Copyright (c) 2026 plysystems. All rights reserved.**

Provided by plysystems under the following terms:

### Permitted:
- **Private Use:** Completely free and unrestricted.
- **Commercial Use:**
  - Live streaming (Twitch, YouTube, Kick, etc.)
  - Content creation (YouTube videos, podcasts, broadcast production)
- **Internal Business Use:** Permitted across companies, studios, and organizations.
- **Source Modifications:** Freely edit and extend the codebase.
- **Redistribution:** Freely redistribute original or modified versions.

### Obligations & Conditions:
- The **copyright notice ("Copyright (c) plysystems")** and this complete license text must be retained in all copies, redistributions, or substantial portions of the Software.
- The software is provided "AS IS", without warranties of any kind.
