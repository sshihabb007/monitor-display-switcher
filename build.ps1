param(
    [switch]$SkipDesktopCopy,
    [string]$Version = "1.2.0"
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$DistDir = Join-Path $ProjectRoot "dist"
$ObjDir = Join-Path $ProjectRoot "obj"
$AssetsDir = Join-Path $ProjectRoot "assets"
$IconPath = Join-Path $AssetsDir "app.ico"
$ManifestPath = Join-Path $ProjectRoot "src\app.manifest"
$OutExe = Join-Path $DistDir "DisplayInputSwitcher.exe"
$PortableReleaseExe = Join-Path $DistDir "Monitor-Display-Switcher-Portable-$Version.exe"
$SetupReleaseExe = Join-Path $DistDir "Monitor-Display-Switcher-Setup-$Version.exe"

New-Item -ItemType Directory -Force -Path $DistDir, $ObjDir | Out-Null

# Gracefully stop any running tray instance locking dist\DisplayInputSwitcher.exe or Desktop\DisplayInputSwitcher.exe
$WasRunning = $false
$RunningProcs = Get-Process -Name "DisplayInputSwitcher" -ErrorAction SilentlyContinue
if ($RunningProcs) {
    $WasRunning = $true
    Write-Host "[0/6] Stopping running DisplayInputSwitcher tray instance before rebuild..."
    $RunningProcs | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 350
}

if (-not (Test-Path $IconPath)) {
    Write-Host "[1/6] Generating multi-resolution icon..."
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

Write-Host "[2/6] Compiling Portable DisplayInputSwitcher.exe (AnyCPU: native x64 / ARM64 / x86)..."
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
    throw "Portable compilation failed with exit code $LASTEXITCODE"
}

Copy-Item -LiteralPath $OutExe -Destination $PortableReleaseExe -Force

$PortableInfo = Get-Item $PortableReleaseExe
$PortableSha = (Get-FileHash -LiteralPath $PortableReleaseExe -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host ("      Built Portable: {0} ({1:N1} KB, SHA-256: {2})" -f $PortableReleaseExe, ($PortableInfo.Length / 1KB), $PortableSha)

$ReportPath = Join-Path $DistDir "selftest-report.txt"
if (Test-Path $ReportPath) { Remove-Item $ReportPath -Force }

Write-Host "[3/6] Running unit + read-only live DDC/CI verification..."
$proc = Start-Process -FilePath $OutExe -ArgumentList @("--self-test", "--out", $ReportPath) -Wait -PassThru
if (Test-Path $ReportPath) {
    Get-Content $ReportPath | ForEach-Object { Write-Host "      $_" }
}
if ($proc.ExitCode -ne 0) {
    throw "Self-test failed with exit code $($proc.ExitCode)"
}

Write-Host "[4/6] Staging payload & compiling Windows Setup Installer ($SetupReleaseExe)..."
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$StageDir = Join-Path $ObjDir "installer_stage"
$PayloadZip = Join-Path $ObjDir "installer_payload.zip"
if (Test-Path $StageDir) { Remove-Item -LiteralPath $StageDir -Recurse -Force }
if (Test-Path $PayloadZip) { Remove-Item -LiteralPath $PayloadZip -Force }

New-Item -ItemType Directory -Force -Path $StageDir | Out-Null
Copy-Item -LiteralPath $OutExe -Destination (Join-Path $StageDir "DisplayInputSwitcher.exe") -Force
if (Test-Path (Join-Path $ProjectRoot "README.md")) {
    Copy-Item -LiteralPath (Join-Path $ProjectRoot "README.md") -Destination (Join-Path $StageDir "README.md") -Force
}
if (Test-Path (Join-Path $ProjectRoot "LICENSE")) {
    Copy-Item -LiteralPath (Join-Path $ProjectRoot "LICENSE") -Destination (Join-Path $StageDir "LICENSE") -Force
}

[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $StageDir,
    $PayloadZip,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false)
Remove-Item -LiteralPath $StageDir -Recurse -Force -ErrorAction SilentlyContinue

$InstallerSrc = Join-Path $ProjectRoot "installer\DisplaySwitcherSetupWizard.cs"
& $Csc `
    /nologo `
    /target:winexe `
    /platform:anycpu `
    /optimize+ `
    /out:$SetupReleaseExe `
    /win32icon:$IconPath `
    /win32manifest:$ManifestPath `
    /r:"$FwDir\System.dll" `
    /r:"$FwDir\System.Core.dll" `
    /r:"$FwDir\System.Drawing.dll" `
    /r:"$FwDir\System.Windows.Forms.dll" `
    /r:"$FwDir\System.IO.Compression.dll" `
    /r:"$FwDir\System.IO.Compression.FileSystem.dll" `
    "/resource:$IconPath,DisplayInputSwitcherInstaller.app.ico" `
    "/resource:$PayloadZip,DisplayInputSwitcherInstaller.payload.zip" `
    $InstallerSrc

if ($LASTEXITCODE -ne 0) {
    throw "Setup Installer compilation failed with exit code $LASTEXITCODE"
}

$SetupInfo = Get-Item $SetupReleaseExe
$SetupSha = (Get-FileHash -LiteralPath $SetupReleaseExe -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host ("      Built Setup:    {0} ({1:N1} KB, SHA-256: {2})" -f $SetupReleaseExe, ($SetupInfo.Length / 1KB), $SetupSha)

Write-Host "[5/6] Verifying Setup Installer silent install + uninstall lifecycle in sandbox..."
$SmokeDir = Join-Path $ObjDir ("smoke_install_" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
try {
    $instProc = Start-Process -FilePath $SetupReleaseExe -ArgumentList @("--silent", "--no-registry", "--install-dir", $SmokeDir) -Wait -PassThru
    if ($instProc.ExitCode -ne 0) {
        throw "Silent setup exited with code $($instProc.ExitCode)"
    }
    $installedApp = Join-Path $SmokeDir "DisplayInputSwitcher.exe"
    $installedUninst = Join-Path $SmokeDir "Uninstall.exe"
    if (-not (Test-Path $installedApp) -or -not (Test-Path $installedUninst)) {
        throw "Setup smoke test missing expected extracted files in $SmokeDir"
    }
    $extractedHash = (Get-FileHash -LiteralPath $installedApp -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($extractedHash -ne $PortableSha) {
        throw "Extracted DisplayInputSwitcher.exe hash ($extractedHash) does not match portable build ($PortableSha)"
    }

    $uninstProc = Start-Process -FilePath $SmokeDir\Uninstall.exe -ArgumentList @("--uninstall", "--silent", "--no-registry", "--install-dir", $SmokeDir) -Wait -PassThru
    if ($uninstProc.ExitCode -ne 0) {
        throw "Silent uninstall exited with code $($uninstProc.ExitCode)"
    }
    Start-Sleep -Milliseconds 1500
    if (Test-Path $installedApp) {
        throw "Uninstall failed to remove $installedApp"
    }
    Write-Host "      [PASS] Setup silent install, SHA-256 payload integrity, and clean uninstall verified."
}
finally {
    if (Test-Path $SmokeDir) {
        Remove-Item -LiteralPath $SmokeDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if (-not $SkipDesktopCopy) {
    $DesktopDir = [Environment]::GetFolderPath("Desktop")
    $DesktopExe = Join-Path $DesktopDir "DisplayInputSwitcher.exe"
    Write-Host "[6/6] Copying verified portable .exe to Desktop: $DesktopExe"
    Copy-Item -Path $OutExe -Destination $DesktopExe -Force

    if ($WasRunning) {
        Write-Host "      Relaunching updated DisplayInputSwitcher.exe in System Tray..."
        Start-Process -FilePath $DesktopExe -ArgumentList "--minimized" | Out-Null
    }
}

Write-Host "SUCCESS! Both Portable and Setup Installer executables are ready in dist\."
