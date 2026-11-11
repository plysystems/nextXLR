# NEXT : XLR v0.1.8.4-alpha 🎛️

### 🚀 Highlights & Verbesserungen

#### 1. Garantiertes Nebeneinander-Layout (Kein Zeilenumbruch!)
- **Eingänge (8 Fader nebeneinander)**:
  - Alle 8 Eingangskanäle (*Mikrofon, Chat, Musik, Game, System, Samples, Line In, Console*) bleiben nun garantiert strikt in einer einzigen horizontalen Reihe nebeneinander angeordnet.
  - Das Wrapping-Problem, bei dem der 8. Fader durch Rundungsabweichungen in eine zweite Zeile umgebrochen wurde, ist durch exakte Pixel-Spaltenberechnung und -0.5px Defensivmaß vollständig behoben.
- **Ausgänge (5 Fader nebeneinander)**:
  - Alle 5 Ausgangskanäle (*Headphones, Broadcast Stream Mix, Line Out, Chat Mic, Monitor*) stehen ebenfalls ausnahmslos nebeneinander in einer Reihe.
- **Feste Obergrenze & Null-Abstand vertikal**:
  - `MaximumRowsOrColumns` ist fest auf 8 (Eingänge) bzw. 5 (Ausgänge) verdrahtet, `MinRowSpacing` ist auf 0 gesetzt.
  - Auf schmaleren Bildschirmen scrollt die Mixing-Konsole sauber horizontal, ohne jemals Kanäle untereinander zu verschieben.

---

### 📦 Download & Installation
- **Standalone Setup**: [NextXLR-Setup-0.1.8.4-alpha.exe](https://github.com/plysystems/nextXLR/releases/download/v0.1.8.4-alpha/NextXLR-Setup-0.1.8.4-alpha.exe) (Selbstextrahierend & Code-signiert)
- **Portables Archiv**: [NextXLR-v0.1.8.4-alpha.zip](https://github.com/plysystems/nextXLR/releases/download/v0.1.8.4-alpha/NextXLR-v0.1.8.4-alpha.zip)

