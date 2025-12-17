param(
    [string]$Version = "latest",
    [string]$InstallDir = "$env:USERPROFILE\.dukascopy-downloader",
    [string]$Repo = "lastko/dukascopy-downloader"
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command curl.exe -ErrorAction SilentlyContinue)) {
    throw "curl.exe is required"
}
if (-not (Get-Command tar.exe -ErrorAction SilentlyContinue)) {
    throw "tar.exe is required (install via Windows optional features or Git for Windows)"
}

$asset = "dukascopy-downloader-win-x64.zip"
if ($Version -eq "latest") {
    $url = "https://github.com/$Repo/releases/latest/download/$asset"
} else {
    $url = "https://github.com/$Repo/releases/download/$Version/$asset"
}

$tmp = New-Item -ItemType Directory -Path ([System.IO.Path]::GetTempPath()) -Name ([System.Guid]::NewGuid())
$zipPath = Join-Path $tmp.FullName $asset
Write-Host "Downloading $url"
curl.exe -fsSL $url -o $zipPath

Expand-Archive -Path $zipPath -DestinationPath $tmp.FullName
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -Force (Join-Path $tmp.FullName 'dukascopy-downloader.exe') (Join-Path $InstallDir 'dukascopy-downloader.exe')

Write-Host "Binary copied to $InstallDir\dukascopy-downloader.exe"
Write-Host "Add `$InstallDir to your PATH or move the file into an existing location."
