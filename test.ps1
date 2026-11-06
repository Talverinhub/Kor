# Otomatik testleri derler ve calistirir. Basarisiz olursa cikis kodu 1 olur.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$work = Join-Path $env:TEMP ("sayac-test-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $work | Out-Null
$exe = Join-Path $work 'Testler.exe'
$langs = Get-ChildItem "$root\src\diller\*.txt" | ForEach-Object { "/resource:$($_.FullName),dil/$($_.Name)" }
$sources = @(Get-ChildItem "$root\src\*.cs" | ForEach-Object { $_.FullName }) + @(Get-ChildItem "$root\tests\*.cs" | ForEach-Object { $_.FullName })
& "$fw\csc.exe" /nologo /target:exe /codepage:65001 /nowarn:0219,0618 /main:Sayac.TestRunner "/out:$exe" `
    "/resource:$root\src\ui.xaml,ui.xaml" "/resource:$root\src\mini.xaml,mini.xaml" "/resource:$root\src\imza.pub.xml,imza.pub.xml" "/resource:$root\extension\manifest.json,eklenti/manifest.json" "/resource:$root\extension\background.js,eklenti/background.js" "/resource:$root\extension\core\connect.js,eklenti/core/connect.js" "/resource:$root\extension\connectors\youtube-music.js,eklenti/connectors/youtube-music.js" "/resource:$root\extension\popup.html,eklenti/popup.html" "/resource:$root\extension\popup.js,eklenti/popup.js" "/resource:$root\extension\icons\icon-16.png,eklenti/icons/icon-16.png" "/resource:$root\extension\icons\icon-32.png,eklenti/icons/icon-32.png" "/resource:$root\extension\icons\icon-48.png,eklenti/icons/icon-48.png" "/resource:$root\extension\icons\icon-128.png,eklenti/icons/icon-128.png" `
    "/r:$fw\WPF\PresentationFramework.dll" "/r:$fw\WPF\PresentationCore.dll" "/r:$fw\WPF\WindowsBase.dll" "/r:$fw\System.Xaml.dll" `
    "/r:$fw\System.Net.Http.dll" "/r:$fw\System.Web.Extensions.dll" "/r:$fw\System.Security.dll" "/r:$fw\System.Windows.Forms.dll" "/r:$fw\System.Drawing.dll" `
    $langs $sources
if ($LASTEXITCODE -ne 0) { throw 'Test derlemesi basarisiz' }
$env:SAYAC_VERI = Join-Path $work 'veri'
& $exe
$code = $LASTEXITCODE
Remove-Item Env:\SAYAC_VERI
if ($code -ne 0) { throw 'Testler basarisiz' }
