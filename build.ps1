# Kor.exe dosyasını Windows'un yerleşik .NET Framework derleyicisiyle üretir.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = $PSScriptRoot
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"

# Simge: siyah zemin üzerinde şampanya rengi çentikli kadran
$icon = Join-Path $root 'src\sayac.ico'
$size = 256
$bmp = New-Object Drawing.Bitmap $size, $size
$g = [Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.Clear([Drawing.Color]::Transparent)
$g.FillEllipse([Drawing.Brushes]::Black, 4, 4, 248, 248)
$pen = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(216, 196, 154)), 9
$pen.StartCap = 'Round'; $pen.EndCap = 'Round'
for ($i = 0; $i -lt 12; $i++) {
    $a = $i * [Math]::PI / 6
    $g.DrawLine($pen, [float](128 + 104 * [Math]::Sin($a)), [float](128 - 104 * [Math]::Cos($a)),
                      [float](128 + 78 * [Math]::Sin($a)), [float](128 - 78 * [Math]::Cos($a)))
}
$g.Dispose()
$png = New-Object IO.MemoryStream
$bmp.Save($png, [Drawing.Imaging.ImageFormat]::Png)
$bytes = $png.ToArray()
$out = [IO.File]::Create($icon)
$w = New-Object IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]1)
$w.Write([byte]0); $w.Write([byte]0); $w.Write([byte]0); $w.Write([byte]0)
$w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$bytes.Length); $w.Write([uint32]22)
$w.Write($bytes)
$w.Close()

$langs = Get-ChildItem "$root\src\diller\*.txt" | ForEach-Object { "/resource:$($_.FullName),dil/$($_.Name)" }
# Spotify kimliği kaynak kodda tutulmaz; dosya varsa uygulamanın içine eklenir.
$idFile = Join-Path $root 'spotify-kimlik.txt'
if (Test-Path $idFile) { $langs = @($langs) + "/resource:$idFile,spotify-kimlik.txt" }
$sources = Get-ChildItem "$root\src\*.cs" | ForEach-Object { $_.FullName }
& "$fw\csc.exe" /nologo /target:winexe /optimize+ /codepage:65001 `
    "/out:$root\Kor.exe" "/win32icon:$icon" "/win32manifest:$root\src\app.manifest" "/resource:$root\src\ui.xaml,ui.xaml" "/resource:$root\src\mini.xaml,mini.xaml" "/resource:$root\src\note.xaml,note.xaml" "/resource:$root\src\imza.pub.xml,imza.pub.xml" "/resource:$root\extension\manifest.json,eklenti/manifest.json" "/resource:$root\extension\background.js,eklenti/background.js" "/resource:$root\extension\core\connect.js,eklenti/core/connect.js" "/resource:$root\extension\connectors\youtube-music.js,eklenti/connectors/youtube-music.js" "/resource:$root\extension\popup.html,eklenti/popup.html" "/resource:$root\extension\popup.js,eklenti/popup.js" "/resource:$root\extension\icons\icon-16.png,eklenti/icons/icon-16.png" "/resource:$root\extension\icons\icon-32.png,eklenti/icons/icon-32.png" "/resource:$root\extension\icons\icon-48.png,eklenti/icons/icon-48.png" "/resource:$root\extension\icons\icon-128.png,eklenti/icons/icon-128.png" `
    "/r:$fw\WPF\PresentationFramework.dll" "/r:$fw\WPF\PresentationCore.dll" "/r:$fw\WPF\WindowsBase.dll" "/r:$fw\System.Xaml.dll" "/r:$fw\System.Net.Http.dll" "/r:$fw\System.Web.Extensions.dll" "/r:$fw\System.Security.dll" "/r:$fw\System.Windows.Forms.dll" "/r:$fw\System.Drawing.dll" `
    $langs $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Output "OK: $root\Kor.exe"
