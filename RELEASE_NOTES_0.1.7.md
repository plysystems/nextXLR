### NEXT : XLR v0.1.7-alpha Release Notes

#### 🎚️ Mixer: Optimierter Dual-Mix, Fader-Steuerung & Unboxed Modern UI
- **Flexible physische Fader-Auswahl im Dual-Modus:**
  - Im `Dual`-Modus steht nun direkt neben dem Umschalter ein Wahl-Segment zur Verfügung: **Fader: `[ Kopfhörer ]` oder `[ Stream ]`**.
  - Der Nutzer kann jederzeit wählen, welcher Mix von den physischen GoXLR-Hardwarefadern gesteuert wird.
  - Das Umschalten aktiviert sofort die physische Motorsteuerung der Hardware und positioniert die Fader exakt auf das gewählte Mix-Level.
- **Hardware-Fader Steuerung unter `Stream` & `Kopfhörer`:**
  - **Unter `Stream`:** Wenn aktiv, steuern die physischen GoXLR-Fader automatisch den Haupt-Stream-Mix und alle Hardware-Fader werden auf die Stream-Werte eingestellt.
  - **Unter `Kopfhörer`:** Wenn aktiv, steuern die physischen GoXLR-Fader automatisch den Kopfhörer-Mix (Monitor) und werden auf die Kopfhörer-Werte eingestellt.
  - Das Fader-Badge `[ A ]`, `[ B ]`, `[ C ]`, `[ D ]` wandert dynamisch zur jeweils aktiven Spalte.
- **Optische Mixer-Optimierung (Kein "Boxing"):**
  - Starres "Boxing" (schwere Kasten-Rahmen um jeden Kanalstreifen, den Fader-Bereich und die Bleep-Sektion) wurde entfernt.
  - Moderner, konsolenartiger Look mit weicheren Radien, dezenten Separatoren und ergonomischen Fadern.
  - Parallele Spaltenführung in der Dual-Ansicht mit feiner Mitten-Trennlinie zwischen Stream- und Kopfhörer-Fadern.

#### 🎙️ Auto-Ducking (Optimierte Benutzerfreundlichkeit & Neue Position)
- **Vereinheitlicht:** Konsistenter Name **"Auto-Ducking"**.
- **Intuitive Positionierung:**
  - Vollständig aus der Mixer-Fader-Box entfernt.
  - Auf der **Routing-Seite** (zwischen Routing-Tabelle und Windows-Lautstärkemixer) als zentrale Signalverarbeitungs-Card platziert.
  - Synchronisiert auf der **Mikrofon-Seite** integriert.
- **Benutzerfreundlichkeit:**
  - Dropdown-Menüs (`ComboBox`) für Trigger (`Mic`, `Chat`, `Line In`, `Console`) und Ziel (`Music`, `Game`, `System`, `Chat`, `Sample`).
  - Live Gain-Reduction-Meter und feine Slider für Dämpfung, Schwelle, Hold und Release mit automatischer Speicherung.

#### 🛠️ Wichtige Stabilitäts- & Performance-Fixes
- **Beseitigung von `0x80004002 (E_NOINTERFACE)` Crashes & Blackscreens:**
  - Fader-Bindings vollständig auf `Mode=OneWay` mit Benutzerinteraktions-Callbacks entkoppelt.
  - Keine blockierenden COM-Abfragen oder Render-Tree-Freezes mehr.
- **Virtuelle Kabel:**
  - Direkte Zuweisung auf bestehende GoXLR-Kanäle ohne separate Fader-Verstopfung im Mixer.

#### 🔐 Integrität & Code-Signing
- Authenticode-signiert mit Entwicklerzertifikat (`plysystems`).
