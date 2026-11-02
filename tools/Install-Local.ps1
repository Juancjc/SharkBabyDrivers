#Requires -Version 5.1
<#
.SYNOPSIS
    Installs (or updates) SharkBabyDrivers for the current user from this repository, without admin rights.

.DESCRIPTION
    1. Publishes the app (Release, win-x64, framework-dependent single file) into a separate folder
       (publish\local-<version>), so a running copy in bin\Release can never block the build output.
    2. Stops running SharkBabyDrivers instances (only that process name; SignalRGB is never touched).
    3. Replaces %LOCALAPPDATA%\Programs\SharkBabyDrivers with the new files.
    4. Updates the Start-menu shortcut, the per-user "Apps & features" entry and an uninstaller.
    5. Keeps "Start with Windows" exactly as the user left it (only re-points an existing entry).
    6. Refreshes Windows' icon cache, so the taskbar and Start menu show the current logo.
    7. Starts the installed app (normal window) unless -NoLaunch.

    The app data in %LOCALAPPDATA%\SharkBabyDrivers (ORIGINAL backups, saved profiles, settings, RGB choice,
    logs) is never modified or removed. Every recursive delete is checked against the exact install path.

.PARAMETER Source
    Use an already published folder instead of publishing (must contain SharkBabyDrivers.exe).

.PARAMETER DesktopShortcut
    Also create a shortcut on the desktop.

.PARAMETER NoLaunch
    Do not start the app after installing.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Install-Local.ps1
#>
[CmdletBinding()]
param(
    [string]$Source,
    [switch]$DesktopShortcut,
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$AppName     = 'SharkBabyDrivers'
$ExeName     = "$AppName.exe"
$RepoRoot    = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$InstallDir  = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA "Programs\$AppName"))
$DataDir     = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA $AppName))
$UninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$AppName"
$RunKey      = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$StartMenuLnk = Join-Path ([Environment]::GetFolderPath('Programs')) "$AppName.lnk"
$DesktopLnk  = Join-Path ([Environment]::GetFolderPath('Desktop')) "$AppName.lnk"

function Write-Step([string]$text) { Write-Host "==> $text" -ForegroundColor Cyan }

# Refuses anything but the exact install folder: never the data folder, the repository, a drive root or a profile.
function Assert-InstallPath([string]$path) {
    $full = [IO.Path]::GetFullPath($path).TrimEnd('\')
    $expected = $InstallDir.TrimEnd('\')
    if ($full -ne $expected) { throw "Caminho recusado para exclusão: '$full' (esperado '$expected')." }
    if ($full -eq $DataDir.TrimEnd('\')) { throw "Caminho recusado: é a pasta de dados ($DataDir)." }
    if ($full.StartsWith($RepoRoot.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) { throw "Caminho recusado: fica dentro do projeto ($RepoRoot)." }
    if ((Split-Path $full -Leaf) -ne $AppName) { throw "Caminho recusado: '$full' não termina em \$AppName." }
    if ($full.Length -lt ($env:LOCALAPPDATA.Length + 10)) { throw "Caminho recusado: '$full' é curto demais." }
}

function New-Shortcut([string]$lnk, [string]$target, [string]$arguments = '') {
    $shell = New-Object -ComObject WScript.Shell
    $s = $shell.CreateShortcut($lnk)
    $s.TargetPath = $target
    $s.Arguments = $arguments
    $s.WorkingDirectory = Split-Path $target
    $s.IconLocation = "$target,0"
    $s.Description = 'SharkBabyDrivers — mouse e teclado Attack Shark'
    $s.Save()
}

# ------------------------------------------------------------------ 1. publish into a separate folder
[xml]$props = Get-Content (Join-Path $RepoRoot 'Directory.Build.props')
$Version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $Version) { throw 'Versão não encontrada em Directory.Build.props.' }

if ($Source) {
    $PublishDir = (Resolve-Path $Source).Path
} else {
    $PublishDir = Join-Path $RepoRoot "publish\local-$Version"
    Write-Step "Publicando a versão $Version em $PublishDir"
    if (Test-Path $PublishDir) {
        $full = [IO.Path]::GetFullPath($PublishDir)
        if (-not $full.StartsWith((Join-Path $RepoRoot 'publish'), [StringComparison]::OrdinalIgnoreCase)) { throw "Pasta de publicação inesperada: $full" }
        Remove-Item -LiteralPath $full -Recurse -Force
    }
    & dotnet publish (Join-Path $RepoRoot "src\$AppName") -c Release -r win-x64 --self-contained false `
        -p:PublishSingleFile=true -p:DebugType=none -m:1 -p:UseSharedCompilation=false -o $PublishDir --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou (código $LASTEXITCODE)." }
}
$NewExe = Join-Path $PublishDir $ExeName
if (-not (Test-Path $NewExe)) { throw "$ExeName não encontrado em $PublishDir." }
$FileVersion = (Get-Item $NewExe).VersionInfo.ProductVersion.Split('+')[0]
Write-Step "Pacote pronto: $NewExe (versão $FileVersion)"

# ------------------------------------------------------------------ 2. stop old instances (only SharkBabyDrivers)
$running = @(Get-Process -Name $AppName -ErrorAction SilentlyContinue)
if ($running.Count) {
    Write-Step "Encerrando $($running.Count) instância(s) antiga(s) do $AppName"
    foreach ($p in $running) {
        $path = try { $p.Path } catch { '?' }
        Write-Host "    PID $($p.Id)  $path"
        Stop-Process -Id $p.Id -Force
    }
    foreach ($p in $running) { try { $p.WaitForExit(10000) | Out-Null } catch { } }
}

# ------------------------------------------------------------------ 3. replace the install folder
Write-Step "Instalando em $InstallDir"
Assert-InstallPath $InstallDir
if (Test-Path -LiteralPath $InstallDir) { Remove-Item -LiteralPath $InstallDir -Recurse -Force }
New-Item -ItemType Directory -Path $InstallDir | Out-Null
Copy-Item -Path (Join-Path $PublishDir '*') -Destination $InstallDir -Recurse -Force
$InstalledExe = Join-Path $InstallDir $ExeName

# Uninstaller: removes the program, its shortcuts, its "Apps & features" entry and its Run entry. Never the data.
$uninstallPs1 = @"
`$ErrorActionPreference = 'Stop'
`$dir = '$InstallDir'
Get-Process -Name '$AppName' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1
foreach (`$l in @('$StartMenuLnk', '$DesktopLnk')) { if (Test-Path -LiteralPath `$l) { Remove-Item -LiteralPath `$l -Force } }
Remove-Item -Path '$UninstallKey' -Recurse -Force -ErrorAction SilentlyContinue
Remove-ItemProperty -Path '$RunKey' -Name '$AppName' -ErrorAction SilentlyContinue
if ([IO.Path]::GetFullPath(`$dir).TrimEnd('\') -eq '$($InstallDir.TrimEnd('\'))') {
    Start-Process -WindowStyle Hidden cmd.exe -ArgumentList "/c timeout /t 2 >nul & rmdir /s /q ```"`$dir```""
}
Write-Host 'SharkBabyDrivers removido. Seus dados em $DataDir foram mantidos.'
"@
Set-Content -LiteralPath (Join-Path $InstallDir 'uninstall.ps1') -Value $uninstallPs1 -Encoding UTF8
Set-Content -LiteralPath (Join-Path $InstallDir 'uninstall.cmd') -Encoding ASCII -Value @"
@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1"
"@

# ------------------------------------------------------------------ 4. shortcuts and "Apps & features"
Write-Step 'Atualizando atalhos'
foreach ($dir in @([Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop'))) {
    Get-ChildItem -LiteralPath $dir -Filter "$AppName*.lnk" -ErrorAction SilentlyContinue | ForEach-Object {
        $target = (New-Object -ComObject WScript.Shell).CreateShortcut($_.FullName).TargetPath
        if ($target -ne $InstalledExe) { Write-Host "    removendo atalho obsoleto: $($_.FullName) -> $target"; Remove-Item -LiteralPath $_.FullName -Force }
    }
}
New-Shortcut $StartMenuLnk $InstalledExe
Write-Host "    Menu Iniciar: $StartMenuLnk"
if ($DesktopShortcut) { New-Shortcut $DesktopLnk $InstalledExe; Write-Host "    Área de trabalho: $DesktopLnk" }

Write-Step 'Registrando em Aplicativos instalados'
$sizeKb = [int]((Get-ChildItem -LiteralPath $InstallDir -Recurse -File | Measure-Object Length -Sum).Sum / 1KB)
New-Item -Path $UninstallKey -Force | Out-Null
$entries = [ordered]@{
    DisplayName = $AppName; DisplayVersion = $FileVersion; Publisher = 'Juancjc'
    InstallLocation = $InstallDir; DisplayIcon = "$InstalledExe,0"
    UninstallString = "`"$(Join-Path $InstallDir 'uninstall.cmd')`""
    URLInfoAbout = 'https://github.com/Juancjc/SharkBabyDrivers'
    InstallDate = (Get-Date -Format 'yyyyMMdd')
}
foreach ($e in $entries.GetEnumerator()) { Set-ItemProperty -Path $UninstallKey -Name $e.Key -Value $e.Value }
foreach ($e in @{ EstimatedSize = $sizeKb; NoModify = 1; NoRepair = 1 }.GetEnumerator()) {
    Set-ItemProperty -Path $UninstallKey -Name $e.Key -Value $e.Value -Type DWord
}

# ------------------------------------------------------------------ 5. start with Windows: keep the user's choice
$run = (Get-ItemProperty -Path $RunKey -ErrorAction SilentlyContinue)
if ($run -and ($run.PSObject.Properties.Name -contains $AppName)) {
    Set-ItemProperty -Path $RunKey -Name $AppName -Value "`"$InstalledExe`" --tray"
    Write-Step 'Iniciar com o Windows: estava ligado, agora aponta para a nova instalação'
} else {
    Write-Step 'Iniciar com o Windows: desligado (mantido como estava)'
}

# ------------------------------------------------------------------ 6. refresh Windows' icon cache
# Windows caches icons by file path, and the path never changes between installs: without this the taskbar,
# Start menu and tray may keep showing an older logo.
Write-Step 'Atualizando o cache de ícones do Windows'
$ie4uinit = Join-Path $env:WINDIR 'System32\ie4uinit.exe'
if (Test-Path -LiteralPath $ie4uinit) { & $ie4uinit -show }
Add-Type -Namespace SharkBabyInstall -Name Shell -MemberDefinition '[System.Runtime.InteropServices.DllImport("shell32.dll")] public static extern void SHChangeNotify(int eventId, uint flags, System.IntPtr item1, System.IntPtr item2);'
[SharkBabyInstall.Shell]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero) # SHCNE_ASSOCCHANGED

# ------------------------------------------------------------------ 7. data folder untouched, launch
if (Test-Path -LiteralPath $DataDir) { Write-Step "Dados preservados em $DataDir" }
if (-not $NoLaunch) {
    Write-Step 'Abrindo o SharkBabyDrivers instalado'
    Start-Process -FilePath $InstalledExe -WorkingDirectory $InstallDir
}
Write-Host ''
Write-Host "SharkBabyDrivers $FileVersion instalado em $InstallDir" -ForegroundColor Green
