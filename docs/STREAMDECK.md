# NEXT : XLR — Offizielles Stream Deck Plugin & API

NEXT : XLR bietet eine voll integrierte, offizielle **Elgato Stream Deck Integration** (`com.plysystems.nextxlr.sdPlugin`) sowie eine ultraschnelle lokale HTTP REST & WebSocket API, um die Audio-Engine, Fader, Mutes, Effekte, Routing und Profile direkt über ein **Elgato Stream Deck** (oder Alternativen wie Loupedeck, Touch Portal, Bitfocus Companion) zu steuern.

Die Engine läuft lokal standardmäßig auf Port **`48500`** (mit automatischer Kompatibilität auf Port `14565`).

---

## 1. Offizielles Stream Deck Plugin (Empfohlen)

NEXT : XLR liefert ein maßgeschneidertes, natives Elgato Stream Deck Plugin mit voller Unterstützung für **Stream Deck, Stream Deck Mini, Stream Deck XL, Stream Deck Mobile** und **Stream Deck + (Drehregler & Touchstrip)**.

### 1-Klick Installation:
1. Öffne NEXT : XLR und gehe auf **Einstellungen** → **Stream Deck**.
2. Klicke auf **"Plugin installieren"**.
3. Das Plugin wird sofort registriert und erscheint in der Elgato Stream Deck Software unter der Kategorie **NEXT : XLR**.

Alternativ kann die Datei `dist/com.plysystems.nextxlr.streamDeckPlugin` mit einem Doppelklick direkt in Elgato Stream Deck importiert werden.

### Enthaltene Aktionen:
* 🎚️ **Lautstärke / Fader**: Beliebigen Kanal oder Fader (Mikrofon, Chat, Musik, Game, etc.) lauter/leiser stellen oder fest setzen. Auf dem **Stream Deck +** kann die Lautstärke stufenlos über die Drehregler angepasst werden (Touch = 100%, Push = Mute).
* 🔇 **Kanal Mute**: Einzelne Kanäle per Tastendruck stummschalten oder umschalten (Toggle). Zeigt den Status live auf der Taste an.
* 🎙️ **Husten-Taste (Mic Mute)**: Mikrofon stummschalten (Toggle oder Push-to-Mute / Halten).
* 🤬 **Zensur / Bleep (!@#$?*)**: Aktiviert den GoXLR Zensur-Piepton und mutet das Mikrofon, solange die Taste gedrückt wird.
* 🔄 **Routing Matrix**: Umschalten bestimmter Audiowege (z. B. Musik auf Stream-Mix an/aus).
* 💾 **Profil laden**: Wechselt direkt zu einem gespeicherten Setup/Profil.

---

## 2. Direkte REST-API Endpunkte (Port 48500 & 14565)

Für eigene Scripts, Companion oder HTTP-Buttons:

### Basis-URL:
```
http://127.0.0.1:48500
```
*(Port `14565` wird parallel unterstützt).*

### A) Lautstärke / Fader (`/api/fader`)
* **Kanal lauter (+5%):**
  `GET http://127.0.0.1:48500/api/fader?channel=music&delta=+0.05`
* **Kanal leiser (-5%):**
  `GET http://127.0.0.1:48500/api/fader?channel=music&delta=-0.05`
* **Fader A auf 75% setzen:**
  `GET http://127.0.0.1:48500/api/fader?channel=A&volume=0.75`
* **Kanal-Namen:** `mic`, `chat`, `music`, `game`, `console`, `linein`, `system`, `samples` oder Fader `A`, `B`, `C`, `D`.

### B) Stummschalten / Mute (`/api/mute`)
* **Mute umschalten (Toggle):**
  `GET http://127.0.0.1:48500/api/mute?channel=music&action=toggle`
* **Fader B stummschalten:**
  `GET http://127.0.0.1:48500/api/mute?channel=B&action=mute`
* **Stummschaltung aufheben:**
  `GET http://127.0.0.1:48500/api/mute?channel=music&action=unmute`

### C) Mikrofon-Stummschaltung / Husten-Taste (`/api/mic-mute`)
* **Mikrofon Mute umschalten:**
  `GET http://127.0.0.1:48500/api/mic-mute?action=toggle`
* **Explizit stummschalten:**
  `GET http://127.0.0.1:48500/api/mic-mute?muted=true`

### D) Bleep / Zensur-Taste (`/api/bleep`)
* **Key Down (Taste gedrückt):**
  `GET http://127.0.0.1:48500/api/bleep?active=true`
* **Key Up (Taste losgelassen):**
  `GET http://127.0.0.1:48500/api/bleep?active=false`

### E) Routing Matrix umschalten (`/api/route`)
* **Mikrofon auf Kopfhörer umschalten (Toggle):**
  `GET http://127.0.0.1:48500/api/route?channel=mic&bus=headphones&action=toggle`
* **Musik auf Stream-Mix aktivieren:**
  `GET http://127.0.0.1:48500/api/route?channel=music&bus=stream&gain=1.0`
* **Bus-Namen:** `headphones`, `stream`, `lineout`, `chatmic`, `sampler`.

### F) Profil laden (`/api/profile`)
* **Profil "Gaming" laden:**
  `GET http://127.0.0.1:48500/api/profile?name=Gaming`

### G) Status-Abfrage (`/api/status`)
* Gibt den gesamten Mixerzustand als JSON zurück:
  `GET http://127.0.0.1:48500/api/status`

---

## 3. WebSocket API

Echtzeit-Telemetrie und bidirektionale Steuerung:
```
ws://127.0.0.1:48500/ws
```
