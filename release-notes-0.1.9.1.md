# NEXT : XLR v0.1.9.1-alpha

Offizieller Release **v0.1.9.1-alpha** für NEXT : XLR, die Mixforge Audio Engine und das Elgato Stream Deck Plugin.

## Highlights & Neuerungen

### 1. App-Optimierungen & System Tray Fix
* **System Tray vollständig gefixt**:
  * Zuverlässiges Minimieren ins Windows System-Tray mit Wiederherstellung per Linksklick und Doppelklick.
  * Automatisches Wiederherstellen des Tray-Icons bei Neustart der Windows Explorer-Shell (`TaskbarCreated`).
  * Vollwertiges Rechtsklick-Kontextmenü mit Schnellnavigation ("NEXT : XLR öffnen", "Mischpult", "Mikrofon (DSP)", "Routing Matrix", "Einstellungen", "Beenden").
  * Sicheres Beenden ohne blockierende Dialoge im Hintergrund.
* **Optische Vereinheitlichung & Barrierefreiheit**:
  * Vollständig von Emojis bereinigt: Sämtliche Emojis in Preset-Karten, Menüs und Einstellungsseiten wurden durch einheitliche, gestochen scharfe Segoe Fluent Icons (`FontIcon`) ersetzt.
  * Klares, aufgeräumtes Design mit konsistenter Typografie und optimierter Benutzerfreundlichkeit.

### 2. Audio Engine (`mixforged` v0.1.9.1-alpha)
* **Reduzierte Latenz & Drift-Schutz**:
  * Puffergröße auf ~10-12ms optimiert für ultra-niedrige Eingabe-zu-Ausgabe-Latenz.
  * Aktiver Drift-Schutz: Verhindert das Anstauen von Puffer-Rückständen im laufenden Betrieb.
* **Sound-Qualität**:
  * Integrierter Soft-Saturator / Peak-Limiter schützt vor digitalem Clipping bei hohen Pegeln und liefert einen sauberen, runden Sound ohne Übersteuerungs-Artefakte.
* **Erweiterte API-Endpunkte**:
  * `/api/route`: Vollständige Unterstützung für externe Bluetooth/Wireless-Busse (`external`) und nahtlose Synchronisierung mit `WirelessRouting`.
  * `/api/profile`: Unterstützung für Mikrofon-DSP-Profile (`category=mic`) zur direkten Anwendung von Equalizer, Compressor, De-Esser und Noise Gate.
  * `/api/status`: Liefert den aktuellen Aktivierungsstatus des externen Lautsprechers und der Wireless-Routing-Matrix.

### 3. Elgato Stream Deck Plugin (v0.1.9.1)
* **Routing Matrix**:
  * Quellen **Musik**, **Mikrofon** und **Line In** funktionieren 100% zuverlässig durch intelligentes Token-Matching.
  * Neues Routing-Ziel: **Externer Lautsprecher** (schaltet Umschaltung auf externe Monitore/Bluetooth ein, mit dynamischem Sperr-Icon bei inaktivem Feature).
* **Fader & Lautstärkeregelung**:
  * Schrittweise Lautstärkeänderung (Hoch / Runter) in Schritten: 1%, 2%, 5%, 10% und 25%.
  * Dynamische Pfeil-Icons (Hoch / Runter) mit Schrittanzeige.
  * Fader-Anzeige: Kanalname und Pegel (0–100) oder durchgestrichenes Mikrofon-Icon ("MUTE").
* **Profil laden**: Umschaltung zwischen Mixer-Setup und Mikrofon-DSP-Profilen.
* **Offizielles Icon**: NEXT : XLR Icon aus `_Medien_/nextXLR_icon.png` in allen Auflösungen.
* **Paketierung**: Erstellt mit der offiziellen Elgato Stream Deck CLI (`streamdeck pack`) für nahtlose Installation per Doppelklick.

### 4. Sicherheit & Code-Signing
* Alle Binärdateien (`NextXLR.exe`, `NextXLR.dll`, `mixforged.exe`, `mixforge.exe`, `NextXLR-Setup-0.1.9.1-alpha.exe`, `NextXLR-Uninstall.exe`) sind mit dem offiziellen plysystems-Zertifikat code-signiert.

---

## Downloads & Installation

* **Standalone Setup**: [NextXLR-Setup-0.1.9.1-alpha.exe](https://github.com/plysystems/nextXLR/releases/download/v0.1.9.1-alpha/NextXLR-Setup-0.1.9.1-alpha.exe) (Selbstextrahierend & Code-signiert)
* **Stream Deck Plugin Paket**: [com.plysystems.nextxlr.streamDeckPlugin](https://github.com/plysystems/nextXLR/releases/download/v0.1.9.1-alpha/com.plysystems.nextxlr.streamDeckPlugin) (Direktimport für Elgato Stream Deck)
* **Portables Archiv**: [NextXLR-v0.1.9.1-alpha.zip](https://github.com/plysystems/nextXLR/releases/download/v0.1.9.1-alpha/NextXLR-v0.1.9.1-alpha.zip)

