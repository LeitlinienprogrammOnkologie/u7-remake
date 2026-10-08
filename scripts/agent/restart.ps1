# (Re)start the game headless with the agent console and wait until it is ready.
#   pwsh scripts/agent/restart.ps1 [-Load <slot>] [-Io <dir>]
# Stops only headless Godot runs; the user's editor and editor-launched games are left alone.
# With -Io, the game reads and writes that folder (set U7_AGENT to it for agent.py) and only
# the game started there before (its pid.txt) is stopped, so several can run side by side.
# Godot binary: $env:GODOT, else the default 4.7 mono install path below.
param([string]$Load = "", [string]$Io = "")
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$io = if ($Io) { [System.IO.Path]::GetFullPath($Io) } else { Join-Path $root "agent_io" }
$godot = if ($env:GODOT) { $env:GODOT } else { "C:\Program Files\Godot_v4.7\Godot_v4.7-stable_mono_win64_console.exe" }
$pidFile = Join-Path $io "pid.txt"

if ($Io) {
    $old = Get-Content $pidFile -ErrorAction SilentlyContinue
    if ($old) {
        Get-CimInstance Win32_Process -Filter "ProcessId = $old" |
            Where-Object { $_.CommandLine -like "*--headless*" } |
            ForEach-Object { Stop-Process -Id $_.ProcessId -Force -Confirm:$false }
    }
} else {
    Get-CimInstance Win32_Process -Filter "Name like 'Godot%'" |
        Where-Object { $_.CommandLine -like "*--headless*" } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -Confirm:$false }
}
Start-Sleep -Milliseconds 500

New-Item -ItemType Directory -Force $io | Out-Null
Set-Content -Path (Join-Path $io "cmd.txt") -Value $null -NoNewline
Set-Content -Path (Join-Path $io "out.txt") -Value $null -NoNewline
$env:U7_AGENT = $io
$p = Start-Process -FilePath $godot -ArgumentList "--headless", "--path", (Join-Path $root "godot") `
    -RedirectStandardOutput (Join-Path $io "godot.log") -RedirectStandardError (Join-Path $io "godot_err.log") -PassThru
Set-Content -Path $pidFile -Value $p.Id -NoNewline

for ($i = 0; $i -lt 600; $i++) {
    $out = Get-Content (Join-Path $io "out.txt") -Raw -ErrorAction SilentlyContinue
    if ($out -and $out.Contains("agent ready")) { break }
    Start-Sleep -Milliseconds 100
}
"started headless game, pid $($p.Id); output in $io"
if ($Load) { python (Join-Path $PSScriptRoot "agent.py") "load $Load" }
