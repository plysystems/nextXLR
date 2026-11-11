# NEXT : XLR v0.1.8.8-alpha 🎛️

### 🚀 Highlights: Perfektes Single-Row Layout (Kein Overflow-Y, garantiert nebeneinander!)

#### 1. Echtes horizontales StackLayout (Umbrechen unmöglich!)
- **Eingänge (8 Fader nebeneinander)**:
  - Verwenden nun ein unteilbares horizontales `StackLayout` (`Spacing="8"`). Ein vertikales Umbrechen in mehrere Reihen ist technisch unmöglich.
  - Dynamische Breitenberechnung: Jeder Kanalzug skaliert proportional mit der verfügbaren Bildschirmbreite.
- **Ausgänge (5 Fader nebeneinander)**:
  - Ebenfalls in einem unteilbaren horizontalen `StackLayout` direkt neben den Eingängen.
- **Overflow-Y komplett deaktiviert**:
  - `VerticalScrollBarVisibility="Disabled"` – es gibt keinerlei vertikales Scrollen auf dem Mischpult-Deck.
  - `HorizontalScrollBarVisibility="Auto"` – bei schmalen Fenstern scrollt das Pult sauber horizontal.

#### 2. Kanonische Kanalreihenfolge
- **Eingänge**:
  1. 🎙️ Mikrofon
  2. 💬 Chat
  3. 🎵 Musik
  4. 🖥️ System
  5. 🎮 Game
  6. 🕹️ Console
  7. 🔌 Line In
  8. 🔊 Samples
- **Ausgänge**:
  1. 🎧 Headphones
  2. 📡 Broadcast Stream Mix
  3. 🔈 Line Out
  4. 🎙️ Chat Mic
  5. 🔊 Monitor

---

### 📦 Download & Installation
- **Standalone Setup**: [NextXLR-Setup-0.1.8.8-alpha.exe](https://github.com/plysystems/nextXLR/releases/download/v0.1.8.8-alpha/NextXLR-Setup-0.1.8.8-alpha.exe) (Selbstextrahierend & Code-signiert)
- **Portables Archiv**: [NextXLR-v0.1.8.8-alpha.zip](https://github.com/plysystems/nextXLR/releases/download/v0.1.8.8-alpha/NextXLR-v0.1.8.8-alpha.zip)

