param(
    [switch]$Bootstrap,
    [switch]$Release,
    [switch]$CreateKeystore,
    [string]$AndroidSdk,
    [string]$JavaHome
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$sdk = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$app = Join-Path $projectRoot 'src\SysWlan.App\SysWlan.App.csproj'
$artifacts = Join-Path $projectRoot 'artifacts\android'

# Reihenfolge: Parameter, Umgebungsvariablen, lokaler Bootstrap-Ordner, übliche Installationsorte.
function Resolve-AndroidSdk {
    if ($AndroidSdk) { return $AndroidSdk }
    foreach ($candidate in @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT, (Join-Path $projectRoot '.tools\android-sdk'), "$env:LOCALAPPDATA\Android\Sdk", "$env:USERPROFILE\Android")) {
        if ($candidate -and (Test-Path -LiteralPath (Join-Path $candidate 'platform-tools'))) { return (Resolve-Path -LiteralPath $candidate).Path }
    }
    return $null
}

function Resolve-JavaHome {
    if ($JavaHome) { return $JavaHome }
    foreach ($candidate in @($env:JAVA_HOME, (Join-Path $projectRoot '.tools\jdk'))) {
        if ($candidate -and (Test-Path -LiteralPath (Join-Path $candidate 'bin\java.exe'))) { return (Resolve-Path -LiteralPath $candidate).Path }
    }
    return $null
}

function Get-Keytool($java) {
    $tool = Join-Path $java 'bin\keytool.exe'
    if (!(Test-Path -LiteralPath $tool)) { throw 'keytool.exe wurde im JDK nicht gefunden.' }
    return $tool
}

if (!(Test-Path -LiteralPath $sdk)) { throw 'Zuerst scripts\build.ps1 -Bootstrap ausführen: die lokale .NET-SDK fehlt.' }

if ($CreateKeystore) {
    $java = Resolve-JavaHome
    if (!$java) { throw 'Für den Keystore wird ein JDK benötigt: -JavaHome oder JAVA_HOME setzen.' }
    $store = Join-Path $env:USERPROFILE '.syswlan\syswlaninfo.keystore'
    New-Item -ItemType Directory -Force -Path (Split-Path $store -Parent) | Out-Null
    if (Test-Path -LiteralPath $store) { throw "Es existiert bereits ein Keystore unter $store. Signierschlüssel niemals überschreiben; für Updates denselben Schlüssel verwenden." }
    & (Get-Keytool $java) -genkeypair -v -keystore $store -alias syswlaninfo -keyalg RSA -keysize 2048 -validity 10000
    if ($LASTEXITCODE) { throw 'Keystore konnte nicht erstellt werden.' }
    Write-Output "Keystore erstellt: $store"
    Write-Output 'Sicherung anlegen und Passwörter ausschließlich über Umgebungsvariablen setzen:'
    Write-Output '  $env:SYSWLAN_ANDROID_KEYPASS und $env:SYSWLAN_ANDROID_STOREPASS'
    Write-Output 'Der Keystore liegt bewusst außerhalb des Repositorys und gehört nie in die Versionsverwaltung.'
    return
}

Push-Location $projectRoot
try {
    $hasAndroidWorkload = (& $sdk workload list | Select-String 'maui-android')
    if ($Bootstrap -or !$hasAndroidWorkload) {
        & $sdk workload install maui-android --skip-manifest-update --source https://api.nuget.org/v3/index.json
        if ($LASTEXITCODE) { throw 'maui-android-Workload konnte nicht installiert werden.' }
    }

    $androidHome = Resolve-AndroidSdk
    $java = Resolve-JavaHome
    if ($Bootstrap -and (!$androidHome -or !$java)) {
        Write-Output 'Android-SDK und/oder JDK fehlen: InstallAndroidDependencies lädt sie in .tools.'
        if (!$androidHome) { $androidHome = Join-Path $projectRoot '.tools\android-sdk' }
        if (!$java) { $java = Join-Path $projectRoot '.tools\jdk' }
        & $sdk build $app -t:InstallAndroidDependencies -f net10.0-android -p:AndroidSdkDirectory="$androidHome" -p:JavaSdkDirectory="$java" -p:AcceptAndroidSDKLicenses=True
        if ($LASTEXITCODE) { throw 'Android-SDK/JDK konnten nicht installiert werden.' }
    }
    if (!$androidHome) { throw 'Android-SDK nicht gefunden: -AndroidSdk <Pfad> angeben, ANDROID_HOME setzen oder -Bootstrap verwenden.' }
    if (!$java) { throw 'JDK nicht gefunden: -JavaHome <Pfad> angeben, JAVA_HOME setzen oder -Bootstrap verwenden.' }

    $sdks = @("-p:AndroidSdkDirectory=$androidHome", "-p:JavaSdkDirectory=$java")
    Write-Output "Android-SDK: $androidHome"
    Write-Output "JDK:         $java"

    & $sdk run --project tests/SysWlan.Tests -c Release
    if ($LASTEXITCODE) { throw 'Tests failed.' }

    New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
    if ($Release) {
        $keystore = $env:SYSWLAN_ANDROID_KEYSTORE
        $signing = @()
        if ($keystore -and (Test-Path -LiteralPath $keystore)) {
            if (!$env:SYSWLAN_ANDROID_KEYPASS -or !$env:SYSWLAN_ANDROID_STOREPASS) { throw 'SYSWLAN_ANDROID_KEYPASS und SYSWLAN_ANDROID_STOREPASS müssen gesetzt sein, damit keine Passwörter in Dateien oder Protokollen landen.' }
            $signing = @('-p:AndroidKeyStore=true', "-p:AndroidSigningKeyStore=$keystore", '-p:AndroidSigningKeyAlias=syswlaninfo', '-p:AndroidSigningKeyPass=env:SYSWLAN_ANDROID_KEYPASS', '-p:AndroidSigningStorePass=env:SYSWLAN_ANDROID_STOREPASS')
        }
        else {
            Write-Output 'Kein Keystore gefunden (SYSWLAN_ANDROID_KEYSTORE). Android signiert das Release-APK dann mit dem Debug-Schlüssel: installierbar zum Testen, aber nicht geeignet für dauerhafte Updates. Eigener Schlüssel: scripts\build-android.ps1 -CreateKeystore.'
        }
        $publishDir = Join-Path $projectRoot 'src\SysWlan.App\bin\Release\net10.0-android\publish'
        & $sdk publish $app -f net10.0-android -c Release -p:AndroidPackageFormats=apk @sdks @signing
        if ($LASTEXITCODE) { throw 'Android-Release-Build fehlgeschlagen.' }
        Get-ChildItem $publishDir -Filter '*.apk' | Copy-Item -Destination $artifacts -Force
    }
    else {
        & $sdk build $app -f net10.0-android -c Debug -p:AndroidPackageFormats=apk @sdks
        if ($LASTEXITCODE) { throw 'Android-Debug-Build fehlgeschlagen.' }
        Get-ChildItem (Join-Path $projectRoot 'src\SysWlan.App\bin\Debug\net10.0-android') -Filter '*.apk' | Copy-Item -Destination $artifacts -Force
    }
    Get-ChildItem $artifacts -Filter '*.apk' | ForEach-Object { Write-Output $_.FullName }
    Write-Output 'Signatur prüfen: <build-tools>\apksigner verify --print-certs <APK>'
    Write-Output 'Installation auf einem Gerät: adb install -r <APK>'
    Write-Output 'Die APK ist eigenständig (EmbedAssembliesIntoApk=true): kein Fast-Deployment über adb nötig.'
    Write-Output 'Hinweis: Die APK enthält standardmäßig arm64-v8a und x86_64 und ist deshalb groß. Für eine kleinere'
    Write-Output 'APK in SysWlan.App.csproj die Android-Gruppe auf RuntimeIdentifiers=android-arm64 kürzen.'
}
finally { Pop-Location }
