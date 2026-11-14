# NEXT : XLR v0.1.9.2-alpha

Offizieller Release **v0.1.9.2-alpha** für NEXT : XLR, die Mixforge Audio Engine und Hardware-Steuerung.

## Highlights & Neuerungen

### 1. Fader-Kalibrierung & Standby-Verhalten (Motor-Fader)
* **Startup-Justierung (0 -> 100 -> Eingestellter Wert)**:
  * Beim Starten der Software / Initialisieren des GoXLR fahren alle motorisierten Fader automatisch einen Kalibrierungssweep von 0 bis 100% (Endstop-Kalibrierung) ab.
  * Anschließend stellen sich die Fader sofort sanft und präzise auf die im Profil eingestellten Lautstärkewerte ein.
  * Eine intelligente Filterung sorgt dafür, dass die Motorfahrten während der Initialisierung nicht als Benutzerinteraktion fehlinterpretiert werden.
* **Shutdown (Fader auf 0 ohne Speichern)**:
  * Beim Herunterfahren des PCs oder Beenden der Software werden die motorisierten Fader automatisch auf 0 gefahren.
  * **Wichtig**: Die ursprünglichen Lautstärkewerte bleiben im Profil und in den Einstellungen vollständig erhalten und werden nicht mit 0 überschrieben. Beim nächsten Starten stehen alle Fader wieder auf ihren gewünschten Werten.

### 2. Hardware-Ruhemodus (Standby beim Herunterfahren & Schließen)
* **Automatischer Standby bei PC-Shutdown / Neustart / Energiesparmodus**:
  * Dedizierter Windows-Power-Watcher registriert Systemereignisse (`WM_QUERYENDSESSION`, `WM_ENDSESSION`, `WM_POWERBROADCAST`).
  * Beim Herunterfahren, Abmelden oder Energiesparmodus schaltet NEXT : XLR die GoXLR-Hardware unmittelbar in einen stromsparenden und lautlosen Ruhemodus (Beleuchtung dunkel, Scribble-Strips aus, alle Tasten-LEDs abgeschaltet).
  * Keine störenden grellen LEDs mehr bei ausgeschaltetem oder schlafendem PC.
* **Standby beim bewussten Beenden der Software**:
  * Wird NEXT : XLR über das Hauptfenster, das System-Tray oder das Kontextmenü regulär beendet, versetzt die Software die Hardware vor dem Schließen automatisch in den Ruhemodus (`/api/power?action=shutdown`).
* **Nahtloses Aufwachen (Wake on Input / Resume)**:
  * Sobald Windows aus dem Energiesparmodus aufwacht oder der Nutzer eine Taste / Regler am GoXLR berührt, wird die Hardware reaktiviert und das konfigurierte Farbprofil sowie alle Display-Inhalte sofort wiederhergestellt.

### 2. Audio Engine & API Optimierungen (`mixforged` v0.1.9.2-alpha)
* Neuer Endpunkt `/api/power` zur direkten Steuerung des Hardware-Standbys (`sleep`, `wake`, `shutdown`) über Skripte, Stream Deck oder App.
* Synchronisierte Thread-Kommunikation für Hardware-Flush garantiert, dass das Standby-Kommando zuverlässig und vollständig an die GoXLR USB-Schnittstelle übermittelt wird, bevor Prozesse beendet werden.

### 3. Sicherheit & Code-Signing
* Alle Binärdateien (`NextXLR.exe`, `NextXLR.dll`, `mixforged.exe`, `mixforge.exe`, `NextXLR-Setup-0.1.9.2-alpha.exe`, `NextXLR-Uninstall.exe`) sind mit dem offiziellen plysystems-Zertifikat code-signiert.

---

## Downloads & Installation

* **Standalone Setup**: [NextXLR-Setup-0.1.9.2-alpha.exe](https://github.com/plysystems/nextXLR/releases/download/v0.1.9.2-alpha/NextXLR-Setup-0.1.9.2-alpha.exe) (Selbstextrahierend & Code-signiert)
* **Stream Deck Plugin Paket**: [com.plysystems.nextxlr.streamDeckPlugin](https://github.com/plysystems/nextXLR/releases/download/v0.1.9.2-alpha/com.plysystems.nextxlr.streamDeckPlugin) (Direktimport für Elgato Stream Deck)
* **Portables Archiv**: [NextXLR-v0.1.9.2-alpha.zip](https://github.com/plysystems/nextXLR/releases/download/v0.1.9.2-alpha/NextXLR-v0.1.9.2-alpha.zip)

