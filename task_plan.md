# SysWLANInfo

## Goal
Develop a Windows and Android router and network management app with persistent profiles, live monitoring, security/developer diagnostics and honest platform limits.

## Next Step
Finish the device run by connecting the phone to a real WLAN: compare SSID, BSSID, band, channel, signal and link rate against the Android system display, and sign in to the router in the app's WebView (including blocked foreign navigation). Afterwards decide whether to enable trimming/AOT with generated JSON contracts.

### Phase 1: Environment and router discovery
**Status:** complete

### Phase 2: Architecture and capability specification
**Status:** complete

### Phase 3: Windows implementation and verification
**Status:** complete

### Phase 4: Packaging and Android portability
**Status:** complete
- Windows publish (`artifacts/windows`) and Android APK (`artifacts/android`) both build from scripts.
- Android adapter, platform actions, lifecycle pause, foreground service, permissions and mobile layout implemented.
- Manifest, signature and package contents verified; device run still pending.

### Phase 5: Device verification and hardening
**Status:** in progress (device run on 2026-09-25 against Android 16)
- Done on the device: install, permissions, all ten views, device-wide traffic against known load, foreground service (notification, background behaviour, logged pause), JSON and `.ics` export through the share sheet, syslog device source, rotation, process restart, persistence.
- Found and fixed on the device: debug APK aborted at start (fast deployment), invented location-switch warning, "dieses PCs" in platform-free text, settings page overflow, UTF-8 BOM in the Android export.
- Still open: real WLAN client values against the system display and router sign-in in the WebView (needs the phone connected to a real WLAN), CalDAV sync and the Android 15 `dataSync` limit.
- Then revisit trimming, AOT, single-ABI packaging and the AndroidX WebKit profile API.

## Constraints
- User authorizes development and administrative work; actual process is not elevated.
- Router authentication is separate from operating system privileges; no credentials in chat, logs or exports.
- Show unavailable capabilities explicitly; never fabricate router, device or traffic values.
- Never commit keystores or passwords; release signing keys stay outside the repository.
- Preserve other workspace changes.

## Errors Encountered
- `dotnet publish -r win-x64` broke once the app multi-targeted: the global RID reached the Android library and restore failed. Fixed with `-f` plus `RuntimeIdentifierOverride=win-x64` in `scripts/build.ps1`.
- Android release requires trimming when AOT is on. Trimming is off on purpose (reflection in JSON, SQLite and MAUI), so AOT is off as well; the APK is therefore large.
- `RuntimeIdentifiers` set on the command line propagates to referenced projects and breaks them; ABI narrowing must happen in the project file.
- HyperOS refused `adb install` with `INSTALL_FAILED_USER_RESTRICTED` until "USB debugging (Security settings)" was enabled, and still refused `adb shell input` (`INJECT_EVENTS`). On-screen work was therefore driven through the WebView DevTools protocol instead of input injection.
- The Debug APK aborted with `No assemblies found in …/files/.__override__/arm64-v8a` because .NET Android uses fast deployment in Debug. A sideloaded Debug APK needs `EmbedAssembliesIntoApk=true`.
- `adb push`/`adb shell` paths starting with `/` get rewritten by Git Bash; commands need `MSYS_NO_PATHCONV=1`.
- The Wi-Fi interface during the test was the phone's own hotspot (`wlan2`, default gateway of the attached PC), so no WLAN client values were available.
