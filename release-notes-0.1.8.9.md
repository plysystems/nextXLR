# NEXT : XLR v0.1.8.9-alpha 🚀⚡

### 🚀 Highlights: Performance- & Stabilitäts-Update (Extrem wenig CPU & RAM)

In dieser Version wurde NextXLR gezielt auf minimale Systemlast, Zero-Allocation auf Hot-Paths und maximale Hintergrund-Effizienz optimiert.

#### 1. Drastisch reduzierte CPU-Last (UI-Thread Entlastung)
- **Hintergrund-Verarbeitung der Audio-Telemetrie (`MetersMessage`)**:
  - Die 30–60 Pegel-Updates pro Sekunde werden nun direkt auf dem Transport-Hintergrundthread verarbeitet, anstatt den UI-Thread mit tausenden Tasks pro Minute zu belasten.
  - **O(1) Zero-Allocation Channel-Lookup**: Ersetzt lineare LINQ-Suchen durch direkte Array-Indexierung.
  - **Volatile atomare Übergabe**: Lock-freie und thread-sichere Übergabe an die Display-Frame-Clock (`CompositionTarget.Rendering`).
- **Absoluter Leerlauf bei minimiertem Fenster**:
  - Sobald NextXLR minimiert (in die Taskleiste oder den System-Tray) oder verdeckt ist, pausieren sämtliche Metering-Loops und Render-Timer vollständig. Die CPU-Last sinkt auf 0.0 %.
- **Reduzierte WinUI DependencyProperty-Updates**:
  - Fader- und Meter-Properties aktualisieren nur noch, wenn sich sichtbare Werte tatsächlich ändern.

#### 2. Drastisch reduzierter RAM-Verbrauch & Memory-Leak Fixes
- **Automatisches Working-Set Trimming**:
  - Beim Minimieren in den System-Tray oder die Taskleiste wird der ungenutzte Speicher sofort freigegeben (`EmptyWorkingSet` & GC-Kompaktierung). Der Arbeitsspeicher fällt im Hintergrund auf **unter 30 MB**!
- **Behebung von Windows Handle-Leaks**:
  - Im `StreamDeckStatusService` wurden bei der Prozess-Abfrage System-Handles geleakt; alle Prozess-Instanzen werden nun ordnungsgemäß disposet und das Abfrageintervall wurde verdoppelt.
- **Zero-Allocation String Caching**:
  - Audio-Peak-Labels (`Spitze: -XX dB`) werden nun gecacht, wodurch hunderte unnötige String-Allokationen pro Sekunde während der Audiowiedergabe eliminiert wurden.
- **In-Memory Caching für Einstellungen**:
  - `AppSettings` werden im Speicher gecacht, sodass Fenster-Events nicht mehr jedes Mal synchrone Festplatten-Lesezugriffe und JSON-Parsing auslösen.
- **Entfernung von UI-Blockern**:
  - Synchrone Datei-Schreibzugriffe (`LogStartup`) während der XAML-Elementinitialisierung wurden entfernt.
- **Optimierte .NET 8 Runtime-Flags**:
  - Workstation GC aktiviert, Tiered PGO und ReadyToRun eingeschaltet, unnötiger MVVM-Overhead deaktiviert.

---

### 📦 Download & Installation
- **Standalone Setup**: [NextXLR-Setup-0.1.8.9-alpha.exe](https://github.com/plysystems/nextXLR/releases/download/v0.1.8.9-alpha/NextXLR-Setup-0.1.8.9-alpha.exe) (Selbstextrahierend & Code-signiert)
- **Portables Archiv**: [NextXLR-v0.1.8.9-alpha.zip](https://github.com/plysystems/nextXLR/releases/download/v0.1.8.9-alpha/NextXLR-v0.1.8.9-alpha.zip)
