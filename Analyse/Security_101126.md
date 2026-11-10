# NEXT : XLR — Sicherheitsanalyse & Threat Model
**Datum:** 10. November 2026  
**Analysierte Version:** v0.1.5-alpha (Commit: `ae703bf`)  
**Autor:** Antigravity (AppSec & System Security)

---

## 1. Sicherheitsphilosophie & Gesamteinstufung

NEXT : XLR verarbeitet lokale Audiodaten und steuert hardwarenahe USB- und Windows-Betriebssystem-Schnittstellen. Die Software arbeitet zu 100 % lokal ohne Cloud-Zwang, Telemetrie-Tracker oder externe Benutzerkonten.

**Gesamtrisiko-Einstufung**: **NIEDRIG bis MITTEL**  
Das Basissystem ist durch die Trennung von Benutzeroberfläche und Rust-Daemon architektonisch solide und speichersicher aufgestellt. Durch die kürzlich hinzugefügten REST-/WebSocket-Schnittstellen auf Port 48500 und das tiefe COM-Interop im Windows-Audiosystem existieren jedoch spezifische Angriffsflächen, die gezielt gehärtet werden müssen.

---

## 2. Detaillierte Analyse der Angriffsflächen (Attack Surfaces)

```
                       ┌─────────────────────────┐
                       │  Web-Browser (Surfen)   │
                       └────────────┬────────────┘
                                    │ (DNS Rebinding / CSRF) ?
                                    ▼
┌──────────────┐  Named Pipe  ┌───────────────┐  CORS *   ┌────────────────┐
│ NextXLR.exe  │◄────────────►│ mixforged.exe │◄─────────►│  Stream Deck   │
│ (WinUI 3 UI) │              │ (Port 48500)  │           │ (Elgato Tool)  │
└──────────────┘              └───────┬───────┘           └────────────────┘
                                      │
                                      ▼
                        ┌───────────────────────────┐
                        │ Windows Audio / COM / USB │
                        └───────────────────────────┘
```

---

### 2.1 Lokaler Web- und WebSocket-Server (`127.0.0.1:48500`)

Mit Version 0.1.4/0.1.5 wurde in `mixforged` ein HTTP- und WebSocket-Server integriert, um Elgato Stream Deck Aktionen und Web-Panels zu bedienen.

#### Befunde & Risiken:
1. **CORS Wildcard (`Access-Control-Allow-Origin: *`)**:
   * *Problem*: Derzeit antwortet `mixforged` mit `ACCESS_CONTROL_ALLOW_ORIGIN: *` auf alle HTTP-Anfragen.
   * *Bedrohung*: Besucht ein Benutzer eine bösartige Webseite im Browser (z. B. Chrome oder Edge), kann ein JavaScript-Script auf dieser Seite Anfragen an `http://127.0.0.1:48500/api/fader` oder `/api/mute` senden oder das Mikrofon stummschalten/entstummen (*Cross-Site Request Forgery / Localhost Probing*).
   * *Schweregrad*: **MITTEL (Medium)**.
2. **Fehlende Authentifizierung / Token-Schutz**:
   * *Problem*: Die Endpunkte `/api/*` verlangen keinen API-Schlüssel oder Nonce-Token.
   * *Bedrohung*: Jedes lokale Programm oder Script, das im Benutzerkontext läuft, kann uneingeschränkt Lautstärken ändern oder den Zensur-Piepton auslösen.
   * *Schweregrad*: **NIEDRIG (Low)** (Lokale Prozesse auf demselben Benutzeraccount haben ohnehin ähnliche Rechte, aber im Zusammenspiel mit Browser-Sandboxen relevant).
3. **Bindung auf `127.0.0.1`**:
   * *Positiver Befund*: Der Server bindet strikt an `127.0.0.1` (Localhost) und lauscht **nicht** auf `0.0.0.0`. Dadurch ist der Port aus dem lokalen Netzwerk (LAN/WLAN) standardmäßig nicht erreichbar.

---

### 2.2 IPC Named Pipe (`\\.\pipe\mixforge`)

Die Kommunikation zwischen der WinUI-Benutzeroberfläche und dem Daemon erfolgt über eine Windows Named Pipe.

#### Befunde & Risiken:
1. **Access Control Lists (ACLs) & User-Boundary**:
   * *Positiver Befund*: Named Pipes unter Windows laufen standardmäßig im Windows Security Context. Unter `mixforged` wird die Named Pipe im Sicherheitskontext des angemeldeten Benutzers geöffnet. Andere Benutzer auf einem Mehrbenutzersystem können nicht ohne Weiteres lauschen.
2. **Pipe Squatting / Hijacking**:
   * *Analyse*: Wenn `mixforged` beendet wird, könnte theoretisch ein bösartiges Programm denselben Pipenamen anlegen, bevor der echte Daemon startet.
   * *Schutz*: `PipeTransport.cs` prüft die Verbindung und `mixforged` startet im Normalfall vor dem UI über den Windows-Autostart.

---

### 2.3 COM-Interop, P/Invoke & Undokumentierte WinRT-APIs

In `WindowsAppAudioRouter.cs` werden native Windows COM-Vtables direkt angesprochen.

#### Befunde & Risiken:
1. **Verwendung undokumentierter Klassen (`Windows.Media.Internal.AudioPolicyConfig`)**:
   * *Analyse*: Die Klasse ist nicht öffentlich im Windows SDK spezifiziert. Vtable-Änderungen durch künftige Windows-Updates (wie beim Übergang von Windows 10 zu 11 geschehen) können theoretisch zu `AccessViolationExceptions` führen.
   * *Schutzmaßnahmen*: Die Implementierung verwendet strikt defensive `try/catch`-Blöcke und prüft HRESULT-Rückgabewerte (`0x00000000 == S_OK`), bevor Zeiger dereferenziert werden.
2. **Speichersicherheit bei HSTRING-Handling**:
   * *Positiver Befund*: Jeder via `WindowsCreateString` allozierte HSTRING-Zeiger wird in `finally`-Blöcken über `WindowsDeleteString` freigegeben. Es entstehen keine Speicherlecks im unmanaged Heap.
3. **COM-Lebenszyklusverwaltung**:
   * *Positiver Befund*: Session-Pointer (`pSession`) aus `IAudioSessionEnumeratorNative` werden nach dem Auslesen der Eigenschaften explizit via `Marshal.Release(pSession)` freigegeben.

---

### 2.4 Windows Registry & Privilegien-Management

* **Berechtigungsebene (UAC)**:
  * NextXLR läuft standardmäßig als regulärer Benutzerprozess (**Keine Administratorrechte erforderlich**).
  * Lediglich für die Treiberinstallation (`DriverInstaller.cs`) oder die Bereinigung alter TC-Helicon-Dienste in `HKLM` wird über den `InstallerForm` eine UAC-Erhöhung angefordert.
* **Registry-Schreibzugriffe**:
  * Schreibzugriffe für das App-Routing erfolgen ausschließlich unter `HKCU\Software\Microsoft\Multimedia\Audio\DefaultEndpoint`.
  * Ein unberechtigter Systemeingriff über Registry-Injections ist ausgeschlossen, da die Schlüssel auf den aktuellen Benutzer beschränkt sind.

---

### 2.5 VST3 Plugin Hosting (`VstPluginService`)

* **Gefährdung**: Externe VST3-Plugins von Drittanbietern können fehlerhaften C++-Code, Speicherüberläufe oder gar bösartigen Code enthalten.
* **Architektur-Maßnahme**: 
  * Audio-Plugins dürfen niemals direkt auf dem Echtzeit-Audiothread crashen.
  * In NEXT : XLR ist für externe Plugins eine Isolierung (Sandbox-Host) vorgesehen, sodass ein Absturz eines VST-Plugins die GoXLR-Kernfunktionen nicht lahmlegt.

---

### 2.6 Code-Signing & Supply Chain Security

1. **Authenticode-Zertifikat**:
   * Sämtliche verteilten Binärdateien (`NextXLR.exe`, `NextXLR.dll`, `mixforged.exe`, `mixforge.exe`, `NextXLR-Setup-*.exe`) werden mit einem SHA256-Codesignatur-Zertifikat signiert (`CN=plysystems`).
   * Dies verhindert SmartScreen-Blockaden und schützt vor unbemerkter Manipulation durch Dritte.
2. **Auto-Updater-Sicherheit**:
   * **Transportverschlüsselung**: Der In-App-Updater kommuniziert ausschließlich über HTTPS (`https://api.github.com/...`).
   * **Download-Ziel**: Der Installer wird in `%TEMP%\NextXLR-Setup-Update.exe` abgelegt.
   * **Empfehlung**: Der Updater sollte vor dem Ausführen die digitale Signatur des heruntergeladenen Installers via Win32 `WinVerifyTrust` überprüfen, um Man-in-the-Middle-Angriffe oder manipulierte Assets zu 100 % auszuschließen.

---

### 2.7 Datenschutz & Telemetrie

* **100 % Offline**: NEXT : XLR übermittelt keinerlei Telemetrie- oder Nutzungsdaten an die Entwickler oder Dritte.
* **Crash-Reports**: Absturzberichte werden lokal unter `%LOCALAPPDATA%\NextXLR\crash_reports\` gespeichert.
  * Enthalten: Zeitstempel, OS-Version, .NET Runtime, Stacktrace.
  * Enthalten **keine** Audioaufnahmen, Passwörter oder persönliche Dateiinhalte.

---

## 3. Risikomatrix (Übersicht)

| ID | Angriffsvektor / Bereich | Bedrohung | Severity | Gegenmaßnahme vorhanden? |
|---|---|---|---|---|
| **SEC-01** | Port 48500 (HTTP API) | Lokale Browser-Webseiten steuern Mixer via CORS | **Mittel** | ⚠️ Nein (`CORS: *`). Empfehlung: Token oder Origin-Filter. |
| **SEC-02** | Named Pipe `\\.\pipe\mixforge` | Pipe Hijacking durch Schadsoftware | **Niedrig** | ✅ Ja (Windows Benutzer-ACLs schützen die Pipe). |
| **SEC-03** | COM Vtable Aufrufe | Crash durch Windows 11 Build-Änderung | **Niedrig** | ✅ Ja (Defensives HRESULT-Handling & Fallback). |
| **SEC-04** | Auto-Updater Download | Man-in-the-Middle / Manipulation | **Niedrig** | ⚠️ Teilweise (HTTPS aktiv; Signaturprüfung empfohlen). |
| **SEC-05** | VST3 Audio-Plugins | Instabile Plugins crashen Audio | **Mittel** | ✅ Ja (Host-Kapselung & Exception-Shielding). |
| **SEC-06** | Thesycon Treiber API | Buffer Overflow in Vendor Requests | **Sehr niedrig** | ✅ Ja (Strikte Pufferlängen in Rust USB-Crate). |

---

## 4. Konkrete Härtungsempfehlungen (Hardening Checklist)

### Priorität 1: API-Absicherung (Port 48500)
1. **Origin-Header Prüfung**: In `crates/daemon/src/main.rs` sollte das CORS-Middleware eingehende `Origin`-Header einschränken. Anfragen aus typischen Webbrowsern (`http://localhost:*` oder `https://*`) sollten abgewiesen werden, es sei denn, sie stammen von einer explizit freigegebenen Stream-Deck-URL.
2. **Lokales API-Token**: Erzeugung eines zufälligen Schlüssels beim ersten Start in `%LOCALAPPDATA%\NextXLR\api_secret.key`. Stream Deck Plugins oder Tools authentifizieren sich über den Header `Authorization: Bearer <token>`.

### Priorität 2: Auto-Updater Härtung
1. **Authenticode-Validierung vor Installer-Start**: Vor dem Start von `NextXLR-Setup-Update.exe` sollte die App prüfen, ob die Datei gültig mit dem `CN=plysystems`-Zertifikat signiert ist:
   ```csharp
   X509Certificate basicCert = X509Certificate.CreateFromSignedFile(tempExe);
   if (!basicCert.Subject.Contains("CN=plysystems")) { throw new SecurityException("Untrusted Update!"); }
   ```

### Priorität 3: Rust-Speichersicherheit
1. **Rust Bounds Checking**: In `mixforge-usb` alle Vendor-Request-Puffer mit explizitem Slicing absichern, um Lese-/Schreibzugriffe über Puffergrenzen hinaus kategorisch unmöglich zu machen.

---

## 5. Fazit

NEXT : XLR verfügt bereits über eine überdurchschnittlich sichere Basis, die insbesondere durch den Verzicht auf Cloud-Telemetrie und die Nutzung von speichersicherem Rust im Audio-Backend besticht. Durch die Umsetzung der empfohlenen Origin-/Token-Validierung für die neue Port-48500-API wird das System auch gegen neuartige Angriffsvektoren (wie Browser-basiertes Localhost-CSRF) optimal geschützt.