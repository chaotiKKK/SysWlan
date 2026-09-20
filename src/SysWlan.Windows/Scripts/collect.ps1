$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [Console]::OutputEncoding
$issues = [System.Collections.Generic.List[string]]::new()
$routes = @(Get-NetRoute -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue | Where-Object { $_.State -eq 'Alive' } | Sort-Object @{Expression={$_.RouteMetric + $_.InterfaceMetric}})
$route = $routes | Select-Object -First 1
$adapter = $null
$neighbors = @()
if ($route) {
    $adapter = Get-NetAdapter -InterfaceIndex $route.InterfaceIndex -ErrorAction SilentlyContinue
    try { $neighbors = @(Get-NetNeighbor -InterfaceIndex $route.InterfaceIndex | Where-Object { $_.IPAddress -notmatch '^(22[4-9]\.|23[0-9]\.|ff)' -and $_.LinkLayerAddress -notmatch '^(00-00-00-00-00-00|FF-FF-FF-FF-FF-FF)$' } | Select-Object -First 256) } catch { $issues.Add('Nachbartabelle nicht verfügbar.') }
}
$gatewayMac = ($neighbors | Where-Object { $_.IPAddress -eq $route.NextHop } | Select-Object -First 1).LinkLayerAddress
$devices = @($neighbors | ForEach-Object { @{Address=[string]$_.IPAddress; Mac=[string]$_.LinkLayerAddress; State=[string]$_.State; Name= $(if ($_.IPAddress -eq $route.NextHop) {'Gateway'} else {''})} })
$processNames = @{}
Get-Process -ErrorAction SilentlyContinue | ForEach-Object { $processNames[[int]$_.Id] = $_.ProcessName }
$connections = [System.Collections.Generic.List[object]]::new()
try {
    Get-NetTCPConnection -ErrorAction Stop | Select-Object -First 1024 | ForEach-Object {
        $connections.Add(@{Protocol='TCP';LocalAddress=[string]$_.LocalAddress;LocalPort=[int]$_.LocalPort;RemoteAddress=[string]$_.RemoteAddress;RemotePort=[int]$_.RemotePort;State=[string]$_.State;ProcessId=[int]$_.OwningProcess;ProcessName=[string]$processNames[[int]$_.OwningProcess]})
    }
    Get-NetUDPEndpoint -ErrorAction Stop | Select-Object -First 512 | ForEach-Object {
        $connections.Add(@{Protocol='UDP';LocalAddress=[string]$_.LocalAddress;LocalPort=[int]$_.LocalPort;RemoteAddress='';RemotePort=0;State='Bound';ProcessId=[int]$_.OwningProcess;ProcessName=[string]$processNames[[int]$_.OwningProcess]})
    }
} catch { $issues.Add('Einige TCP/UDP-Verbindungen konnten nicht gelesen werden.') }
$events = @()
try {
    $events = @(Get-WinEvent -FilterHashtable @{LogName='Microsoft-Windows-WLAN-AutoConfig/Operational';StartTime=(Get-Date).AddMinutes(-15)} -MaxEvents 30 -ErrorAction Stop | ForEach-Object {
        @{Timestamp=$_.TimeCreated.ToUniversalTime().ToString('o'); Source='Windows WLAN'; Severity=$(if ($_.Level -le 2) {'Fehler'} elseif ($_.Level -eq 3) {'Warnung'} else {'Info'}); Message=([string]$_.Message)}
    })
} catch { if ($_.FullyQualifiedErrorId -notmatch 'NoMatchingEventsFound') { $issues.Add('Windows-WLAN-Ereignisprotokoll nicht verfügbar.') } }
$wlan = (& "$env:SystemRoot\System32\netsh.exe" wlan show interfaces | Out-String)
@{InterfaceId=[string]$adapter.InterfaceGuid; InterfaceName=[string]$adapter.Name; Gateway=[string]$route.NextHop; GatewayMac=[string]$gatewayMac; Devices=$devices; Connections=$connections.ToArray(); Events=$events; WlanText=$wlan; Errors=$issues.ToArray()} | ConvertTo-Json -Depth 6 -Compress
