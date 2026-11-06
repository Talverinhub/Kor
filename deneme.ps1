# Yayinlamadan once deneme: kodu deneme klasorune derler ve ayri ayarlarla acar.
# Asil Kor.exe ve ayarlar degismez, GitHub'a hicbir sey yuklenmez.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$folder = Join-Path $root 'deneme'
$data = Join-Path $folder 'veri'
New-Item -ItemType Directory -Force $data | Out-Null

Get-Process Sayac,Kor -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$folder*" } | ForEach-Object { $_.Kill(); $_.WaitForExit(3000) | Out-Null }

$main = Join-Path $root 'Kor.exe'
$saved = "$main.deneme-yedek"
if (Test-Path $main) { Copy-Item $main $saved -Force }
try {
    & powershell -ExecutionPolicy Bypass -File (Join-Path $root 'build.ps1')
    Copy-Item $main (Join-Path $folder 'Kor.exe') -Force
}
finally {
    if (Test-Path $saved) {
        $old = "$main.eski"
        if (Test-Path $main) { Move-Item $main $old -Force }
        Move-Item $saved $main -Force
        Remove-Item $old -ErrorAction SilentlyContinue
    }
}

$settings = Join-Path $data 'ayarlar.json'
if (-not (Test-Path $settings)) {
    $source = Join-Path $env:LOCALAPPDATA 'Sayac\ayarlar.json'
    $json = if (Test-Path $source) { Get-Content $source -Raw -Encoding UTF8 | ConvertFrom-Json } else { New-Object psobject }
    $json | Add-Member -NotePropertyName AutoUpdate -NotePropertyValue $false -Force
    $json | Add-Member -NotePropertyName GuideHidden -NotePropertyValue $true -Force
    [IO.File]::WriteAllText($settings, ($json | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding $false))
}

$env:SAYAC_VERI = $data
Start-Process (Join-Path $folder 'Kor.exe')
Write-Output "Deneme surumu acildi: $folder"
