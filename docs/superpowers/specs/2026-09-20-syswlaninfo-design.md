# SysWLANInfo – Architekturentwurf

Stand: 20.09.2026. Vorschlag zur Entscheidung; noch keine implementierte App.

## Ziel und Plattformen

Eine deutschsprachige Windows-App für das aktive Netzwerk und den vorgeschalteten Router. Gespeicherte Routerprofile werden beim Wiederverbinden erkannt und um neue Messungen und Konfigurationsstände ergänzt. Android folgt als eigene APK mit gemeinsamem Datenmodell und UI, aber plattformspezifischen Netzwerkzugriffen. „AAA“ wird als Anspruch an Bedienbarkeit, Gestaltung, Stabilität und reale Funktionen verstanden, nicht als bereits erreichte Qualitätszertifizierung.

## Festgestellter Ausgangspunkt

Der Projektordner enthält noch keine Anwendung. Der PC ist über Vodafone-CF26 mit 192.168.0.1 verbunden. Die HTTP-Startseite des Gateways antwortet mit ARRIS-Oberfläche, Firmware 01.05.063.15.EURO.PC20 und einem Modellindikator isModel6442. Das genaue Handelsmodell ist damit noch nicht verifiziert. Die abgerufene Seite weist keine gültige angemeldete Sitzung aus. Sie liefert bereits einige Statusinformationen, unter anderem DOCSIS Online und aktiviertes WLAN.

Der aktuelle Prozess ist nicht erhöht. Installiert sind Node.js und eine .NET-Laufzeit, aber kein .NET SDK. Für native Entwicklung müssen passende SDKs eingerichtet werden; geschützte Installationen nutzen ausschließlich die Elevation-Funktion der Desktop-App.

## Architekturvarianten

1. Empfehlung: .NET MAUI Blazor Hybrid, gemeinsames C#-Domänenmodell, Razor-UI, SQLite-Verlauf und getrennte Windows-/Android-Netzwerkadapter. Vorteil: eine Plattformfamilie für Windows und APK; Nachteil: umfangreichere initiale SDK-Einrichtung.
2. Electron mit React unter Windows, später Capacitor für Android. Vorteil: schnelle Windows-Entwicklung mit vorhandenem Node.js; Nachteil: zwei native Integrationswege und größerer Desktop-Ressourcenbedarf.
3. Windows-spezifische WPF-App mit später separat entwickelter Android-UI. Vorteil: direkter Windows-Fokus; Nachteil: mehr doppelte UI-Arbeit.

## Funktionsbereiche

### Dashboard und Diagnose

- Aktive Schnittstelle, SSID/BSSID soweit zugänglich, Gateway, lokale IPv4/IPv6, DNS, WLAN-Sicherheit, Frequenzband, Kanal, Signal und ausgehandelte Linkraten.
- Empfang/Senden aus Differenzen der lokalen Schnittstellenzähler; Zeitreihen, Sitzungsvolumen und dokumentierter Messzeitraum. Zählerwechsel, Reset, Standby und Netzwerkwechsel erzeugen keine negativen Raten.
- Gateway-Latenz, DNS-Diagnose und Verbindungsereignisse. Ein echter Internet-Speedtest ist eine separat gestartete Messung mit angezeigtem Datenverbrauch.
- Lokale TCP/UDP-Verbindungen und Prozesszuordnung, soweit Betriebssystemrechte reichen.
- Nachbartabelle als beobachtete Geräte; vollständige Clients, Gerätenamen und deren Traffic nur aus einer unterstützten Routerquelle. Nicht beobachtet bedeutet nicht offline.

### Routerprofile

- Profilidentität aus mehreren Merkmalen: Routerkennung/Seriennummer, falls verfügbar, Gateway-MAC, Netzwerkkennung und Schnittstellenkontext. IP oder SSID allein genügen nicht.
- Automatische Zuordnung bei eindeutiger Identität, sonst neues vorläufiges Profil. Mehrere Access Points können einem Router zugeordnet werden.
- Name, Notizen, Tags, Adaptertyp, erste/letzte Sichtung, Fähigkeiten, Konfigurationsstände und Messhistorie speichern.
- Wiederverbindung aktualisiert Zeitstempel und Messungen und dokumentiert Änderungen. Beobachtete Änderungen werden nicht automatisch zurückgeschrieben.
- Export/Import mit Schema-Version; Zugangsdaten sind ausgeschlossen. Aufbewahrung standardmäßig 30 Tage, konfigurierbar.

### Routerkonfiguration und Gäste-WLAN

- Adapterinterface für Erkennung, Status, Clients, WLAN, Gastnetz, Konfigurationslesung/-änderung und Logzugriff.
- Zuerst allgemeine Erkennung und der tatsächlich vorhandene Vodafone/ARRIS-Router. Weitere Routerfamilien werden durch eigene getestete Adapter ergänzt.
- Jede Fähigkeit meldet verfügbar, Anmeldung erforderlich, Betriebssystemberechtigung erforderlich, nicht unterstützt oder vorübergehend fehlgeschlagen.
- Gastnetz-Formular mit SSID, Sicherheitsmodus, Passwort und den tatsächlich unterstützten Optionen. Übernahme zeigt die konkrete Änderung und mögliche Verbindungsunterbrechung; anschließend Status erneut auslesen.
- Keine Behauptung universeller Gastnetz-, Konfigurations- oder Syslog-Unterstützung. Unbekannte Firmware erhält keinen spekulativen Schreibzugriff.

### Zugriff ohne wiederholte Anmeldung

Windows-Administratorrechte ersetzen keine Routeranmeldung. Die beobachtete Gateway-Seite hat keine gültige angemeldete Sitzung geliefert. Ohne bereitgestellten Zugang funktionieren nur lokale und öffentlich vom Router gelieferte Informationen.

Vorgeschlagener Vollfunktionsmodus: einmalige Anmeldung innerhalb der App; Zugangsdaten nur im Betriebssystem-Geheimnisspeicher, Sitzungen erneuern, soweit der Router das erlaubt. Keine Zugangsdaten in Logs, Exporten oder normalen Profildateien. Bestehende andere Browserprofile werden nicht nach Passwörtern durchsucht.

Alternative bei strikt keinerlei Passworteingabe: Monitoring ohne Routeranmeldung; gesperrte Konfigurations- und Gastnetzfunktionen werden mit ihrem tatsächlichen Verfügbarkeitsstatus angezeigt. Eine spätere berechtigte Kopplung bleibt möglich.

### Security, Developer und Logs

- Nachprüfbare Befunde zu WLAN-Sicherheit, HTTP-Verwaltungszugriff, geänderten Netzwerk-/DNS-Daten und veränderten Gerätebeobachtungen; jeder Befund nennt Quelle und Zeitpunkt.
- Windows-Netzwerkereignisse, App-Diagnoselog und Routerlogs getrennt kennzeichnen; Filter nach Quelle, Zeit, Schweregrad und Text; begrenzte Speicherung und Export.
- Syslog-Empfänger optional, standardmäßig deaktiviert. Erst nach Auswahl des Interfaces/Ports und Routerkonfiguration empfangen; eingehende Nachrichten als untrusted Text behandeln. Ein Empfänger allein aktiviert keinen Syslog-Versand im Router.
- Entwickleransicht mit redigierten Statusdaten, Adapterfähigkeiten, Messfehlern und Diagnoseexport; keine beliebigen Shellbefehle aus der UI.

## Oberfläche

Desktop-Dashboard mit Navigation für Übersicht, Geräte, Verbindungen, WLAN/Gastnetz, Profile, Sicherheit, Logs und Einstellungen. Oben aktives Profil und Erfassungsstatus; unten Zeitstempel und Quellen. Klare Diagramme, Tastaturbedienung, skalierbare Schrift und verständliche Leer-/Fehlerzustände. Kleine Android-Displays erhalten angepasste Navigation. Keine simulierten Livewerte im normalen Betrieb.

## Technische Grenzen und Fehlerbehandlung

Erfassung außerhalb des UI-Threads, Abbruch bei Profilwechsel, begrenzte parallele Abfragen und zunehmende Wartezeit nach Fehlern. Veraltete Daten sichtbar markieren. SQLite-Migrationen versionieren und Geheimnisse getrennt ablegen. Android nutzt seine eigenen Berechtigungs- und Hintergrundregeln; Windows-Funktionsumfang wird nicht ungeprüft für Android versprochen.

## Umsetzung und Nachweis

1. Windows-Grundgerüst, echte lokale Erfassung, Profile und Verlauf. Nachweis an diesem PC und mit Tests für Zähler-Reset, Profilwechsel und Persistenz.
2. Routeradapter und Zugriffsmodell, unterstützte Status- und Konfigurationsfunktionen. Tests gegen repräsentative Antworten und Fehlerfälle; reale Leseprüfung am Gateway. Routeränderungen nur anhand konkreter gewünschter Werte.
3. Geräte-/Verbindungsansicht, Security-Befunde, Ereignisse/Syslog, Exporte und Einstellungen. Tests für Redigierung, fehlerhafte Nachrichten und Aufbewahrung.
4. Windows-Paket, Starttest und dokumentierte Funktionsmatrix. Ein nicht signiertes lokales Paket wird entsprechend benannt; veröffentlichte Signatur und Store-Release sind eigene Schritte.
5. Spätere Android-APK: Plattformadapter, Berechtigungsabläufe, Smartphone-Layout und Prüfung auf einem realen Gerät.

## Noch zu entscheidender Punkt

Darf die App bei Bedarf einmalig den Routerzugang einrichten und sicher speichern, oder muss auch die Erstanmeldung vollständig ohne Eingabe auskommen? Die zweite Variante begrenzt den Funktionsumfang am aktuell beobachteten Router.

## Referenzen

- Microsoft .NET MAUI: https://learn.microsoft.com/en-us/dotnet/maui/?view=net-maui-10.0
- Vodafone Station Hilfe: https://www.vodafone.de/hilfe/router/station.html
- Lokale, ausschließlich lesende Erhebung: Windows-Netzwerkcmdlets und HTTP-Startseite des tatsächlich aktiven Gateways.
