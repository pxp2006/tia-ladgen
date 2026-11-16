# 编译 C# 探针（不依赖 .NET SDK / Visual Studio，用 .NET Framework 自带 csc）
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$tia  = 'D:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48'
$csc  = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path $csc)) { throw "找不到 csc.exe: $csc（需要 .NET Framework 4.x）" }
if (-not (Test-Path $tia)) { throw "找不到 Openness API 目录: $tia（需要 TIA Portal V21）" }

$outDir = Join-Path $repo 'bin'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$sources = Get-ChildItem (Join-Path $repo 'src') -Filter *.cs | ForEach-Object { $_.FullName }

& $csc /nologo /target:exe /platform:x64 /codepage:65001 `
    "/out:$outDir\TiaProbe.exe" `
    "/r:$tia\Siemens.Engineering.Base.dll" `
    "/r:$tia\Siemens.Engineering.Step7.dll" `
    $sources

if ($LASTEXITCODE -ne 0) { throw "编译失败，退出码 $LASTEXITCODE" }
Write-Host "[OK] 编译完成: $outDir\TiaProbe.exe"
