# SysWLANInfo – Geräteverlauf und offener Kalender

**Stand:** 20.09.2026  
**Status:** Entwurf nach gemeinsamer Designfreigabe; noch keine Implementierung.

## Ziel

SysWLANInfo erhält eine lokale, adaptive Kalenderansicht für beobachtete Netzwerkgeräte. Sie macht Anwesenheit, belegbare Aktivitätsdaten und Netzwerkereignisse über Zeit sichtbar. Die interne Zeitachse ist die alleinige Quelle; `.ics`-Export und CalDAV sind getrennte Adapter.

Die Standardansicht ist eine kontrastreiche Wochenübersicht. Ein ausgewählter Tag öffnet eine Stundenzeitleiste; die Monatsansicht zeigt langfristige Muster als Heatmap. Darkmode und Lesbarkeit sind gleichwertige Anforderungen, nicht nachträgliche Dekoration.

## Bestehender Kontext und Grenzen

Die Windows-Erfassung liefert aktuell Nachbargeräte aus der lokalen Beobachtung sowie lokale PC-Messwerte. `DeviceInfo` enthält Adresse, MAC, Zustand und optionalen Namen; die vorhandene SQLite-Store-/Monitor-Pipeline speichert derzeit Routerprofile, Traffic-Samples und Logs.

Die Nachbartabelle ist **keine vollständige Router-Clientliste**. Das Auftauchen eines Geräts belegt eine Beobachtung, nicht seine vollständige Anwesenheit, Offline-Zeit oder seinen Traffic. Datenverkehr anderer Geräte darf nur dann als Aktivität dargestellt werden, wenn ein unterstützter Routeradapter diese Daten tatsächlich liefert. Bis dahin wird für solche Geräte ausdrücklich „Aktivität nicht verfügbar“ angezeigt. Lokale PC-Trafficwerte bleiben als lokale Aktivität gekennzeichnet und werden nicht fremden Geräten zugerechnet.

Die Routerprofile der bestehenden App und die neuen Client-Geräteprofile sind getrennte Domänenobjekte. Eine Routerprofil-ID darf nicht als Geräte-ID wiederverwendet werden.

## Nutzerentscheidungen

- Alle beobachteten Geräte erscheinen standardmäßig im Kalender.
- Gerätefarben werden automatisch stabil vergeben und können manuell überschrieben werden.
- Identität basiert zunächst auf MAC-/Profilbindung; zufällige MAC-Adressen können als getrennte Profile erscheinen.
- Geräteprofile können reversibel zusammengeführt und wieder getrennt werden, ohne Rohdaten zu löschen.
- Es gibt einen gemeinsamen externen Kalender `Netzwerkgeräte`, keine standardmäßigen Einzelkalender pro Gerät.
- Synchronisation läuft standardmäßig nur **App → Kalender**.
- CalDAV wird bei Nichtverfügbarkeit lokal gepuffert und mit begrenztem exponentiellem Backoff wiederholt.
- CalDAV-Konfiguration kommt aus lokaler Konfigurationsdatei und Umgebungsvariablen; Umgebungsvariablen überschreiben gleichnamige Dateifelder.

## Architektur

### Datenfluss

1. `WindowsCollector` liefert unveränderte `NetworkSnapshot`-Momentaufnahmen.
2. Ein neuer Timeline-Dienst normalisiert beobachtete Geräte und überführt jede Snapshot-Beobachtung in eine Zeitachsen-Operation.
3. Anwesenheitsintervalle werden pro Geräteprofil gebildet. Eine Lücke von höchstens zwei konfigurierten Abfrageintervallen wird toleriert; eine längere Lücke beendet das Intervall.
4. Ereignisse werden aus belegbaren Zustandsänderungen erzeugt: erstmals beobachtet, Profil-/Netzwerkwechsel, längere Abwesenheit, Erfassungsfehler und verfügbare Router-/Adapterereignisse.
5. Die UI fragt aggregierte Kalenderdaten für Woche, Tag und Monat ab. Rohmessungen bleiben für bestehende Diagramme und Nachprüfung erhalten.
6. Der iCalendar-Adapter liest abgeschlossene Zeitachsenobjekte und erzeugt `.ics`.
7. Der CalDAV-Adapter verwendet dieselben stabilen UIDs und synchronisiert den gemeinsamen Kalender idempotent.

Die Erfassung darf nicht vom Kalender- oder CalDAV-Server abhängen. Ein blockierter Server darf weder Polling noch die lokale Anzeige stoppen.

### Domänenobjekte

Die neuen Objekte werden unabhängig von den bestehenden Routerprofilen modelliert:

- **DeviceProfile:** interne ID, zugrunde liegende Identitätsmitglieder, Anzeigename, MAC-/Quellinformationen, manuelle Farbe, optionales Symbol, Sichtbarkeit, erster/letzter Nachweis.
- **DeviceIdentityMember:** einzelne beobachtete MAC-/Profil-ID mit Quelle und Gültigkeitszeitraum. So bleiben zufällige MAC-Adressen und spätere Zusammenführungen nachvollziehbar.
- **PresenceInterval:** Quellprofil, Start, Ende oder offen, Beobachtungsquelle, Qualitätsstatus und Zusammenführungsstatus.
- **ActivityAggregate:** Zeitfenster, Geräte-/Quellprofil, Messwert oder Intensitätsklasse, Quelle und Verfügbarkeitsstatus. Fehlende Aktivitätsdaten sind `unavailable`, nicht null oder 0.
- **DeviceEvent:** stabile ID, Zeitpunkt, Typ, Schweregrad, betroffene Quelle/Gruppe, belegbare Beschreibung und Quelle.
- **CalendarSyncRecord:** Objekt-ID, stabile externe UID, Adapter, letzter Hash/Stand, letzter Erfolg, Status, Versuchszähler und redigierte Fehlermeldung.

Rohmessungen werden nicht durch Kalenderaggregation ersetzt. Die bestehenden Aufbewahrungseinstellungen gelten weiter; zusammengeführte Intervalle und Sync-Status dürfen nicht vor ihrer Referenzquelle gelöscht werden.

### Identität, Zusammenführung und Trennung

Eine MAC-/Profil-ID erhält eine stabile Gerätequelle. Adresse, DHCP-Name oder IP-Wechsel ändern diese Quelle nicht. Unbekannte Geräte erhalten einen neutralen Namen.

Bei zufälligen MAC-Adressen kann der Nutzer mehrere Quellen unter einem Alias zusammenführen. Historische Intervalle und Ereignisse behalten ihre Quellreferenz und werden in der Gruppenansicht gemeinsam dargestellt. Beim Trennen werden sie wieder nach ihren ursprünglichen Quellen getrennt; keine Rohdaten werden überschrieben. Externe UIDs bleiben an Quelle plus Zeitachsenobjekt gebunden, damit eine spätere Korrektur über stabile Updates bzw. Löschmarkierungen synchronisiert werden kann.

Automatische Zusammenführung wird nicht aus bloßer Ähnlichkeit von Name, IP oder Zeitpunkt vorgenommen.

## Kalendersemantik und UI

### Ansichten

- **Woche:** sieben Tage mit Zeitsegmenten, Gerätezeilen bzw. Gerätebalken, Anwesenheit und Ereignisindikatoren.
- **Tag:** Stundenzeitleiste mit Intervallen, Aktivitätsstatus, Ereignissen und Messlücken.
- **Monat:** Heatmap/Übersicht für Anwesenheitsdauer und Ereignisdichte; keine Scheingenauigkeit bei fehlender Aktivität.
- **Filter:** Anwesenheit, Aktivität, Ereignisse sowie Gerät, Status und Zeitraum sind einzeln ein-/ausblendbar. Filter verändern nur die Anzeige, nicht den vollständigen Export.

Ein offenes, aktuell laufendes Anwesenheitsintervall ist intern sichtbar. Für externe Kalender wird es erst nach dem Ende des Intervalls als endgültiger Termin synchronisiert, um bei jedem Polling keine Terminflut zu erzeugen. Die interne Ansicht kann den laufenden Endpunkt bis zur nächsten Beobachtung visualisieren.

### Farb- und Lesbarkeitsregeln

- Die automatische Grundfarbe wird deterministisch aus der stabilen Gerätequelle gewählt und bleibt über Ansichten und Tage gleich.
- Eine manuelle Farbe wird am Geräteprofil gespeichert und überschreibt nur die automatische Farbe.
- Anwesenheit nutzt die Gerätefarbe; Aktivitätsintensität wird zusätzlich über eine klar beschriftete Intensitätsstufe bzw. begrenzte Deckkraft dargestellt.
- Ereignisse verwenden Symbole und Text: neu/verbunden, Wechsel/Warnung, Fehler/längere Abwesenheit.
- Farbe ist nie die einzige Informationsquelle. Gerätename, Statuslabels und Ereignistext bleiben sichtbar.
- Darkmode verwendet dunkle Flächen, helle Schrift, klar getrennte Panelgrenzen, größere Typografie und kontrastgeprüfte Gerätefarben.
- Tastaturfokus, reduzierte Bewegung, Skalierung und Screenreader-Beschriftungen werden für Kalenderzellen, Filter und Legende vorgesehen.

## iCalendar- und CalDAV-Adapter

### iCalendar

Der Export enthält standardmäßig alle beobachteten Geräte und abgeschlossene Anwesenheitsintervalle sowie separate Ereignisobjekte. Jede Kalenderkomponente enthält mindestens:

- stabile `UID`,
- Start und Ende in eindeutigem UTC-/Zeitzonenformat,
- Gerätename und Ereignistyp im Titel,
- Beschreibung mit Quelle, Beobachtungsqualität und Aktivitätsverfügbarkeit,
- Kategorien für Gerät und Typ,
- standardisierte Farbmetadaten, soweit vom Format/Client unterstützt.

Die UID eines Intervalls hängt von Quellprofil und Intervallidentität ab, nicht von dessen aktuellem Ende. Eine Änderung des geschlossenen Intervalls aktualisiert denselben Termin; Profilzusammenführungen erzeugen keine stillen Datenverluste. Zugangsdaten, Cookies, Router-HTML und unredigierte Geheimnisse sind ausgeschlossen.

### CalDAV

CalDAV nutzt einen gemeinsamen Kalender namens `Netzwerkgeräte`. Die App ist die führende Quelle; externe Änderungen werden nicht als Messdaten importiert. Der erste Umfang ist App → CalDAV mit Create/Update/Delete anhand stabiler UIDs. Wenn ein Zielclient Farben nicht unterstützt, bleiben Kategorien, Titel und Beschreibung als Fallback erhalten.

Die Konfiguration wird aus einer lokalen Datei im App-Konfigurationsbereich und Umgebungsvariablen gelesen. Für die Umsetzung werden dokumentierte Schlüssel vorgesehen, mindestens für Server-URL, Benutzername, Passwort und Kalendername; Umgebungsvariablen überschreiben Dateifelder einzeln. Die Datei wird nur im Benutzerkontext abgelegt und nicht exportiert. Passwortwerte dürfen nie in Logs, Fehlermeldungen, UI-Diagnose, `.ics`-Dateien oder Sync-Status erscheinen.

Bei Offline- oder Serverfehlern:

- lokale Intervalle und eine ausstehende Sync-Operation bleiben erhalten,
- Netzwerk-, Authentifizierungs- und Serverfehler werden getrennt klassifiziert,
- Wiederholung erfolgt mit exponentiellem Backoff und begrenztem Maximalintervall,
- die UI zeigt „lokal aktuell, CalDAV ausstehend“,
- bereits synchronisierte Objekte bleiben erhalten,
- erfolgreicher Abgleich ist idempotent und setzt den Status zurück.

## Speicherung und Migration

Die SQLite-Erweiterung erhält versionierte Tabellen für Geräteprofile, Identitätsmitglieder, Anwesenheitsintervalle, Aktivitätsaggregate, Geräteereignisse und Kalender-Sync-Status. Die Migration muss die vorhandene `user_version` korrekt erhöhen und darf sie nicht bei jedem Start auf einen festen Wert zurücksetzen. Bestehende Routerprofile, Samples, Logs und Einstellungen bleiben lesbar.

Zeitpunkte werden intern eindeutig (UTC/`DateTimeOffset`) gespeichert. Darstellung und Export berücksichtigen lokale Zeitzone sowie Sommer-/Winterzeit ohne doppelte oder ausgelassene lokale Zeitpunkte.

## Fehler- und Sicherheitsregeln

- Fehlende Quelle oder fehlende Aktivitätsmessung wird sichtbar als nicht verfügbar markiert.
- Stale-/Abwesenheitszustände dürfen nicht als sicher offline bezeichnet werden.
- Ungültige MAC-/Profilwerte erzeugen einen vorläufigen Quellschlüssel statt eine falsche Geräteidentität.
- Externe CalDAV-Inhalte werden als untrusted behandelt; HTML/ICS-Inhalte dürfen keine UI- oder Log-Injektion verursachen.
- Netzwerkfehler beenden keine lokale Erfassung.
- Sync-Logs werden redigiert und enthalten keine Passwort- oder Cookiewerte.
- Keine automatische Zweiweg-Synchronisation und keine Änderung von Routereinstellungen im Rahmen dieser Funktion.

## Tests und Abnahmekriterien

### Core und Persistenz

- Beobachtungen öffnen, verlängern und schließen Intervalle korrekt.
- Lücken bis zu zwei Intervallen werden toleriert; längere Lücken erzeugen ein neues Intervall.
- Netzwerk-/Profilwechsel und Zähler-/Erfassungsfehler erzeugen nachvollziehbare Events.
- Umbenennen, IP-Wechsel, manuelle Farbe und Profilzusammenführung/-trennung erhalten die erwarteten Daten.
- Migration von einer bestehenden Datenbankversion bewahrt Routerprofile, Samples, Logs und Einstellungen.
- UTC, lokale Zeitzone und Sommer-/Winterzeit werden ohne Intervallverschiebung verarbeitet.

### Export und Synchronisation

- `.ics` enthält alle Geräte, stabile UIDs, korrekte Start-/Endzeiten, Kategorien und keine Secrets.
- Wiederholter Export ist deterministisch; geänderte Intervalle aktualisieren bestehende UIDs.
- CalDAV Create/Update/Delete ist idempotent.
- Offline-Puffer, Backoff, Wiederaufnahme und Authentifizierungsfehler werden getrennt geprüft.
- Umgebungsvariablen überschreiben Dateifelder und werden nicht in Diagnoseausgaben geleakt.

### UI und Regression

- Woche öffnet Tagdetails; Monat zeigt aggregierte Muster.
- Filter für Anwesenheit, Aktivität und Ereignisse wirken unabhängig.
- Darkmode, Kontrast, Textlabels, Tastaturfokus, reduzierte Bewegung und responsive Layouts werden geprüft.
- Bestehende Core-/Persistence-Checks, Release-Build und Live-Collector-Prüfung bleiben erfolgreich.

## Nicht Bestandteil des ersten Umfangs

- Vollständige Router-Client- oder Fremdgeräte-Trafficdaten ohne unterstützte Quelle.
- App-weite Behauptung, dass jede Beobachtung Online-/Offline-Zustand beweist.
- Zweiweg-CalDAV oder Rückimport externer Termine als Messdaten.
- Ein Kalender pro Gerät als Standard.
- Automatische Profilzusammenführung bei zufälligen MAC-Adressen.
- Per-Polling erzeugte Einzeltermine für jede Rohmessung.
- Routerkonfigurationsänderungen oder automatische Login-Umgehung.
