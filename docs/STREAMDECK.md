# NEXT : XLR — Stream Deck Integration

NEXT : XLR bietet eine integrierte, ultraschnelle HTTP REST & WebSocket API, um die Audio-Engine, Fader, Mutes, Effekte, Routing und Profile direkt über ein **Elgato Stream Deck** (oder Alternativen wie Loupedeck, Touch Portal, Bitfocus Companion) zu steuern.

Die API läuft lokal auf Port `14565` (bzw. konfigurierbar über `MIXFORGE_BIND`).

---

## 1. Schnelleinrichtung im Stream Deck

Für die Steuerung über das Stream Deck kann jedes generische HTTP-Plugin verwendet werden (z. B. **"API Request"**, **"HTTP Request"** oder **"Advanced Launcher"** aus dem Elgato Stream Deck Store).

### Basis-URL:
```
http://127.0.0.1:14565
```

---

## 2. API Endpunkte

### A) Lautstärke / Fader (`/api/fader`)
Steuert die Lautstärke eines Kanals oder physischen Faders. Unterstützt absolute Werte (`0.0`–`1.0` oder `0`–`100`) sowie relative Schritte (`delta` für Lauter/Leiser-Buttons).

* **Kanal lauter (+5%):**
  `GET http://127.0.0.1:14565/api/fader?channel=music&delta=+0.05`
* **Kanal leiser (-5%):**
  `GET http://127.0.0.1:14565/api/fader?channel=music&delta=-0.05`
* **Fader A auf 75% setzen:**
  `GET http://127.0.0.1:14565/api/fader?channel=A&volume=0.75`
* **Kanal-Namen:** `mic`, `chat`, `music`, `game`, `console`, `linein`, `system`, `samples` oder Fader `A`, `B`, `C`, `D`.

---

### B) Stummschalten / Mute (`/api/mute`)
Schaltet einen Kanal stumm, hebt die Stummschaltung auf oder schaltet um (`toggle`).

* **Mute umschalten (Toggle):**
  `GET http://127.0.0.1:14565/api/mute?channel=music&action=toggle`
* **Fader B stummschalten:**
  `GET http://127.0.0.1:14565/api/mute?channel=B&action=mute`
* **Stummschaltung aufheben:**
  `GET http://127.0.0.1:14565/api/mute?channel=music&action=unmute`

---

### C) Mikrofon-Stummschaltung / Husten-Taste (`/api/mic-mute`)
Globaler Mic-Mute (entspricht der physischen Cough-Mute Taste des GoXLR).

* **Mikrofon Mute umschalten:**
  `GET http://127.0.0.1:14565/api/mic-mute?action=toggle`
* **Explizit stummschalten:**
  `GET http://127.0.0.1:14565/api/mic-mute?muted=true`

---

### D) Bleep / Zensur-Taste (`/api/bleep`)
Aktiviert den GoXLR Zensur-Piepton und mutet das Mikrofon auf dem Gerät, während die Taste gedrückt wird.

* **Key Down (Taste gedrückt):**
  `GET http://127.0.0.1:14565/api/bleep?active=true`
* **Key Up (Taste losgelassen):**
  `GET http://127.0.0.1:14565/api/bleep?active=false`

---

### E) Routing Matrix umschalten (`/api/route`)
Verbindet oder trennt Audiowege der Routing-Tabelle mit einem Tastendruck.

* **Mikrofon auf Kopfhörer umschalten (Toggle):**
  `GET http://127.0.0.1:14565/api/route?channel=mic&bus=headphones&action=toggle`
* **Musik auf Stream-Mix aktivieren:**
  `GET http://127.0.0.1:14565/api/route?channel=music&bus=stream&gain=1.0`
* **Musik auf Stream-Mix deaktivieren:**
  `GET http://127.0.0.1:14565/api/route?channel=music&bus=stream&gain=0.0`
* **Bus-Namen:** `headphones`, `stream`, `lineout`, `chatmic`, `sampler`.

---

### F) Profil laden (`/api/profile`)
Wechselt das komplette Mixer-Setup auf ein gespeichertes Profil.

* **Profil "Gaming" laden:**
  `GET http://127.0.0.1:14565/api/profile?name=Gaming`
* **Profil "Stream" laden:**
  `GET http://127.0.0.1:14565/api/profile?name=Stream`

---

### G) Status-Abfrage (`/api/status`)
Gibt den gesamten Zustand des Mixers, Faderpositionen, Mute-Stati und Hardware-Infos als JSON zurück:
```
GET http://127.0.0.1:14565/api/status
```

---

## 3. WebSocket API

Für bidirektionale Integrationen und Echtzeit-Displays steht die WebSocket-Schnittstelle zur Verfügung:
```
ws://127.0.0.1:48500/ws
```
Alle Ereignisse (Faderbewegungen, Tastenanschläge, Metering) werden im JSON-Format in Echtzeit gestreamt.

