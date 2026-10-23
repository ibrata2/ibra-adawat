# IBMC — build the Windows installer on your own PC.
# Right-click this file → "Run with PowerShell". It installs what it needs the first time (.NET 10 SDK, Inno Setup),
# downloads ffmpeg, whisper, yt-dlp and Real-ESRGAN, builds the app, and leaves
# windows\build\installer\IBMC-Setup-x64.exe next to this file.
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = 1; $env:DOTNET_NOLOGO = 1

function Need($what, $test, $winget) {
  if (-not (& $test)) {
    Write-Host "Installing $what…" -ForegroundColor Yellow
    winget install --id $winget -e --accept-source-agreements --accept-package-agreements
    $env:Path = [Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [Environment]::GetEnvironmentVariable("Path", "User")
  }
}
Need ".NET 10 SDK" { (Get-Command dotnet -ErrorAction SilentlyContinue) -and ((dotnet --list-sdks) -match "^10\.") } "Microsoft.DotNet.SDK.10"
$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
  winget install --id JRSoftware.InnoSetup -e --accept-source-agreements --accept-package-agreements
  $iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
}
$arabic = Join-Path (Split-Path $iscc) "Languages\Arabic.isl"
if (-not (Test-Path $arabic)) {
  Invoke-WebRequest "https://raw.githubusercontent.com/jrsoftware/issrc/main/Files/Languages/Unofficial/Arabic.isl" -OutFile $arabic
}

Set-Location windows
$out = "build\publish\app-x64"
Remove-Item -Recurse -Force $out -ErrorAction SilentlyContinue
foreach ($p in "Adawat.App", "Adawat.Cli") {
  dotnet publish "src\$p\$p.csproj" -f net10.0-windows10.0.22621.0 -c Release -r win-x64 --self-contained -p:DebugType=none -o $out
  if ($LASTEXITCODE) { throw "Build failed ($p). Send the red error lines to Claude." }
}

$tools = "$out\tools"; $dl = "build\dl"
New-Item -ItemType Directory -Force $tools, $dl, "$tools\realesrgan\models" | Out-Null
function Get($url, $file) { if (-not (Test-Path "$dl\$file")) { Write-Host "Downloading $file…"; Invoke-WebRequest $url -OutFile "$dl\$file" } }
Get "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip" "ffmpeg.zip"
Get "https://github.com/ggml-org/whisper.cpp/releases/latest/download/whisper-bin-x64.zip" "whisper.zip"
Get "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe" "yt-dlp.exe"
Get "https://github.com/xinntao/Real-ESRGAN/releases/download/v0.2.5.0/realesrgan-ncnn-vulkan-20220424-windows.zip" "esrgan.zip"
foreach ($z in "ffmpeg", "whisper", "esrgan") { Remove-Item -Recurse -Force "$dl\$z" -ErrorAction SilentlyContinue; Expand-Archive "$dl\$z.zip" "$dl\$z" }
Get-ChildItem "$dl\ffmpeg" -Recurse -Include ffmpeg.exe, ffprobe.exe | Copy-Item -Destination $tools
$cli = Get-ChildItem "$dl\whisper" -Recurse -Filter whisper-cli.exe | Select-Object -First 1
Copy-Item $cli.FullName $tools; Get-ChildItem $cli.DirectoryName -Filter *.dll | Copy-Item -Destination $tools
Copy-Item "$dl\yt-dlp.exe" $tools
$exe = Get-ChildItem "$dl\esrgan" -Recurse -Filter realesrgan-ncnn-vulkan.exe | Select-Object -First 1
Copy-Item $exe.FullName "$tools\realesrgan"; Get-ChildItem $exe.DirectoryName -Filter *.dll | Copy-Item -Destination "$tools\realesrgan"
Get-ChildItem $exe.DirectoryName -Recurse -Filter "realesrgan-x4plus.*" | Where-Object { $_.Name -notlike "*anime*" } | Copy-Item -Destination "$tools\realesrgan\models"

Set-Location installer
& $iscc /Q /DArch=x64 /DAppVersion=1.1.0 "/DSource=..\build\publish\app-x64" Adawat.iss
if ($LASTEXITCODE) { throw "Installer failed." }
Write-Host "`nDone: windows\build\installer\IBMC-Setup-x64.exe" -ForegroundColor Green
explorer.exe (Resolve-Path ..\build\installer)
Read-Host "Press Enter to close"
