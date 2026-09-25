# SysWLANInfo auf Android

Version 0.2.0 liefert die App erstmals als Android-APK aus: dieselbe Oberfläche, dieselbe SQLite-Zeitachse und derselbe Kalender wie unter Windows, aber mit Android-Erfassung. Dieses Dokument beschreibt, was die APK unter Android wirklich liefert, was sie ausdrücklich nicht liefert und wie sie gebaut und signiert wird.

Die APK ist für **Sideload** gedacht (direkt auf dem Gerät installieren). Sie ist **kein Play-Store-Kandidat**, solange zwei Eigenschaften so bleiben: freigegebenes Klartext-HTTP für die Routeroberfläche im LAN und der Systemzugriff „Nutzungszugriff“ für geräteweiten Datenverkehr. Beides ist unten begründet.

## Bauen

Voraussetzungen: .NET SDK 10.0.401 aus `.tools` (siehe README), ein Android-SDK und ein JDK 17 oder 21.

```powershell
# Workload, SDK und JDK bei Bedarf einrichten (arbeitet im Projekt: .tools\)
powershell -File scripts/build-android.ps1 -Bootstrap

# Debug-APK für Tests
powershell -File scripts/build-android.ps1

# Release-APK (ohne Keystore mit dem Debug-Schlüssel signiert)
powershell -File scripts/build-android.ps1 -Release
```

Ergebnis: `artifacts/android/de.syswlaninfo.app(-Signed).apk`. Die **Debug-APK ist eigenständig**: `EmbedAssembliesIntoApk=true` in der Projektdatei verhindert, dass .NET Android „Fast Deployment“ nutzt und die Assemblies erst per `dotnet build -t:Install` nachschiebt. Ohne diese Eigenschaft bricht eine per `adb install` verteilte Debug-APK beim Start mit `No assemblies found in …/files/.__override__/arm64-v8a` ab (im Gerätetest am 25.09.2026 genau so aufgetreten).

Signatur prüfen und installieren:

```powershell
& "$AndroidSdk\build-tools\35.0.0\apksigner.bat" verify --print-certs artifacts/android/de.syswlaninfo.app-Signed.apk
& "$AndroidSdk\platform-tools\adb.exe" install -r artifacts/android/de.syswlaninfo.app-Signed.apk
```

### Signierung

Ein Release-APK ohne eigenen Schlüssel signiert Android mit dem **Debug-Schlüssel**: installierbar zum Testen, aber ungeeignet für Updates, weil die Signatur wechselt.

```powershell
# Einmalig: Keystore außerhalb des Repositorys unter %USERPROFILE%\.syswlan\ anlegen
powershell -File scripts/build-android.ps1 -CreateKeystore
$env:SYSWLAN_ANDROID_KEYSTORE = "$env:USERPROFILE\.syswlan\syswlaninfo.keystore"
$env:SYSWLAN_ANDROID_KEYPASS = '<Passwort aus deinem Passwortspeicher>'
$env:SYSWLAN_ANDROID_STOREPASS = $env:SYSWLAN_ANDROID_KEYPASS
powershell -File scripts/build-android.ps1 -Release
```

Der Keystore liegt bewusst außerhalb des Projekts; `*.keystore` und `*.jks` sind zusätzlich in `.gitignore` gesperrt. Passwörter laufen ausschließlich über Umgebungsvariablen (`env:`-Präfix), damit sie nicht in Protokollen landen. Keystore und Passwort sichern: ohne sie sind keine Updates mit derselben Identität möglich.

### Größe und Trimmung

Das Release-APK ist groß (rund 65 MB), weil Trimmung und AOT bewusst ausgeschaltet sind: `System.Text.Json`, SQLite und MAUI nutzen Reflexion, die ungeprüft wegzuoptimieren unter Android zu Laufzeitfehlern führt. Getestet wurde ohne Trimmung. Wer eine kleinere APK braucht, setzt in `src/SysWlan.App/SysWlan.App.csproj` in der Android-Gruppe `<RuntimeIdentifiers>android-arm64</RuntimeIdentifiers>`; dann enthält das Paket nur diesen ABI-Block (etwa halb so groß). Trimmung und AOT sauber zu aktivieren braucht quellcodegenerierte JSON-Verträge und einen Gerätetest.

## Berechtigungen

| Berechtigung | Zweck | Ohne sie |
|---|---|---|
| `ACCESS_NETWORK_STATE`, `ACCESS_WIFI_STATE`, `INTERNET` | Netzwerkzustand, WLAN-Daten, Routerzugriff | Keine Messwerte bzw. keine Verbindung |
| `NEARBY_WIFI_DEVICES` (`neverForLocation`, ab Android 13) bzw. `ACCESS_FINE_LOCATION` (bis Android 12) | SSID, BSSID, Kanal, Signalstärke | Diese Werte bleiben „unbekannt“; Überblick, Geräte und Kalender funktionieren weiter |
| `PACKAGE_USAGE_STATS` (nur über Android-Einstellungen erteilbar) | Geräteweiter Datenverkehr über `NetworkStatsManager` | Kein Verbrauchswert; die App zeigt die Systemeinstellung „Nutzungszugriff“ mit Öffnen-Knopf statt einer Schätzung |
| `POST_NOTIFICATIONS`, `FOREGROUND_SERVICE`, `FOREGROUND_SERVICE_DATA_SYNC`, `WAKE_LOCK` | Optionaler Vordergrunddienst für Dauermonitoring | Monitoring nur bei sichtbarer App |

Berechtigungen werden nie umgangen und nie heimlich erteilt: Anfragen laufen ausschließlich über die Schaltflächen unter **Einstellungen → Plattformberechtigungen**, und der Zustand steht dort im Klartext. `android:allowBackup="false"` verhindert, dass Netzwerkdaten, Profile und der Zugangsspeicher in eine Cloud-Sicherung geraten.

Zusätzlich gilt: WLAN-Daten liegen auch mit Berechtigung nur vor, wenn der **Standortschalter** des Geräts aktiv ist. Ist er aus, verbirgt Android SSID und BSSID, und die App benennt genau das.

## Was Android liefert

| Bereich | Quelle | Grenze |
|---|---|---|
| Gateway, IP-Adressen, DNS | `ConnectivityManager`, `LinkProperties` | Netzwerkwechsel beginnt einen neuen Messabschnitt |
| WLAN: SSID, BSSID, Band, Kanal, Funkstandard, Linkrate | `NetworkCapabilities.TransportInfo`, `WifiManager` | Sicherheitstyp erst ab Android 12, Linkraten erst ab Android 10 verfügbar |
| Signalstärke | `WifiInfo` (RSSI) | Prozentwert ist aus dem RSSI geschätzt, ausgewiesen als Schätzung |
| Datenverkehr | `NetworkStatsManager` (geräteweit), `TrafficStats` (eigene App als Nebenwert) | Android bucht in Intervallen: die Summe ist eine Systemzählung, kein Momentanwert |
| Latenz zum Gateway | ICMP-Ping | Kein Internet-Speedtest |
| Ereignisse | `ConnectivityManager.NetworkCallback` | Ersetzt das Windows-Ereignisprotokoll |
| Geräte im Netz | Router-Syslog | Nur was der Router meldet (siehe unten) |
| Firmware, DOCSIS, Gastnetz | Öffentliche Routerseite bzw. Routeroberfläche in der App | Routeranmeldung; Klartext-HTTP nur zum eigenen Gateway |
| Kalender, Profile, Verlauf, Export | Gemeinsame Core-Datenbank und Razor-Ansichten | Wie unter Windows |

## Was Android strukturell nicht liefert

Diese Punkte sind nicht „noch nicht implementiert“, sondern dauerhaft gesperrt. Die App benennt sie in der jeweiligen Ansicht:

- **Nachbartabelle (ARP):** Seit Android 10 darf keine App mehr `/proc/net` lesen. Android kennt die Geräte im LAN daher nicht selbst. Geräte erscheinen nur, wenn der Router sie per Syslog meldet — über `DHCPACK`, `DHCPREQUEST` oder WLAN-Anmeldung. Die Liste ist sitzungsbezogen, läuft nach zwei Stunden ab, zeigt nur MAC-Adressen (plus Namen, falls der Router welche nennt) und ist ausdrücklich keine vollständige Clientliste.
- **Verbindungen und Prozesse anderer Apps:** Android erlaubt diese Einsicht nicht. Die Ansicht **Verbindungen** bleibt leer mit Begründung.
- **Windows-Ereignisprotokoll, WLAN-Profile, Nachbar-MACs des Gateways:** Diese Quellen existieren unter Android nicht. Die Routerkennung übernimmt stattdessen die BSSID (siehe unten).

## Routerkennung und Routerzugriff

Android gibt die **Gateway-MAC nicht heraus**. Profileschlüssel, Sitzungsregeln und Zugangsregeln hängen aber an einer Hardwarekennung. Deshalb nutzt die App unter Android die **BSSID des WLAN-Accesspoints** als Routerkennung und kennzeichnet das in Ansicht und Diagnoseausgabe:

- `GatewayMacSource = "gateway-mac"` unter Windows, `"ap-bssid"` unter Android.
- Die BSSID ist eine Zuordnungshilfe, kein Nachweis der Routeridentität: der LAN-Anschluss des Routers kann eine andere MAC-Adresse haben.
- Über Ethernet oder Mobilfunk gibt es keine BSSID. Dann entsteht ein vorläufiges Profil, und Routerbereich sowie Verbindungstest bleiben gesperrt, statt eine unsichere Kennung zu erfinden.

Für die Routeroberfläche gilt: Klartext-HTTP ist per Network-Security-Config erlaubt, weil Heimrouter im LAN praktisch immer HTTP sprechen und Android Klartext ab targetSdk 28 auch im WebView blockiert. Die Einschränkung liegt im Code: jede Routeraktion ist über `RouterAccessPolicy.IsSameOrigin` an die aktuelle Gateway-Origin gebunden, es wird genau eine Sitzung gleichzeitig geöffnet, Downloads sind gesperrt, Zertifikatfehler werden nicht umgangen (`OnReceivedSslError` bricht ab), und die Sitzung endet bei Pause, Fehler, veralteter Erfassung oder Profilwechsel. Android hat einen globalen Cookie-Speicher: Beim Öffnen und Schließen werden Cookies und WebStorage geleert, statt sie über Profile hinweg zu vermischen. Das ist eine bewusste Abweichung vom isolierten Windows-Sitzungsordner.

## Hintergrundbetrieb

Android hält den Prozess nach dem Verlassen der App weiter am Leben. Damit daraus keine erfundenen Messwerte werden:

- **Standard:** Beim Wechsel in den Hintergrund wird die Erfassung ausgesetzt, `RateTracker` zurückgesetzt und die Lücke protokolliert. Anwesenheitsintervalle schließen dort. In der Oberfläche steht dann der Hinweis, dass in dieser Zeit bewusst nicht gemessen wurde.
- **Optional:** Unter **Einstellungen → Dauerhaftes Monitoring** startet der Vordergrunddienst (`dataSync`) mit dauerhafter Benachrichtigung und partiellem WakeLock. Er läuft nur nach einer Nutzeraktion, weil Android Hintergrundstarts verbietet und Dauermonitoring eine bewusste Entscheidung ist.
- Android 15 beendet `dataSync`-Dienste nach etwa sechs Stunden pro Tag. Der Dienst protokolliert die Beendigung und kann in den Einstellungen neu gestartet werden; eine stille Lücke entsteht nicht. Im Hintergrund gilt ein Mindestintervall von 15 Sekunden.

## Unterschiede zur Windows-Version, die bleiben

- Routerfenster ist eine Seite in der App statt eines isolierten Fensters mit eigenem WebView2-Profil.
- Export (JSON und `.ics`) läuft über das Android-Teilen-Menü statt in den Ordner `Dokumente/SysWLANInfo`.
- Kein Windows-Ereignisprotokoll, keine Prozessverbindungen, keine Windows-Nachbartabelle.
- Kein Router-Schreibadapter und keine automatischen Routeränderungen — wie unter Windows.

## Gerätetest (25.09.2026)

Getestet auf einem Xiaomi POCO `zorn_global` (Modell 24117RK2CG) mit **Android 16 (API 36)**, arm64-v8a, 1440×3200 bei Dichte 480 (entspricht 480 CSS px Breite), Sicherheitspatch 2026-08-01, HyperOS V816, ohne Root. Das Telefon war zur Testzeit **selbst WLAN-Hotspot**, der WLAN-Client war getrennt — echte Client-Werte und die Routeranmeldung stehen deshalb weiterhin aus.

Vorgehen: Installation per `adb install`. HyperOS verweigert das bis „USB-Debugging (Sicherheitseinstellungen)“ aktiviert ist (`INSTALL_FAILED_USER_RESTRICTED`); `adb shell input` blieb auch danach gesperrt (`INJECT_EVENTS`). Die Oberfläche wurde deshalb über das DevTools-Protokoll der WebView gesteuert (`adb forward` auf `@webview_devtools_remote_<pid>`, dann echte Klicks und DOM-Auslesen), ergänzt durch `uiautomator dump` für Systemdialoge und `run-as` für Datenbank und Exporte.

Bestätigt:

- Start und alle zehn Ansichten ohne Absturz; Adresse, Gateway und DNS stimmen mit `dumpsys connectivity` überein.
- Ohne WLAN-Verbindung zeigt die App „—“ mit Begründung statt geschätzter Werte; der eigene App-Verbrauch aus `TrafficStats` läuft mit.
- Berechtigungsweg: `NEARBY_WIFI_DEVICES` über die App-Schaltfläche erteilt, die App erkennt das sofort; der Nutzungszugriff öffnet die Systemseite „App-Nutzungsdaten“.
- Geräteweiter Datenverkehr: 400 MB Download über den Hotspot des Telefons ergaben rund +460 MB im Zähler (Erwartung 400 MB plus gemessene Fensterdrift, Abweichung 7 %).
- Vordergrunddienst: läuft als `dataSync` mit Kanal `syswlaninfo-monitoring`, dauerhafter Benachrichtigung und WakeLock. Mit Dienst blieb der 6-Sekunden-Takt 124 s lang im Hintergrund erhalten; ohne Dienst zeigt der Verlauf eine protokollierte Lücke von 78 s.
- Export: JSON und `.ics` landen im App-Cache und gehen an das Android-Teilen-Menü; der Inhalt enthält keine Zugangsdaten.
- Gerätequelle Syslog: synthetische Routerzeilen erzeugten „Testgerät-Andi“ (Name, Adresse, MAC), „Unbekanntes Gerät“ (nur MAC) — und bei `00:00:00:00:00:00` bewusst kein Gerät.
- Verlauf, Profile und Exporte überstehen Update, Neustart und Prozessende; Quer- und Hochformat laufen ohne Seitenüberlauf.

Auf dem Gerät gefundene und behobene Fehler: nicht startende Debug-APK (Fast Deployment), erfundene Standortwarnung (der Schalterzustand wurde nie gelesen), „dieses PCs“ in einer plattformfreien Meldung, Überlauf der Einstellungsseite durch einen untrennbaren Pfad sowie ein UTF-8-BOM im Android-Export.

Noch offen, weil dafür ein echter WLAN-Client und Router nötig sind: WLAN-Werte gegen die Systemanzeige, Routeranmeldung im WebView samt blockierter Fremdnavigation, echte CalDAV-Synchronisation und die 6-Stunden-Grenze für `dataSync` ab Android 15.

## Offen (Stand dieser Version)

- **Tab-Beschriftungen am Telefon:** `app.css` blendet die Beschriftungen unter 900 px aus, `mobile.css` gestaltet sie unter 700 px nur kleiner — die Navigation bleibt dadurch icon-only.
- **WebView-Profilisolation:** Die AndroidX-WebKit-Profil-API wäre der saubere Ersatz für das Leeren des globalen Cookie-Speichers. Sie ist noch nicht genutzt.
- **Trimmung und AOT:** aus (siehe oben).
- **Direkter Ordner-Export** (Storage Access Framework) statt Teilen-Menü wäre möglich.

Offizielle Grundlagen: [WLAN-Berechtigungen ab Android 13](https://developer.android.com/develop/connectivity/wifi/wifi-permissions), [Netzwerksicherheitskonfiguration](https://developer.android.com/privacy-and-security/security-config), [Foreground-Service-Typen](https://developer.android.com/develop/background-work/services/fgs/service-types), [Android-Publish über die Kommandozeile](https://learn.microsoft.com/en-us/dotnet/maui/android/deployment/publish-cli).
