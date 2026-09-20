# Geräteverlauf und offener Kalender Implementation Plan

> **Für agentische Ausführung:** REQUIRED SUB-SKILL: Diesen Plan Aufgabe für Aufgabe mit einer frischen Review-Grenze nach jeder Aufgabe ausführen.

**Goal:** Eine lokale, adaptive Kalenderansicht für beobachtete Netzwerkgeräte mit Anwesenheitsintervallen, belegbaren Aktivitätsdaten, Ereignissen, stabilen Farben, `.ics`-Export und optionaler App→CalDAV-Synchronisation liefern.

**Architecture:** Die bestehende Snapshot-Erfassung speist eine getrennte Geräte-Zeitachse in SQLite. Kalender-UI und iCalendar-Export lesen diese lokale Quelle; CalDAV synchronisiert sie einseitig über stabile UIDs. Weder Kalenderdarstellung noch Serververfügbarkeit dürfen den lokalen Erfassungspfad blockieren.

**Tech Stack:** .NET 10, MAUI Blazor Hybrid, `SysWlan.Core`, `SysWlan.Windows`, `Microsoft.Data.Sqlite`, Razor/Blazor UI, `HttpClient` und das bestehende ausführbare Testprojekt ohne neues Kalender- oder CalDAV-Paket.

**Spec:** `docs/superpowers/specs/2026-09-20-device-calendar-design.md`

## Global Constraints

- Keine Kalender- oder CalDAV-Abhängigkeit im Erfassungspfad; Monitoring und lokale UI funktionieren offline.
- Routerprofile und beobachtete Client-Geräte bleiben getrennte Domänenobjekte.
- Eine Beobachtung ist kein sicherer Online-/Offline-Nachweis; `Stale` darf nicht als aktuelle Anwesenheit gespeichert werden.
- Fremdgeräte-Aktivität bleibt `unavailable`, solange keine belegbare Routerquelle existiert; lokale PC-Zähler werden nur dem lokalen PC zugeordnet.
- Keine Einzeltermine pro Polling-Messung; nur zusammengeführte Intervalle und separate belegbare Events.
- Keine Zweiweg-Synchronisation und keine externen Kalenderänderungen in Messdaten übernehmen.
- Keine Geheimnisse in Logs, `.ics`, Exporten, Exceptions oder Diagnosezuständen.
- Bestehende Daten und unbeteiligte Workspace-Änderungen erhalten; SQLite-Migrationen sind monoton und versioniert.
- Keine neuen NuGet-Pakete ohne vorherigen Nachweis, dass eine vorhandene Projektabhängigkeit wiederverwendet werden kann.
- Erst Core-/Persistenztests, dann Adapter und UI; jede Aufgabe endet mit einem passenden Testlauf.
- Keine Git-Commits während der Ausführung ohne separate Nutzeranweisung.

---

## Datei- und Verantwortungsübersicht

| Einheit | Verantwortung |
|---|---|
| `src/SysWlan.Core/DeviceTimelineModels.cs` | Geräte-, Intervall-, Aktivitäts-, Ereignis- und Sync-Datentypen |
| `src/SysWlan.Core/DeviceIdentity.cs` | stabile Quellschlüssel, MAC-Normalisierung und Farbpalette |
| `src/SysWlan.Core/PresenceIntervalBuilder.cs` | zustandslos testbare Intervallübergänge und Lückentoleranz |
| `src/SysWlan.Core/DeviceTimelineService.cs` | Snapshot-Normalisierung, Store-Aufrufe und beobachtungsbezogene Events |
| `src/SysWlan.Core/Store.cs` | versionierte SQLite-Migrationen und atomare Kalender-Persistenz |
| `src/SysWlan.Core/Calendar/CalendarModels.cs` | UI-/Export-Read-Models ohne SQLite-Abhängigkeit |
| `src/SysWlan.Core/Calendar/CalendarQueryService.cs` | Bereichsabfragen für Woche, Tag und Monat |
| `src/SysWlan.Core/Calendar/IcsExporter.cs` | deterministische RFC-kompatible iCalendar-Erzeugung |
| `src/SysWlan.Core/Calendar/CalDavOptions.cs` | validierte, secret-freie Konfigurationsansicht |
| `src/SysWlan.Core/Calendar/CalDavConfigurationProvider.cs` | Datei-/Umgebungsvariablen-Auflösung mit Vorrangregeln |
| `src/SysWlan.Core/Calendar/CalDavClient.cs` | begrenzte CalDAV-HTTP-Operationen und Antwortprüfung |
| `src/SysWlan.Core/Calendar/CalendarSyncService.cs` | lokale Queue, stabile UIDs, Backoff und Status |
| `src/SysWlan.App/Components/Views/CalendarView.razor` | Kalendernavigation, Filter und Statusanzeige |
| `src/SysWlan.App/Components/Calendar/*.razor` | Woche, Tag, Monat und Legende als fokussierte UI-Einheiten |
| `src/SysWlan.App/Services/ThemeService.cs` | persistenter Hell-/Dunkel-/Systemmodus |
| `src/SysWlan.App/Components/Pages/Home.razor` | Kalender als Hauptnavigation einhängen |
| `src/SysWlan.App/wwwroot/app.css` | kontrastreiche Theme- und Kalenderstyles |
| `tests/SysWlan.Tests/Program.cs` | bestehender ausführbarer Test-Runner und neue Regressionstests |
| `README.md` | Bedienung, Grenzen, `.ics` und CalDAV-Konfiguration |

---

## Task 1: Geräteidentität, Datenmodelle und stabile Farben

**Files:**
- Create: `src/SysWlan.Core/DeviceTimelineModels.cs`
- Create: `src/SysWlan.Core/DeviceIdentity.cs`
- Test: `tests/SysWlan.Tests/Program.cs`

**Interfaces:**
- Consumes: vorhandene `DeviceInfo`- und `NetworkSnapshot`-Records aus `Models.cs`.
- Produces: `DeviceIdentity.SourceKey(DeviceInfo, NetworkSnapshot)`, `DeviceIdentity.AutomaticColor(string)`, `DeviceIdentity.IsValidColor(string)`, sowie die Kalender-Records für alle folgenden Tasks.

- [ ] **Step 1: Failing tests für Quellschlüssel und Farbstabilität schreiben**

```csharp
Check("device source key survives IP and name changes", () =>
{
    var first = new DeviceInfo("192.168.0.20", "AA-BB-CC-DD-EE-20", "Reachable", "Phone");
    var later = new DeviceInfo("192.168.0.88", "aa:bb:cc:dd:ee:20", "Reachable", "Renamed Phone");
    var snapshot = Sample(0, 0, 0);
    Require(DeviceIdentity.SourceKey(first, snapshot) == DeviceIdentity.SourceKey(later, snapshot), "MAC source changed");
});
Check("invalid MAC gets a provisional source and foreign activity is unavailable", () =>
{
    var device = new DeviceInfo("192.168.0.21", "", "Reachable", "Unknown");
    var source = DeviceIdentity.SourceKey(device, Sample(0, 0, 0));
    Require(source.StartsWith("provisional:") && DeviceIdentity.AutomaticColor(source).StartsWith("#"), "invalid source not provisional");
    Require(ActivityAvailability.Unavailable.ToString() == "Unavailable", "availability contract changed");
});
Check("automatic device color is deterministic", () =>
{
    Require(DeviceIdentity.AutomaticColor("mac:AABBCCDDEE20") == DeviceIdentity.AutomaticColor("mac:AABBCCDDEE20"), "color changed");
    Require(DeviceIdentity.IsValidColor("#73E3DC") && !DeviceIdentity.IsValidColor("red"), "color validation failed");
});
```

- [ ] **Step 2: Run only the existing executable test project to verify the new symbols fail**

Run: `.tools/dotnet/dotnet.exe run --project tests/SysWlan.Tests -c Release`  
Expected: compile failure naming the not-yet-defined `DeviceIdentity` or `ActivityAvailability` symbols.

- [ ] **Step 3: Add the minimal nullable-safe models and identity API**

Use these stable contracts:

```csharp
public enum ActivityAvailability { Unavailable, Available }
public enum DeviceEventType { FirstObserved, ProfileChanged, Absence, CollectionError, AdapterEvent }
public enum SyncStatus { Pending, Synced, Failed }

public sealed record DeviceProfile(
    string Id, string Name, string? ManualColor, string? Icon, bool IsVisible,
    DateTimeOffset FirstSeen, DateTimeOffset LastSeen);

public sealed record DeviceIdentityMember(
    string SourceKey, string DeviceProfileId, string? Mac, string SourceKind,
    DateTimeOffset FirstSeen, DateTimeOffset LastSeen);

public sealed record PresenceInterval(
    string Id, string SourceKey, DateTimeOffset Start, DateTimeOffset? End,
    string Quality, bool IsOpen);

public sealed record ActivityAggregate(
    string Id, string SourceKey, DateTimeOffset Start, DateTimeOffset End,
    double? ReceiveMbps, double? SendMbps, ActivityAvailability Availability, string Source);

public sealed record DeviceEvent(
    string Id, string SourceKey, DateTimeOffset Timestamp, DeviceEventType Type,
    string Severity, string Message, string Source);

public sealed record CalendarSyncRecord(
    string ObjectType, string ObjectId, string Uid, string ContentHash, string? ETag,
    SyncStatus Status, int Attempts, DateTimeOffset? NextAttemptAt, string? LastError);
```

`SourceKey` is `mac:<12 uppercase hex characters>` for a valid nonzero/nonbroadcast MAC. Otherwise use `provisional:<InterfaceId>|<IPAddress>`. The automatic palette must contain fixed, contrast-reviewed hex colors; select one by SHA-256 hash modulo palette length. `ManualColor` accepts only `#RRGGBB` and is never used as an HTML fragment.

- [ ] **Step 4: Run the tests and verify the identity contract passes**

Run: `.tools/dotnet/dotnet.exe run --project tests/SysWlan.Tests -c Release`  
Expected: all existing tests and the three new identity/color checks pass.

---

## Task 2: Pure Anwesenheits-Intervallbildung

**Files:**
- Create: `src/SysWlan.Core/PresenceIntervalBuilder.cs`
- Modify: `tests/SysWlan.Tests/Program.cs`

**Interfaces:**
- Consumes: a source key, observation timestamp, configured poll interval and quality string.
- Produces: `IntervalTransition Observe(string, DateTimeOffset, TimeSpan, string)` and `IntervalTransition CloseExpired(DateTimeOffset, TimeSpan)`.

- [ ] **Step 1: Write failing transition tests**

```csharp
Check("presence builder tolerates one missed poll and closes a longer gap", () =>
{
    var builder = new PresenceIntervalBuilder();
    var first = builder.Observe("mac:AABBCCDDEE20", start, TimeSpan.FromSeconds(5), "observed");
    var second = builder.Observe("mac:AABBCCDDEE20", start.AddSeconds(10), TimeSpan.FromSeconds(5), "observed");
    var closed = builder.CloseExpired(start.AddSeconds(26), TimeSpan.FromSeconds(5));
    Require(first.Opened.Length == 1 && second.Updated.Length == 1, "interval did not open/extend");
    Require(closed.Closed.Length == 1 && closed.Closed[0].End == start.AddSeconds(20), "long gap not closed at last observation");
});
Check("different source keys never share an interval", () =>
{
    var builder = new PresenceIntervalBuilder();
    builder.Observe("mac:A", start, TimeSpan.FromSeconds(5), "observed");
    builder.Observe("mac:B", start, TimeSpan.FromSeconds(5), "observed");
    Require(builder.OpenIntervals.Count == 2, "sources merged");
});
```

The chosen rule is exact: an observation extends an open interval when elapsed time from the last observation is `<= 2 * pollInterval`; `CloseExpired` closes an interval when elapsed time is `> 2 * pollInterval`. This tolerates one missing poll without fabricating a longer presence period.

- [ ] **Step 2: Run the tests and confirm the builder is missing**

Run: `.tools/dotnet/dotnet.exe run --project tests/SysWlan.Tests -c Release`  
Expected: compile failure for `PresenceIntervalBuilder` and `IntervalTransition`.

- [ ] **Step 3: Implement the in-memory builder**

`IntervalTransition` contains `PresenceInterval[] Opened`, `Updated`, `Closed` and `DeviceEvent[] Events`. Use a dictionary keyed by `SourceKey`; generate a deterministic interval ID from source key plus the first-observed UTC timestamp. Never generate an absence event until `CloseExpired` closes an interval.

- [ ] **Step 4: Run the focused and complete checks**

Run: `.tools/dotnet/dotnet.exe run --project tests/SysWlan.Tests -c Release`  
Expected: interval creation, extension, gap tolerance and closure checks pass with no existing regression.

---

## Task 3: SQLite-Migration und Kalender-Store

**Files:**
- Modify: `src/SysWlan.Core/Store.cs`
- Modify: `tests/SysWlan.Tests/Program.cs`

**Interfaces:**
- Consumes: the records from Tasks 1–2 and the existing version-1 router/profile tables.
- Produces: `Store.SaveDeviceObservation`, `Store.DeviceProfiles`, `Store.QueryCalendar`, `Store.MergeDeviceProfiles`, `Store.SplitDeviceSources`, `Store.PendingSync`, `Store.SaveSyncRecord` and `Store.PruneCalendarData`.

- [ ] **Step 1: Add migration tests against a version-1-compatible temporary database**

Create a temporary SQLite file, create the existing `profiles`, `samples`, `logs` and `settings` tables with `PRAGMA user_version=1`, insert one router profile and one sample, then instantiate `Store`. Assert that the old rows remain and the new tables exist. Also assert that reopening the same file does not rerun or reset the migration.

- [ ] **Step 2: Run the migration tests before changing Store**

Run: `.tools/dotnet/dotnet.exe run --project tests/SysWlan.Tests -c Release`  
Expected: migration assertions fail because the new schema and APIs do not exist.

- [ ] **Step 3: Replace the fixed user-version assignment with monotone migrations**

Keep existing version-1 tables intact. Read `PRAGMA user_version`; apply version 2 exactly once with these tables and indexes:

```sql
CREATE TABLE IF NOT EXISTS device_profiles (
  id TEXT PRIMARY KEY, name TEXT NOT NULL, manualColor TEXT,
  icon TEXT, isVisible INTEGER NOT NULL DEFAULT 1,
  firstSeen TEXT NOT NULL, lastSeen TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS device_identity_members (
  sourceKey TEXT PRIMARY KEY, deviceProfileId TEXT NOT NULL REFERENCES device_profiles(id),
  mac TEXT, sourceKind TEXT NOT NULL, firstSeen TEXT NOT NULL, lastSeen TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS presence_intervals (
  id TEXT PRIMARY KEY, sourceKey TEXT NOT NULL REFERENCES device_identity_members(sourceKey),
  startedAt TEXT NOT NULL, endedAt TEXT, quality TEXT NOT NULL, isOpen INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS activity_aggregates (
  id TEXT PRIMARY KEY, sourceKey TEXT NOT NULL REFERENCES device_identity_members(sourceKey),
  startedAt TEXT NOT NULL, endedAt TEXT NOT NULL, receiveMbps REAL, sendMbps REAL,
  availability TEXT NOT NULL, source TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS device_events (
  id TEXT PRIMARY KEY, sourceKey TEXT NOT NULL REFERENCES device_identity_members(sourceKey),
  timestamp TEXT NOT NULL, type TEXT NOT NULL, severity TEXT NOT NULL,
  message TEXT NOT NULL, source TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS calendar_sync (
  objectType TEXT NOT NULL, objectId TEXT NOT NULL, uid TEXT NOT NULL,
  contentHash TEXT NOT NULL, etag TEXT, status TEXT NOT NULL, attempts INTEGER NOT NULL DEFAULT 0,
  nextAttemptAt TEXT, lastError TEXT, PRIMARY KEY(objectType, objectId)
);
CREATE INDEX IF NOT EXISTS idx_presence_time ON presence_intervals(startedAt, endedAt);
CREATE INDEX IF NOT EXISTS idx_activity_time ON activity_aggregates(startedAt, endedAt);
CREATE INDEX IF NOT EXISTS idx_device_events_time ON device_events(timestamp);
```

Set `PRAGMA user_version=2` only after all version-2 statements succeed. Do not set a constant version on every startup. Store UTC timestamps with the existing invariant `O` format and keep all SQL parameterized.

- [ ] **Step 4: Implement atomic Store operations**

`SaveDeviceObservation` must upsert the source/member and profile, insert or update the interval, insert activity only when its availability is explicit, and insert event IDs with `ON CONFLICT DO NOTHING` inside one transaction. Query intervals by `sourceKey` and resolve the current group through `device_identity_members`, so merge/split changes do not rewrite historical source data.

`MergeDeviceProfiles(targetId, sourceIds)` moves identity members to the target without deleting intervals/events. `SplitDeviceSources(profileId, sourceKeys, newName, manualColor)` creates a new profile and moves only the selected source members. `QueryCalendar(fromUtc, toUtc, includeOpen)` returns joined intervals, activity and events; it must include an interval when it overlaps the range, not only when its start lies inside it.

- [ ] **Step 5: Add persistence tests for profile metadata, merge/split and range boundaries**

Assert that manual color and name survive reopening, merge/split preserves source rows and events, an interval crossing midnight appears in both relevant ranges, and `PruneCalendarData` never deletes a sync record whose source interval still exists.

- [ ] **Step 6: Run all Core/persistence tests**

Run: `.tools/dotnet/dotnet.exe run --project tests/SysWlan.Tests -c Release`  
Expected: all old tests plus migration, query, color and merge/split tests pass.

---

## Task 4: Snapshot-Integration und belegbare Aktivität

**Files:**
- Create: `src/SysWlan.Core/DeviceTimelineService.cs`
- Modify: `src/SysWlan.Core/MonitorService.cs`
- Modify: `src/SysWlan.App/MauiProgram.cs`
- Modify: `tests/SysWlan.Tests/Program.cs`

**Interfaces:**
- Consumes: `NetworkSnapshot`, `TrafficSample`, configured polling seconds, `Store` and `PresenceIntervalBuilder`.
- Produces: `DeviceTimelineService.ObserveSnapshot(NetworkSnapshot, TrafficSample, int)`, `DeviceTimelineService.ResetNetwork(DateTimeOffset, string)` and a nonblocking `Changed`/sync notification after the local transaction commits.

- [ ] **Step 1: Add a fake-collector test for current versus stale neighbors**

Construct a snapshot with one `Reachable` neighbor, one `Stale` neighbor, a valid `LocalMac` and a `TrafficSample`. Assert that only the reachable neighbor and local host receive presence observations, that the stale neighbor remains visible in the device list but gets no current interval, and that the local host receives an `ActivityAggregate` with `Available` when the traffic sample has rates.

- [ ] **Step 2: Run the new integration test before implementation**

Run: `.tools/dotnet/dotnet.exe run --project tests/SysWlan.Tests -c Release`  
Expected: compile failure for `DeviceTimelineService`.

- [ ] **Step 3: Implement snapshot normalization**

For each current neighbor whose `State` equals `Reachable` case-insensitively, compute a `SourceKey` and pass it to the builder/store. Do not treat `Stale` as current presence. When `NetworkSnapshot.LocalMac` is valid, create a separate `local:<normalized-mac>` source and store the `TrafficSample`; if rates are null, store `Availability.Unavailable` rather than zero. Deduplicate repeated MACs inside one snapshot before writing.

When the active router profile changes, call `ResetNetwork` to close old open intervals and create one `ProfileChanged` event. When collection fails, preserve local data and create a redacted `CollectionError` event only once per failure transition.

- [ ] **Step 4: Wire the service into the existing serial monitor loop**

Register one singleton `DeviceTimelineService` in `MauiProgram.CreateMauiApp`. Inject it into `MonitorService`; after each successful collector result and `RateTracker.Sample`, call `ObserveSnapshot`. Keep the existing `MonitorService` loop serial and invoke CalDAV signaling only after `Store` commits. Do not await network sync from the collector loop.

- [ ] **Step 5: Run regression and live-safe checks**

Run: `.tools/dotnet/dotnet.exe run --project tests/SysWlan.Tests -c Release`  
Expected: all tests pass; no live network request is needed for the new unit checks. Run the existing live mode only when explicitly validating the current Windows adapter.

---

## Task 5: Kalender-Read-Models und Bereichsabfragen

**Files:**
- Create: `src/SysWlan.Core/Calendar/CalendarModels.cs`
- Create: `src/SysWlan.Core/Calendar/CalendarQueryService.cs`
- Modify: `tests/SysWlan.Tests/Program.cs`

**Interfaces:**
- Consumes: `Store.QueryCalendar` and `TimeZoneInfo`.
- Produces: `CalendarQueryService.GetWeek`, `GetDay` and `GetMonth`, each returning a `CalendarSnapshot` with intervals, activity, events and device metadata.

- [ ] **Step 1: Add range and daylight-saving tests**

Use `Europe/Berlin` when available and UTC fallback only when the runtime does not expose that ID. Query an interval crossing local midnight and the autumn DST transition. Assert that UTC storage is unchanged, local labels show the correct date, and no event is duplicated by the range conversion.

- [ ] **Step 2: Implement UTC-boundary conversion**

`GetWeek(DateOnly anchor, TimeZoneInfo zone)` converts the local Monday 00:00 through the following Monday 00:00 to UTC and calls `Store.QueryCalendar`. `GetDay` uses one local day; `GetMonth` uses the first day through the first day of the next month. Keep open intervals in the internal result and mark them `IsLive`; never infer activity from a missing row.

- [ ] **Step 3: Run query tests**

Run: `.tools/dotnet/dotnet.exe run --project tests/SysWlan.Tests -c Release`  
Expected: week/day/month, overlap, UTC and DST tests pass.

---

## Task 6: Deterministischer iCalendar-Export

**Files:**
- Create: `src/SysWlan.Core/Calendar/IICalendarExporter.cs`
- Create: `src/SysWlan.Core/Calendar/IcsExporter.cs`
- Modify: `tests/SysWlan.Tests/Program.cs`

**Interfaces:**
- Consumes: a `CalendarSnapshot` containing closed presence intervals and events.
- Produces: `string IICalendarExporter.Export(CalendarSnapshot snapshot)` containing one VCALENDAR with escaped VEVENT components.

- [ ] **Step 1: Write exporter tests before implementation**

Assert all of the following in the raw output: `BEGIN:VCALENDAR`/`END:VCALENDAR`, stable UID for repeated export, changed end time with unchanged UID, separate event VEVENT, UTC `DTSTART`/`DTEND`, `CATEGORIES`, escaped commas/semicolons/backslashes/newlines, and absence of values such as `password=`, `Authorization:` and router cookies.

- [ ] **Step 2: Implement the minimal RFC-compatible writer without a new package**

Use CRLF line endings, deterministic ordering by start time then UID, and these UID formulas:

```text
presence:<sourceKey>:<intervalId>@syswlaninfo.local
 event:<sourceKey>:<eventId>@syswlaninfo.local
```

Escape text in one function before writing `SUMMARY`, `DESCRIPTION` and `CATEGORIES`. Export only closed intervals; include event objects separately. Include activity availability as text (`Aktivität: nicht verfügbar` or the measured source/rates), not as a fabricated numeric value. Add `X-APPLE-CALENDAR-COLOR` and `COLOR` only as optional metadata; the device name, category and description remain the portable fallback.

- [ ] **Step 3: Run exporter tests and inspect a generated fixture**

Run: `.tools/dotnet/dotnet.exe run --project tests/SysWlan.Tests -c Release`  
Expected: deterministic export and secret-redaction checks pass. Keep a small in-memory fixture in the test code; do not commit user data or live addresses.

---

## Task 7: CalDAV-Konfiguration und HTTP-Adapter

**Files:**
- Create: `src/SysWlan.Core/Calendar/CalDavOptions.cs`
- Create: `src/SysWlan.Core/Calendar/CalDavConfigurationProvider.cs`
- Create: `src/SysWlan.Core/Calendar/ICalendarSyncAdapter.cs`
- Create: `src/SysWlan.Core/Calendar/CalDavClient.cs`
- Modify: `tests/SysWlan.Tests/Program.cs`

**Interfaces:**
- `CalDavConfigurationProvider.Load(string filePath, IReadOnlyDictionary<string,string?> environment)` returns `CalDavOptions`.
- Environment keys are exact: `SYSWLAN_CALDAV_URL`, `SYSWLAN_CALDAV_USERNAME`, `SYSWLAN_CALDAV_PASSWORD`, `SYSWLAN_CALDAV_CALENDAR`.
- `ICalendarSyncAdapter.PushAsync(IReadOnlyList<CalendarSyncItem>, CancellationToken)` returns per-object statuses without throwing secrets.
- `CalDavClient` uses a supplied `HttpClient` so tests can inject a local fake handler.

- [ ] **Step 1: Test configuration precedence and redaction**

Write a JSON file with URL/user/password/calendar, provide only `SYSWLAN_CALDAV_PASSWORD` in the supplied environment dictionary, and assert that the environment password wins while other file fields remain. Assert that `CalDavOptions.ToSafeString()` contains URL/calendar but never password.

- [ ] **Step 2: Run configuration tests before implementation**

Run: `.tools/dotnet/dotnet.exe run --project tests/SysWlan.Tests -c Release`  
Expected: compile failure for `CalDavOptions` and `CalDavConfigurationProvider`.

- [ ] **Step 3: Implement options and provider**

Use `caldav.json` under `FileSystem.AppDataDirectory` from the App layer; the Core provider accepts a path and has no MAUI dependency. Treat the configured URL as the calendar collection URL. Default `CalendarName` to `Netzwerkgeräte`. Require absolute `http`/`https` URL, nonempty username/password and a nonempty calendar name before enabling sync. Read environment values individually so one environment override does not erase unrelated file settings.

- [ ] **Step 4: Implement bounded CalDAV operations with `HttpClient`**

Use only `PROPFIND` for collection validation/ETag discovery, `MKCALENDAR` when the configured collection is absent and the server permits it, `PUT` with `If-Match`/`If-None-Match` for stable UIDs, and `DELETE` for removed objects. Set explicit timeout and cancellation, cap response bodies, validate status codes and never include response bodies containing credentials in thrown messages. Escape XML request values and treat all server text as untrusted.

- [ ] **Step 5: Add fake-handler tests**

Cover collection validation, create/update/delete, 401 without password leakage, 404/5xx classification, cancellation timeout and ETag handling. Assert that a response body containing `secret123` does not appear in any returned safe error/status.

- [ ] **Step 6: Run adapter tests**

Run: `.tools/dotnet/dotnet.exe run --project tests/SysWlan.Tests -c Release`  
Expected: configuration precedence, HTTP method, ETag, error-classification and redaction tests pass.

---

## Task 8: Lokale Sync-Queue und exponentielles Backoff

**Files:**
- Create: `src/SysWlan.Core/Calendar/CalendarSyncService.cs`
- Modify: `src/SysWlan.Core/Store.cs`
- Modify: `tests/SysWlan.Tests/Program.cs`

**Interfaces:**
- Consumes: completed `CalendarSnapshot` items, `ICalendarSyncAdapter` and `Store` sync records.
- Produces: `CalendarSyncService.Signal()`, `Start()`, `Stop()`, `Status` and `SyncNowAsync(CancellationToken)`.

- [ ] **Step 1: Write queue/backoff tests with a fake adapter**

Use a fake adapter that fails twice, then succeeds. Assert that the first failure stores `Pending`/`Failed` with a future `NextAttemptAt`, the retry count increases, success stores `Synced`, and a repeated success does not create a duplicate operation. Use a fake clock or injected `Func<DateTimeOffset>` so the test never sleeps.

- [ ] **Step 2: Implement queue persistence and backoff**

Enqueue only closed presence intervals and events. Keep a stable object ID and UID across retries. Compute delay as `min(15 minutes, 2^attempt * 5 seconds)`; authentication errors remain visible and are not retried more often than the same bounded schedule. `Signal()` must be nonblocking; the worker reads persisted pending records and uses `CancellationToken`.

- [ ] **Step 3: Isolate external changes**

Never query external event contents into local device intervals. Store only adapter status, ETag/hash and redacted error metadata. The UI status must distinguish `lokal aktuell, CalDAV ausstehend`, authentication failure and server/network failure.

- [ ] **Step 4: Run sync tests**

Run: `.tools/dotnet/dotnet.exe run --project tests/SysWlan.Tests -c Release`  
Expected: offline buffering, bounded backoff, idempotency and failure classification pass.

---

## Task 9: MAUI-DI und Konfigurations-/Exportaktionen

**Files:**
- Modify: `src/SysWlan.App/MauiProgram.cs`
- Modify: `src/SysWlan.App/Services/DesktopActions.cs`
- Modify: `src/SysWlan.App/Components/Views/SettingsView.razor`
- Modify: `README.md`

**Interfaces:**
- Register `DeviceTimelineService`, `CalendarQueryService`, `IICalendarExporter`, `CalDavConfigurationProvider` and `CalendarSyncService` as singletons.
- Add `DesktopActions.ExportCalendarAsync(DateOnly, CalendarViewMode)` returning only the saved path.
- Add settings actions for writing `caldav.json`, testing configuration and triggering `SyncNowAsync` without displaying secrets.

- [ ] **Step 1: Add a settings integration test seam**

Keep file-writing and configuration resolution behind injectable methods so tests can use a temporary directory. Assert that a configuration status can be rendered without password text and that `.ics` export uses the local query service rather than live router data.

- [ ] **Step 2: Implement export action**

Write `.ics` under `Dokumente/SysWLANInfo` beside the existing JSON export, use a timestamped filename, and return the path. Do not place credentials in the file. Show success/failure through the existing `message` status pattern.

- [ ] **Step 3: Add CalDAV settings**

Expose URL, username, password, calendar name, enabled state, safe connection status and a manual sync button. Store the file beneath `FileSystem.AppDataDirectory`; mask the password after input and never bind it into diagnostic output. State that the configured URL is the calendar collection URL and that synchronization is App→CalDAV only.

- [ ] **Step 4: Run the complete test project**

Run: `.tools/dotnet/dotnet.exe run --project tests/SysWlan.Tests -c Release`  
Expected: all Core tests pass; UI-only actions are covered by their injectable Core seams.

---

## Task 10: Adaptive Kalender-UI und Darkmode

**Files:**
- Create: `src/SysWlan.App/Components/Views/CalendarView.razor`
- Create: `src/SysWlan.App/Components/Calendar/WeekCalendar.razor`
- Create: `src/SysWlan.App/Components/Calendar/DayTimeline.razor`
- Create: `src/SysWlan.App/Components/Calendar/MonthHeatmap.razor`
- Create: `src/SysWlan.App/Components/Calendar/CalendarLegend.razor`
- Create: `src/SysWlan.App/Services/ThemeService.cs`
- Modify: `src/SysWlan.App/Components/Pages/Home.razor`
- Modify: `src/SysWlan.App/Components/Views/SettingsView.razor`
- Modify: `src/SysWlan.App/wwwroot/app.css`

**Interfaces:**
- `CalendarView` injects `CalendarQueryService`, `Store`, `DesktopActions` and receives no fake/demo data.
- Child components receive typed `CalendarSnapshot` parameters and emit selected date/filter changes through `EventCallback`.
- `ThemeService.SetMode("system"|"light"|"dark")` persists the selection in the existing settings table.

- [ ] **Step 1: Add the calendar navigation item and empty-state view**

Add `("calendar", "Gerätekalender", "▦")` to the existing `tabs` array and a `case "calendar": <CalendarView />` branch. Render the correct empty state when no intervals exist, including the distinction between no observations and CalDAV being unavailable.

- [ ] **Step 2: Implement week navigation as the default**

Render seven day columns with time blocks, all visible device profiles, current/open intervals and text labels. Selecting a day switches the child view to `DayTimeline`; next/previous changes the anchor by seven days. Keep a horizontal scroll container at narrow widths rather than clipping device names.

- [ ] **Step 3: Implement day and month views**

`DayTimeline` renders hour labels, presence intervals, activity availability and event text. `MonthHeatmap` aggregates presence duration and event count per local day; it uses a neutral hatch/label for unavailable activity instead of treating it as zero. Month selection returns to the selected day/week.

- [ ] **Step 4: Add independent filters and device controls**

Provide checkboxes for Anwesenheit, Aktivität and Ereignisse plus device/status filters. Filter state changes only the local render; `.ics` and CalDAV always use the complete local dataset. Add a device details action for name, manual `#RRGGBB` color, visibility, reversible merge and split.

- [ ] **Step 5: Implement readable light/dark/system styles**

Refactor `app.css` colors into variables, add `[data-theme="dark"]` values with dark surfaces and light text, and keep `@media (prefers-color-scheme: dark)` for system mode. Add visible focus rings, `aria-label` text for every interval/event, status text alongside every color, `prefers-reduced-motion: reduce`, larger readable defaults and sufficient panel borders. Never use color as the only state indicator.

- [ ] **Step 6: Verify UI paths manually against real local state**

Start the native app using the existing development workflow, visit Übersicht, Geräte, Gerätekalender and Einstellungen, and verify empty, active, stale, error, darkmode, keyboard and narrow-window states. Do not insert mock measurements into the application database.

---

## Task 11: Regression, Dokumentation und Abnahme

**Files:**
- Modify: `README.md`
- Modify: `docs/android.md`
- Modify: `progress.md`
- Test: `tests/SysWlan.Tests/Program.cs`

- [ ] **Step 1: Document user-facing behavior**

Add the calendar to the feature table and document: observation versus certainty, `Stale`, local-PC-only activity, week/day/month views, stable/manual colors, reversible merge/split, `.ics` location, exact environment keys, `caldav.json` location, configured calendar collection URL, common calendar name `Netzwerkgeräte`, App→CalDAV direction, offline queue/backoff and secret exclusions.

- [ ] **Step 2: Document Android portability boundaries**

State that timeline models, SQLite data and exporters are portable, while Windows neighbor collection, local interface counters and Windows event logs require Android-specific adapters and permissions.

- [ ] **Step 3: Run the complete validation sequence**

Run in project root:

```powershell
.tools/dotnet/dotnet.exe run --project tests/SysWlan.Tests -c Release
powershell -File scripts/build.ps1
```

The first command must report zero failures. The second must complete the existing release test and Windows publish without new warnings. Run the live collector only as a separate, read-only validation:

```powershell
.tools/dotnet/dotnet.exe run --project tests/SysWlan.Tests -- --live
```

- [ ] **Step 4: Review the final workspace scope**

Inspect the diff and confirm that only the files listed in the task map, required generated build artifacts ignored by the repository, and documentation changes are present. Do not stage or commit until the user separately requests it.

## Handoff

Plan complete and saved to `docs/superpowers/plans/2026-09-20-device-calendar.md`. The implementation must be started only after choosing an execution mode and then proceed task-by-task with tests at each boundary.
