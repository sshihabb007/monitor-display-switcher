param(
    [switch]$SkipDesktopCopy
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$DistDir = Join-Path $ProjectRoot "dist"
$AssetsDir = Join-Path $ProjectRoot "assets"
$IconPath = Join-Path $AssetsDir "app.ico"
$ManifestPath = Join-Path $ProjectRoot "src\app.manifest"
$OutExe = Join-Path $DistDir "DisplayInputSwitcher.exe"

New-Item -ItemType Directory -Force -Path $DistDir | Out-Null

# Gracefully stop any running tray instance locking dist\DisplayInputSwitcher.exe or Desktop\DisplayInputSwitcher.exe
$WasRunning = $false
$RunningProcs = Get-Process -Name "DisplayInputSwitcher" -ErrorAction SilentlyContinue
if ($RunningProcs) {
    $WasRunning = $true
    Write-Host "[0/4] Stopping running DisplayInputSwitcher tray instance before rebuild..."
    $RunningProcs | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 350
}

if (-not (Test-Path $IconPath)) {
    Write-Host "[1/4] Generating multi-resolution icon..."
    python (Join-Path $ProjectRoot "scripts\generate_icon.py")
}

$FwDir = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
if (-not (Test-Path $FwDir)) {
    $FwDir = "C:\Windows\Microsoft.NET\Framework\v4.0.30319"
}
$WpfDir = Join-Path $FwDir "WPF"
$Csc = Join-Path $FwDir "csc.exe"

if (-not (Test-Path $Csc)) {
    throw "csc.exe not found at $Csc"
}

$Sources = Get-ChildItem -Path (Join-Path $ProjectRoot "src") -Recurse -Filter "*.cs" | Select-Object -ExpandProperty FullName

Write-Host "[2/4] Compiling universal portable DisplayInputSwitcher.exe (AnyCPU: native x64 / ARM64 / x86)..."
& $Csc `
    /nologo `
    /target:winexe `
    /platform:anycpu `
    /optimize+ `
    /out:$OutExe `
    /win32icon:$IconPath `
    /win32manifest:$ManifestPath `
    /r:"$FwDir\System.dll" `
    /r:"$FwDir\System.Core.dll" `
    /r:"$FwDir\System.Drawing.dll" `
    /r:"$FwDir\System.Windows.Forms.dll" `
    /r:"$FwDir\System.Web.Extensions.dll" `
    /r:"$FwDir\System.Xaml.dll" `
    /r:"$WpfDir\WindowsBase.dll" `
    /r:"$WpfDir\PresentationCore.dll" `
    /r:"$WpfDir\PresentationFramework.dll" `
    $Sources

if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE"
}

$ExeInfo = Get-Item $OutExe
$Sha256 = (Get-FileHash -Path $OutExe -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host ("      Built: {0} ({1:N1} KB, SHA-256: {2})" -f $OutExe, ($ExeInfo.Length / 1KB), $Sha256)

$ReportPath = Join-Path $DistDir "selftest-report.txt"
if (Test-Path $ReportPath) { Remove-Item $ReportPath -Force }

Write-Host "[3/4] Running unit + read-only live DDC/CI verification..."
$proc = Start-Process -FilePath $OutExe -ArgumentList @("--self-test", "--out", $ReportPath) -Wait -PassThru
if (Test-Path $ReportPath) {
    Get-Content $ReportPath | ForEach-Object { Write-Host "      $_" }
}
if ($proc.ExitCode -ne 0) {
    throw "Self-test failed with exit code $($proc.ExitCode)"
}

if (-not $SkipDesktopCopy) {
    $DesktopDir = [Environment]::GetFolderPath("Desktop")
    $DesktopExe = Join-Path $DesktopDir "DisplayInputSwitcher.exe"
    Write-Host "[4/4] Copying verified portable .exe to Desktop: $DesktopExe"
    Copy-Item -Path $OutExe -Destination $DesktopExe -Force

    if ($WasRunning) {
        Write-Host "      Relaunching updated DisplayInputSwitcher.exe in System Tray..."
        Start-Process -FilePath $DesktopExe -ArgumentList "--minimized" | Out-Null
    }
}

Write-Host "SUCCESS! Universal Portable DisplayInputSwitcher.exe is ready."
