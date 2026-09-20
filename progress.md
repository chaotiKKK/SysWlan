# Progress

## 2026-09-20
- Read brainstorming and planning-with-files skills.
- Inspected workspace, tool availability and Windows network configuration.
- Classified task as architectural; preparing capability-aware design.
- No implementation or router settings changed.
- Read-only gateway inspection identified an ARRIS interface and limited public status; no authenticated session established.
- Saved a reviewed architecture proposal with platform alternatives, monitoring sources, profile identity, capability limits and implementation acceptance checks.
- Pending user decision on strict zero-input access versus one-time router pairing. No SDK installation or implementation started.
- User instructed 'weiter'. Proceeding with no-login monitoring by default and optional app-owned router pairing; not requesting credentials in chat.
- Created implementation plan; proceeding inline.
- Installed local .NET SDK 10.0.401. Initial restore/workload failed because inherited NuGet configuration had no sources; adding project-scoped official NuGet source.
- Installed maui-windows workload, scaffolded MAUI host, implemented Core/Windows libraries and nine German dashboard views.
- Upgraded SQLite bundle to 3.0.5 and Microsoft.Data.Sqlite to 10.0.12 after package vulnerability finding. Builds have no warnings.
- 14 core/persistence checks pass; live collector verified actual WLAN, gateway, public ARRIS firmware/DOCSIS, neighbors, process connections and Windows events.
- Native debug app launched PID 34872; task-only CDP 9237 enabled via process environment for UI QA. Screenshot artifacts/overview.png shows real populated data.
- Implemented optional OS-secret-store credentials, per-profile isolated router WebView2, navigation restriction, credential autofill without automatic submit, network-change window closure.
- Implemented DNS diagnostic, profile editor/history, JSON export, retention/interval settings and optional bounded UDP syslog receiver.
- Read requesting-code-review skill, delegated independent read-only review while testing native UI.
- Added the approved device timeline/calendar design and implementation plan; implementation now includes versioned timeline storage, interval aggregation, iCalendar export, CalDAV configuration/client queue, adaptive calendar UI and dark-mode/readability styles.
- Release tests and Windows publish pass after the calendar work; no authenticated router writes or real credential entry performed.
