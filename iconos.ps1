# Genera src\Icons.cs: logos de redes sociales (Simple Icons, licencia CC0) como PNG blancos de 96 px en base64.
# Uso: powershell -STA -ExecutionPolicy Bypass -File iconos.ps1
Add-Type -AssemblyName PresentationCore, WindowsBase, PresentationFramework
$src = "$env:TEMP\bg-icons"
$brands = [ordered]@{
    whatsapp = '#25D366'; discord = '#5865F2'; tiktok = '#000000'; facebook = '#0866FF'
    youtube = '#FF0000'; instagram = '#E4405F'; telegram = '#26A5E4'
}
$sb = New-Object Text.StringBuilder
[void]$sb.AppendLine('// Generado por iconos.ps1 - logos de Simple Icons (https://simpleicons.org, CC0). No editar a mano.')
[void]$sb.AppendLine('using System.Collections.Generic;')
[void]$sb.AppendLine('namespace BGLauncher')
[void]$sb.AppendLine('{')
[void]$sb.AppendLine('    static class Icons')
[void]$sb.AppendLine('    {')
[void]$sb.AppendLine('        // red -> { color de la marca, PNG blanco 96x96 en base64 }')
[void]$sb.AppendLine('        public static readonly Dictionary<string, string[]> Data = new Dictionary<string, string[]>')
[void]$sb.AppendLine('        {')
foreach ($k in $brands.Keys) {
    $svg = [IO.File]::ReadAllText("$src\$k.svg")
    $d = [regex]::Match($svg, ' d="([^"]+)"').Groups[1].Value
    $geo = [Windows.Media.Geometry]::Parse($d)
    $size = 96; $pad = 4
    $dv = New-Object Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    $dc.PushTransform((New-Object Windows.Media.TranslateTransform($pad, $pad)))
    $dc.PushTransform((New-Object Windows.Media.ScaleTransform((($size - 2 * $pad) / 24.0), (($size - 2 * $pad) / 24.0))))
    $dc.DrawGeometry([Windows.Media.Brushes]::White, $null, $geo)
    $dc.Pop(); $dc.Pop(); $dc.Close()
    $bmp = New-Object Windows.Media.Imaging.RenderTargetBitmap($size, $size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($dv)
    $enc = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $ms = New-Object IO.MemoryStream; $enc.Save($ms)
    $b64 = [Convert]::ToBase64String($ms.ToArray())
    [void]$sb.AppendLine('            { "' + $k + '", new[] { "' + $brands[$k] + '", "' + $b64 + '" } },')
    "$k -> $($ms.Length) bytes"
}
[void]$sb.AppendLine('        };')
[void]$sb.AppendLine('    }')
[void]$sb.AppendLine('}')
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'src\Icons.cs'), $sb.ToString(), (New-Object Text.UTF8Encoding $false))
