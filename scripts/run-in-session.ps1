# 在“当前用户会话”里运行 TiaProbe.exe（绕过沙箱/服务会话，能连上 TIA Portal）
# 用法: pwsh scripts\run-in-session.ps1 -ExeArgs '--import' -OutDir 'D:\TiaLadGen\out'
param(
    [string]$ExeArgs = '--import',
    [string]$OutDir = 'D:\TiaLadGen\out',
    [string]$ExePath = ''
)

$ErrorActionPreference = 'Stop'
if (-not $ExePath) {
    $repo = Split-Path -Parent $PSScriptRoot
    $ExePath = Join-Path $repo 'bin\TiaProbe.exe'
}
if (-not (Test-Path $ExePath)) { throw "找不到 $ExePath，先运行 scripts\build.ps1" }

$taskName = 'TiaLadGenRun'
$principal = New-ScheduledTaskPrincipal -UserId ("$env:USERDOMAIN\$env:USERNAME") -LogonType Interactive
$action = New-ScheduledTaskAction -Execute $ExePath -Argument "`"$OutDir`" $ExeArgs" -WorkingDirectory $OutDir
Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Force | Out-Null
Start-ScheduledTask -TaskName $taskName

$log = Join-Path $OutDir 'probe.log'
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Seconds 3
    if (Test-Path $log) {
        $c = Get-Content $log -Raw
        if ($c -match '\[DONE\]|\[FAIL\]') { break }
    }
}
Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue | Out-Null
if (Test-Path $log) { Get-Content $log -Raw } else { Write-Warning "没有日志输出" }
