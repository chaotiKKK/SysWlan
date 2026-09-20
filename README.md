# SysWLANInfo

Lokale Windows-Netzwerkzentrale mit echten Messwerten, gespeicherten Routerprofilen und integrierter Routerverwaltung. Version 0.1.0 ist eine erste nutzbare Windows-Version; kein universeller Router-Schreibadapter und noch keine Android-APK.

## Starten

Nach dem Build `Start-SysWLANInfo.cmd` doppelklicken oder `artifacts/windows/SysWlan.App.exe` ausführen. Den gesamten Ordner `artifacts/windows` zusammenhalten, nicht nur die EXE kopieren. Das Paket enthält .NET und die Windows App SDK-Laufzeit. Microsoft Edge WebView2 muss installiert sein; auf dem Entwicklungsgerät wurde es erkannt.

Die App startet als normaler Benutzer. Monitoring benötigt keine Routeranmeldung. Es gibt keinen Cloud-Dienst und keinen automatisch geöffneten Netzwerkserver. Nur der optional eingeschaltete Syslog-Empfänger lauscht auf der gewählten lokalen Adresse.

## Funktionsumfang

| Bereich | Implementiert | Grenzen |
|---|---|---|
| Übersicht | Aktives Gateway, SSID, IP/DNS, Band, Kanal, Signal, Linkrate, Gateway-Latenz | Linkrate ist kein Internet-Speedtest |
| Datenverkehr | Empfang/Senden des PCs, Verlauf, beobachtete Profilmengen | Nur laufend erfasste Zeiträume; kein gesamter Routertraffic |
| Geräte | Nachbartabelle, Gruppierung nach MAC, Suche | Unvollständig; „Stale“ ist kein Offline-Nachweis |
| Gerätekalender | Woche/Tag/Monat, Anwesenheitsintervalle, Ereignisse, lokale Aktivität, stabile/manuelle Farben, Darkmode | Beobachtung ist kein sicherer Online-/Offline-Nachweis; Fremdgeräte-Aktivität bleibt ohne Routerquelle nicht verfügbar |
| Verbindungen | TCP/UDP-Endpunkte mit PID/Prozess, Filter | Systemweite PC-Momentaufnahme; keine Prozess-Trafficmengen |
| Profile | Automatische Wiedererkennung, Name/Notizen, Verlauf, persistente SQLite-Daten | Gateway-MAC ist eine Zuordnungshilfe, keine kryptografische Identität |
| WLAN/Gastnetz | Originale Routeroberfläche im isolierten Fenster | Anmeldung erforderlich; Funktionen hängen von Firmware ab |
| Routerstatus | Öffentliche ARRIS/Vodafone-Statusfelder und Firmware | Andere Router nur grundlegende Erkennung; keine automatische Konfigurationsänderung |
| Sicherheit | Beobachtete WLAN-/HTTP-/Signalbefunde mit Quellen | Kein vollständiges Audit, kein CVE-Abgleich |
| Ereignisse | Windows-WLAN-Ereignisse, App-/Netzwerkereignisse, Filter | Windows-Protokoll muss lesbar sein |
| Syslog | Optionaler UDP-Empfänger auf IP/Port, Redaktion, Begrenzung | Router muss Versand unterstützen; UDP ohne Absenderauthentifizierung |
| Entwickler | DNS-Prüfung, redigierter Diagnosezustand, Funktionsmatrix | Kein beliebiger Shellzugriff aus der UI |
| Einstellungen | Abfrageintervall, Aufbewahrung, JSON-Export | Kein Profilimport; Export enthält begrenzte letzte Messpunkte |

## Routerzugang

Unter **WLAN & Gastnetz** kannst du einen einzelnen manuellen Verbindungstest mit eingegebenem Benutzername und Passwort starten. Pro Klick wird genau ein Versuch ausgeführt; Wiederholung erfolgt nur durch einen neuen Klick, mit Mindestabstand und Abbruchmöglichkeit. MAUI SecureStorage legt Zugangsdaten nur ab, wenn du ausdrücklich „Zugang sicher speichern“ wählst. Die Routeroberfläche setzt keine Zugangsdaten automatisch ein; Anmeldung und Änderungen erfolgen dort manuell. Ein separater lokaler Generator erzeugt starke Passwörter zum manuellen Copy & Paste in den Router, ohne automatische Übertragung oder Speicherung.

Pro Routerprofil wird ein eigener WebView2-Sitzungsordner verwendet. Routerfenster schließen bei Netzwerkadressänderungen, pausierter/fehlgeschlagener oder veralteter Erfassung. HTTP/HTTPS-Adresse und Port dürfen während der Sitzung nicht wechseln. Der gewählte Transport wird nicht still herabgestuft; Zertifikatfehler werden nicht ignoriert. HTTP ist unverschlüsselt. Eine gespeicherte Anmeldung hebt die Sitzungsablaufregeln des Routers nicht auf.

Zugänge entfernen löscht das gespeicherte Passwort, nicht eine bereits am Router laufende Sitzung. Dafür im Router **Abmelden** wählen. Der Verbindungstest speichert und protokolliert eingegebene Passwörter nicht. Downloads aus der integrierten Oberfläche sind aktuell gesperrt; für Router-Konfigurationsdateien die Herstelleroberfläche im eigenen Browser verwenden.

## Daten und Messung

SQLite und isolierte Routersitzungen liegen unter dem von MAUI verwendeten lokalen Benutzer-Appdatenordner. Die Datenbank heißt `syswlaninfo.db`. Messwerte werden aus Differenzen der Windows-Schnittstellenzähler berechnet; Netzwerkwechsel, Reset, Standby und Pause beginnen einen neuen Messabschnitt. SSID oder Gateway-IP allein identifizieren keinen Router. Ohne Gateway-MAC entsteht ein vorläufiges Profil.

Standard: 5 Sekunden Pause zwischen Abfragen, zuzüglich Erfassungszeit; Routerstatus alle 60 Sekunden; Messwerte/Logs 30 Tage. Konfigurierbar: 3–60 Sekunden und 1–365 Tage. Logs maximal 50.000 Zeilen. Syslog maximal 50 Nachrichten/s und 8 KiB pro Nachricht; ausgeschaltet nach Appstart. Typische Zugangsdatenfelder in Logs werden redigiert, unbekannter Freitext kann weiterhin sensible Angaben enthalten.

JSON-Exporte landen in `Dokumente/SysWLANInfo` und enthalten Profile, bis zu 2.000 letzte Messpunkte pro Profil und 1.000 letzte Logs. Sie enthalten IP/MAC-Adressen und Notizen. Zugangsdaten, Browsercookies und Router-HTML sind ausgeschlossen. Kalender können als `.ics` in denselben Ordner exportiert werden; der Export enthält zusammengeführte Anwesenheitsintervalle und Ereignisse, nicht jede Rohmessung.

## Gerätekalender und CalDAV

Der Gerätekalender verwendet eine lokale Zeitachse. Eine Beobachtung öffnet ein Intervall; bis zu eine fehlende Abfrage wird toleriert, längere Lücken schließen das Intervall. Alle beobachteten Geräte erscheinen standardmäßig. Farben bleiben an die Gerätequelle gebunden und können pro Profil manuell als `#RRGGBB` festgelegt werden. Profile mit zufälligen MAC-Adressen können später reversibel zusammengeführt oder getrennt werden.

Die Standardansicht ist eine kontrastreiche Wochenansicht mit Tagesdetails und Monatsübersicht. Anwesenheit, Ereignisse und Aktivität sind getrennt filterbar; Darkmode und Textlabels verhindern, dass Farbe allein Bedeutung trägt. Aktivität fremder Geräte wird nicht erfunden: Ohne unterstützte Routerquelle steht dort „nicht verfügbar“.

Optional synchronisiert die App den gemeinsamen CalDAV-Kalender `Netzwerkgeräte` ausschließlich von der App zum Kalender. Die lokale Ansicht bleibt ohne Server verfügbar; bei Ausfall puffert die App und wiederholt mit Backoff. Die Konfiguration liegt in `caldav.json` im lokalen Appdatenordner. Unterstützte Umgebungsvariablen sind `SYSWLAN_CALDAV_URL`, `SYSWLAN_CALDAV_USERNAME`, `SYSWLAN_CALDAV_PASSWORD` und `SYSWLAN_CALDAV_CALENDAR`; Umgebungsvariablen überschreiben gleichnamige Dateifelder. Passwörter erscheinen nie in Logs, Exporten oder Diagnoseausgaben.

## Entwickeln und prüfen

Voraussetzung: Windows 10 ab Build 17763 oder Windows 11, x64, WebView2. Entwicklung mit lokalem .NET SDK 10.0.401 und MAUI Windows.

```powershell
# Einmalige lokale Toolchain-Einrichtung und Build
powershell -File scripts/build.ps1 -Bootstrap

# Wiederholbarer Test- und Release-Build
powershell -File scripts/build.ps1

# Kernprüfungen plus lesende Erhebung am aktuellen Netzwerk
.tools/dotnet/dotnet.exe run --project tests/SysWlan.Tests -- --live
```

`src/SysWlan.Core` enthält Datenmodell, Speicherung, Parser und Monitoring. `src/SysWlan.Windows` kapselt Windows-Erfassung mit einem eingebetteten festen PowerShell-Skript. `src/SysWlan.App` enthält MAUI, Razor-Ansichten und native Routerfenster. Das Skript erhält keine nutzerdefinierten Shellbefehle. Das Release startet keinen Debugport; ein solcher wurde ausschließlich für die lokale UI-Prüfung über die Prozessumgebung aktiviert.

## Späteres Android-Ziel

Das gemeinsame Datenmodell, die Zeitachse, Parser und große Teile der Razor-Oberfläche können übernommen werden. Android braucht einen eigenen Netzwerkadapter, Berechtigungsabläufe, sicheren Sitzungsspeicher und Tests auf realen Geräten. PC-weite Prozessverbindungen und Windows-Ereignisprotokolle sind dort nicht gleichwertig verfügbar. Details: [Android-Roadmap](docs/android.md).

Offizielle Grundlagen: [.NET MAUI](https://learn.microsoft.com/en-us/dotnet/maui/), [Windows-Veröffentlichung](https://learn.microsoft.com/en-us/dotnet/maui/windows/deployment/publish-unpackaged-cli), [Vodafone Station](https://www.vodafone.de/hilfe/router/station.html).
