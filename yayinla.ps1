# Yeni surum yayinlar. Kullanim: .\yayinla.ps1 1.1.0
# Surum notu surum-notu.txt dosyasindan okunur. Ozel anahtar proje disindadir.
param([Parameter(Mandatory = $true)][string]$Version)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$repo = 'Talverinhub/Kor'
$keyPath = Join-Path $env:USERPROFILE '.sayac-imza\ozel-anahtar.xml'
if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'Surum 1.2.3 veya 1.2.3.4 biciminde olmali' }
$assemblyVersion = if ($Version.Split('.').Count -eq 4) { $Version } else { "$Version.0" }
if (-not (Test-Path $keyPath)) { throw 'Ozel imza anahtari bulunamadi' }

& powershell -ExecutionPolicy Bypass -File (Join-Path $root 'test.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Testler basarisiz, yayin durduruldu' }

$utf8 = New-Object Text.UTF8Encoding $false
$versionFile = Join-Path $root 'src\Version.cs'
$text = [IO.File]::ReadAllText($versionFile, $utf8)
$text = $text -replace 'Current = "[^"]+"', "Current = `"$Version`""
$text = $text -replace 'Version\("[^"]+"\)', "Version(`"$assemblyVersion`")"
[IO.File]::WriteAllText($versionFile, $text, $utf8)

$exe = Join-Path $root 'Kor.exe'
if (Test-Path $exe) { Move-Item $exe "$exe.yayin-eski" -Force }
& powershell -ExecutionPolicy Bypass -File (Join-Path $root 'build.ps1')
if (-not (Test-Path $exe)) { throw 'Derleme basarisiz' }
Remove-Item "$exe.yayin-eski" -ErrorAction SilentlyContinue

$notesFile = Join-Path $root 'surum-notu.txt'
$notes = if (Test-Path $notesFile) { [IO.File]::ReadAllText($notesFile, $utf8).Trim() } else { '' }
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()
$out = Join-Path $env:TEMP "sayac-yayin-$Version"
New-Item -ItemType Directory -Force $out | Out-Null
$manifest = @{ version = $Version; sha256 = $hash; notes = $notes } | ConvertTo-Json -Compress
$bytes = $utf8.GetBytes($manifest)
[IO.File]::WriteAllBytes("$out\surum.json", $bytes)

$rsa = New-Object Security.Cryptography.RSACryptoServiceProvider
$rsa.PersistKeyInCsp = $false
$rsa.FromXmlString([IO.File]::ReadAllText($keyPath))
$sha = [Security.Cryptography.SHA256]::Create()
$signature = [Convert]::ToBase64String($rsa.SignData($bytes, $sha))
[IO.File]::WriteAllText("$out\surum.json.sig", $signature, $utf8)

Copy-Item $exe "$out\Kor.exe" -Force
$noteArg = if ($notes) { $notes } else { "Surum $Version" }
[IO.File]::WriteAllText("$out\not.txt", $noteArg, $utf8)
& gh release create "v$Version" "$out\Kor.exe" "$out\surum.json" "$out\surum.json.sig" --repo $repo --title "Kor $Version" --notes-file "$out\not.txt"
if ($LASTEXITCODE -ne 0) { throw 'GitHub yayini basarisiz' }
Write-Output "Yayinlandi: $Version"
