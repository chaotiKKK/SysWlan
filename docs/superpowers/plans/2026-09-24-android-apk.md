# SysWLANInfo Android APK Plan

**Goal:** Ship an installable, signable Android build of SysWLANInfo that fills every area Android can actually deliver and names every area it cannot — instead of inventing values.
**Architecture:** The MAUI Blazor app targets `net10.0-windows10.0.19041.0;net10.0-android`. Platform access sits behind small interfaces (`IPlatformActions`, `IPlatformInfo`, `IPlatformPermissions`, `IBackgroundMonitoring`), with a new `SysWlan.Android` adapter mirroring `SysWlan.Windows`. `SysWlan.Core` stays platform-free and carries the pure, testable mapping logic.
**Tech Stack:** .NET 10.0.401 (local `.tools`), MAUI 10.0.101, Android target API 36 / minimum API 26, JDK 21, Microsoft.Data.Sqlite, AndroidX through MAUI.
**Spec:** ../../android.md

## Global constraints
- The Windows build must not regress: tests plus the `win-x64` publish stay green.
- German UI, no fabricated values, no router writes, no root, no signing key copied into the repository.
- Windows-only facts (neighborhood table, process connections, event log) are labelled as unavailable on Android, with the reason.
- No silent gaps: a missing permission produces a visible notice and a button, never a substituted value.
- The APK is a sideload artifact: cleartext HTTP for the LAN router UI and `PACKAGE_USAGE_STATS` rule out a Play-Store release.

## Task 1: Toolchain, project structure, signing — done
- [x] `scripts/build-android.ps1` with `-Bootstrap`, `-Release`, `-CreateKeystore`, SDK/JDK detection (parameter, environment, `.tools`, usual install locations).
- [x] Keystore outside the repository (`%USERPROFILE%\.syswlan\`), passwords only via `env:` prefix, `*.keystore`/`*.jks` in `.gitignore`.
- [x] `src/SysWlan.Android` project (`net10.0-android`), app multi-targeted with TFM-conditional project references and Windows-only properties.
- [x] Android release deliberately without trimming and AOT (`AndroidLinkMode=None`, `PublishTrimmed=false`, `RunAOTCompilation=false`), documented size tradeoff.
- Acceptance: `scripts/build.ps1` stays green; Android debug and release APKs build.

## Task 2: Core additions (platform-free, testable) — done
- [x] `AndroidWifiMapping`: frequency → band/channel (2.4/5/6 GHz), RSSI → percent estimate, WiFi standard and security mapping, `0x`/`<unknown ssid>` cleanup, explicit permission/location warnings, connectionless Ethernet case left unknown.
- [x] `GatewayIdentityPolicy` plus `NetworkSnapshot.GatewayMacSource`: Windows uses the real gateway MAC, Android the accesspoint BSSID; `ProfileIdentity` and `RouterAccessPolicy` stay untouched, so session and credential rules keep working without being weakened.
- [x] `MonitorService.SetBackgrounded` and `BackgroundIntervalSeconds`: background time is excluded and logged, the user pause stays separate, the background interval is raised to 15 seconds.
- [x] `RouterSyslogDeviceParser` and `RouterSyslogDeviceFeed` plus a `Received` event on `SyslogReceiver`: the only device source Android has.
- Acceptance: new checks in `tests/SysWlan.Tests` (band/channel bounds, unknown stays unknown, BSSID identity passes the session policy, syslog device parsing and expiry, background pause really stops collecting); all existing checks stay green.

## Task 3: Android adapter `SysWlan.Android` — done
- [x] `AndroidCollector`: ConnectivityManager, NetworkCapabilities, LinkProperties (addresses, DNS, default route → gateway), guarded WifiInfo reads (RSSI, frequency, link speeds, standard, security type), local MAC privacy filtering.
- [x] Device-wide traffic via `NetworkStatsManager` with an explicit usage-access requirement and a clearly labelled own-app value as secondary.
- [x] `NetworkEventsRecorder` as the Android replacement for the Windows event log.
- [x] Permissions in the manifest: network/Wi-Fi, `NEARBY_WIFI_DEVICES` with `neverForLocation`, location capped at API 32, notification/foreground/`dataSync`/wakelock, `PACKAGE_USAGE_STATS`, `allowBackup=false`, `networkSecurityConfig`.
- Acceptance: on a device the collector delivers real Wi-Fi, address and traffic values; without usage access the traffic panel shows the requirement instead of a number.

## Task 4: Platform actions behind one interface — done
- [x] `IPlatformActions` replaces the direct `DesktopActions` injection; Windows implementation moved to `Platforms/Windows`.
- [x] Android router page: same-origin enforcement per navigation, download blocking, file/content access off, mixed content never, certificate errors cancel the session, one session at a time with cookie/web-storage purge, session ends on pause/error/stale/profile change.
- [x] Export (JSON and `.ics`) written to the app cache and handed to the Android share sheet; credentials stay in `SecureStorage`.
- Acceptance: router sign-in works in the WebView, foreign navigation is blocked, exports are shareable, credentials only after explicit choice.

## Task 5: Lifecycle and optional foreground service — done
- [x] `MainActivity.OnPause/OnResume` drive `MonitorService.SetBackgrounded`, so background time is not counted as measurement.
- [x] `MonitoringForegroundService` (`dataSync`) with notification channel, own vector icon, partial wakelock, notification permission request, start only from user action, timeout handling that logs the Android 15 limit instead of leaving a silent gap.
- Acceptance: service toggle visible, notification appears, monitoring continues with the screen off, system termination is visible and restartable.

## Task 6: Mobile UI and honest platform texts — done
- [x] `IPlatformInfo` supplies device label, platform label, footer, traffic source note, capability matrix and per-view notes; views no longer hardcode "Windows".
- [x] Settings section for permissions and background monitoring; background gap notice in the dashboard.
- [x] `mobile.css`: horizontal tab strip for phones, 44-px tap targets, stacked forms, own scrolling for wide tables and calendars.
- Acceptance: every one of the ten views is usable at 360–420 CSS pixels without Windows-specific claims.

## Task 7: Verification — partly done
- [x] 36 core checks pass, including 8 new Android/identity/syslog/background checks.
- [x] Windows regression: `scripts/build.ps1` green, live smoke test (window opens, fresh SQLite database with monitoring and network log entries).
- [x] Android debug and release builds; merged manifest checked with `aapt dump permissions`; signature via `apksigner verify`; package contents (wwwroot assets, notification drawable) confirmed.
- [ ] Device run: permission dialogs, real WLAN values against the system display, traffic before/after usage access, service toggle and notification, router sign-in and blocked foreign navigation, share-sheet export, CalDAV, rotation/back/process restart. No device was connected, so this stays open and is stated in the documentation.

## Task 8: Documentation — done
- [x] `docs/android.md` rewritten from roadmap to as-built state (permissions, usage access, foreground service and its 6-hour limit, cleartext decision and counter-measures, structurally unavailable Android facts, open items).
- [x] README: Android section, capability differences, build and signing commands, sideload instead of store, version 0.2.0.
- [x] `progress.md`, `task_plan.md`, `findings.md` updated; this plan document added.

## Execution notes
- The Android SDK at `C:\Users\HP\Android` and JDK 21 were reused, so no multi-gigabyte download was needed; only the `maui-android` workload was installed.
- `dotnet publish -r win-x64` had to become `-f net10.0-windows10.0.19041.0 -p:RuntimeIdentifierOverride=win-x64`, because a global RID reached the Android project during restore.
- ABI narrowing cannot be passed on the command line (global properties propagate to referenced projects); it belongs in the Android property group of the app project.
