# Android-Roadmap

Die aktuelle Version baut ausschließlich Windows x64. Eine APK ist noch nicht erstellt.

1. Android-MAUI-Ziel mit separatem `INetworkCollector` ergänzen. Netzwerkdaten aus ConnectivityManager, LinkProperties und den jeweils erlaubten WLAN-APIs lesen; nicht das Windows-PowerShell-Skript verwenden.
2. Standort-/Nearby-Wi-Fi- und Netzwerkberechtigungen anhand der unterstützten Android-Versionen festlegen. Fehlende Berechtigungen als unbekannte Daten anzeigen.
3. Native Desktopaktionen hinter einer Plattform-Schnittstelle kapseln: Dateiexport, Routerfenster und Zugangsspeicherung benötigen Android-Implementierungen. Routercookies verschiedener Profile dürfen nicht zusammenfallen.
4. Hintergrundbetrieb ausdrücklich konzipieren. Ein unbegrenzt laufender Desktop-Pollingloop ist unter Android keine tragfähige Annahme; Vordergrunddienst und Benachrichtigung nur bei gewünschtem Dauermonitoring.
5. Smartphone-Navigation, Tastatur/Touch, Netzwerkwechsel, Wiederverbindung und App-Lebenszyklus auf echten Geräten testen. Die gemeinsame Kalenderdomäne, SQLite-Zeitachse und iCalendar-Exporter sind portabel; Windows-Nachbartabelle, lokale Interface-Zähler und Eventlog brauchen Android-spezifische Adapter und Berechtigungen. Windows-Prozessdaten und globale Gerätezähler bleiben dort als nicht unterstützt gekennzeichnet.
6. APK signieren, Updates und Aufbewahrung testen; keine privaten Signierschlüssel ins Repository übernehmen.

Direkte Gastnetzänderungen brauchen weiterhin eine unterstützte, authentifizierte Router-API. Die Plattformwahl beseitigt keine Routerzugriffsbeschränkungen.
