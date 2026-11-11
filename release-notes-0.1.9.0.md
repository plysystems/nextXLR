# NEXT : XLR v0.1.9.0-alpha 🚀🎛️

### 🚀 Highlights: Offizielles Elgato Stream Deck Plugin & Port 48500 Integration

In dieser Version erhält NEXT : XLR ein **vollständig entwickeltes, offizielles Elgato Stream Deck Plugin** (`com.plysystems.nextxlr.sdPlugin`) mit nativer Unterstützung für alle Stream Deck Modelle inklusive **Stream Deck + (Drehregler & Touchstrip)** sowie die Behebung der Engine API-Erreichbarkeit auf Port 48500.

---

#### 1. Offizielles Elgato Stream Deck Plugin (`com.plysystems.nextxlr.sdPlugin`)
* **1-Klick Installation**:
  * In NEXT : XLR unter **Einstellungen** → **Stream Deck** kann das Plugin mit einem Klick auf **"Plugin installieren"** sofort installiert und registriert werden.
  * Alternativ liegt dem Release die eigenständige `com.plysystems.nextxlr.streamDeckPlugin`-Datei bei (Doppelklick genügt).
* **6 Vollwertige Stream Deck Aktionen**:
  1. 🎚️ **Lautstärke / Fader**:
     * Stufenlose Steuerung von Lautstärke für alle Kanäle (Mikrofon, Chat, Musik, System, Game, Console, Line In, Samples) oder GoXLR-Fader A–D.
     * Unterstützt lauter (+), leiser (-) oder feste Prozentwerte.
     * **Stream Deck + Unterstützung**: Stufenlose Lautstärkeregelung über die physischen Drehregler (Encoder) inklusive Pegel-Anzeige auf dem LCD-Touchstrip, Mute beim Drücken des Reglers und Reset auf 100% per Touch!
  2. 🔇 **Kanal Mute**:
     * Umschalten (Toggle), Stummschalten oder Entstummen einzelner Kanäle mit Live-Zustandsanzeige auf der Taste.
  3. 🎙️ **Husten-Taste (Mic Mute)**:
     * Globales Stummschalten des Mikrofons als Umschalter oder als Push-to-Mute (nur stumm, solange gedrückt).
  4. 🤬 **Zensur / Bleep (!@#$?*)**:
     * Aktiviert den GoXLR Zensur-Piepton und mutet das Mikrofon in Echtzeit, solange die Taste gedrückt wird.
  5. 🔄 **Routing Matrix**:
     * Direkte Steuerung von Audio-Routen (z. B. Musik auf Broadcast-Stream aktivieren/deaktivieren).
  6. 💾 **Profil laden**:
     * Wechselt per Tastendruck blitzschnell zwischen NEXT : XLR Setups und Profilen (z. B. Gaming, Stream, Podcasting).

---

#### 2. Engine API: Port 48500 & Multi-Port-Unterstützung
* **Standard-Port 48500**:
  * Der Audio-Daemon `mixforged` lauscht standardmäßig auf Port `48500` (statt zuvor 14565).
  * Parallele Rückwärtskompatibilität: Port `14565` bleibt als sekundärer Listener aktiv, sodass bestehende HTTP-Requests nahtlos weiterfunktionieren.
* **Auto-Discovery & Engine-Start**:
  * NEXT : XLR erkennt den aktiven Port der Audio-Engine automatisch.
  * In den Stream Deck Einstellungen steht nun ein **"Engine starten"**-Button zur Verfügung, falls der Hintergrunddienst noch nicht gestartet war.

---

### 📦 Download & Installation
* **Standalone Setup**: [NextXLR-Setup-0.1.9.0-alpha.exe](https://github.com/plysystems/nextXLR/releases/download/v0.1.9.0-alpha/NextXLR-Setup-0.1.9.0-alpha.exe) (Selbstextrahierend & Code-signiert)
* **Stream Deck Plugin Paket**: [com.plysystems.nextxlr.streamDeckPlugin](https://github.com/plysystems/nextXLR/releases/download/v0.1.9.0-alpha/com.plysystems.nextxlr.streamDeckPlugin) (Direktimport für Elgato Stream Deck)
* **Portables Archiv**: [NextXLR-v0.1.9.0-alpha.zip](https://github.com/plysystems/nextXLR/releases/download/v0.1.9.0-alpha/NextXLR-v0.1.9.0-alpha.zip)

