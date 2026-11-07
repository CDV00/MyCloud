<#
.SYNOPSIS
    Chạy toàn bộ MyCloud ở môi trường dev: hạ tầng (Docker), các service .NET và web app.

.EXAMPLE
    .\run-dev.ps1              # kiểm tra yêu cầu rồi chạy
    .\run-dev.ps1 -CheckOnly   # chỉ kiểm tra yêu cầu
    .\run-dev.ps1 -Test        # chạy toàn bộ test backend + frontend
#>
param(
    [switch]$CheckOnly,
    [switch]$Test
)

# Không dùng 'Stop': Windows PowerShell 5.1 coi mọi dòng stderr của lệnh ngoài (docker, npm) là lỗi.
# Script tự kiểm tra $LASTEXITCODE ở từng bước.
$ErrorActionPreference = 'Continue'
$root = $PSScriptRoot
$web = Join-Path $root 'FrontEnd\web'

function Fail($message) {
    Write-Host "  X $message" -ForegroundColor Red
    exit 1
}

function Ok($message) {
    Write-Host "  OK $message" -ForegroundColor Green
}

Write-Host 'Kiem tra yeu cau...' -ForegroundColor Cyan

# .NET SDK 10
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Fail 'Chua cai .NET SDK 10: winget install Microsoft.DotNet.SDK.10' }
$sdks = (& dotnet --list-sdks) -join "`n"
if ($sdks -notmatch '(?m)^10\.') { Fail 'Can .NET SDK 10: winget install Microsoft.DotNet.SDK.10' }
Ok ".NET SDK $((& dotnet --version))"

# Docker Desktop đang chạy
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { Fail 'Chua cai Docker Desktop' }
& cmd /c "docker info >nul 2>&1"
if ($LASTEXITCODE -ne 0) { Fail 'Docker Desktop chua chay - mo Docker Desktop roi chay lai' }
Ok 'Docker dang chay'

# Node.js >= 20.18
if (-not (Get-Command node -ErrorAction SilentlyContinue)) { Fail 'Chua cai Node.js: winget install OpenJS.NodeJS.LTS' }
$nodeVersion = [version]((& node --version).TrimStart('v'))
if ($nodeVersion -lt [version]'20.18.0') { Fail "Node $nodeVersion qua cu, can >= 20.18 (khuyen nghi 24 LTS)" }
Ok "Node $nodeVersion"

# Cổng cần trống (AppHost cũ còn chạy sẽ giữ các cổng này)
# Gateway, service, web, Aspire dashboard + 2 cổng nội bộ của Aspire (OTLP 19080, resource service 20080).
$ports = 5000, 5101, 5102, 5103, 5104, 5105, 5106, 5173, 15080, 19080, 20080
$busy = Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Where-Object { $ports -contains $_.LocalPort }
if ($busy) {
    $list = ($busy | ForEach-Object { "$($_.LocalPort) (PID $($_.OwningProcess))" } | Sort-Object -Unique) -join ', '
    Fail "Cong dang bi dung: $list. MyCloud co the dang chay o cua so khac - tat no (Ctrl+C) roi chay lai."
}
Ok 'Cac cong cua MyCloud dang trong'

# Cài package cho web app lần đầu (Aspire cũng tự cài, làm trước cho nhanh và báo lỗi rõ)
if (-not (Test-Path (Join-Path $web 'node_modules'))) {
    Write-Host 'Cai package cho web app (lan dau)...' -ForegroundColor Cyan
    Push-Location $web
    try { & npm install --no-fund --no-audit; if ($LASTEXITCODE -ne 0) { Fail 'npm install that bai' } }
    finally { Pop-Location }
}
Ok 'Package web app da san sang'

if ($CheckOnly) { exit 0 }

if ($Test) {
    Write-Host 'Chay test backend (can Docker cho Testcontainers)...' -ForegroundColor Cyan
    & dotnet test (Join-Path $root 'BackEnd\MyCloud.slnx')
    $backend = $LASTEXITCODE
    Write-Host 'Chay test frontend...' -ForegroundColor Cyan
    Push-Location $web
    try { & npm test; $frontend = $LASTEXITCODE } finally { Pop-Location }
    if ($backend -ne 0 -or $frontend -ne 0) { Fail 'Co test that bai' }
    Ok 'Tat ca test deu pass'
    exit 0
}

Write-Host ''
Write-Host 'Khoi dong MyCloud (Ctrl+C de dung)...' -ForegroundColor Cyan
Write-Host '  Web app         http://localhost:5173'
Write-Host '  Aspire dashboard http://localhost:15080 (log, trace, trang thai)'
Write-Host '  Gateway         http://localhost:5000'
Write-Host ''
& dotnet run --project (Join-Path $root 'BackEnd\src\Aspire\MyCloud.AppHost')
