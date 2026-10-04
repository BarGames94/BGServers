# Compila el launcher y el publicador con el csc de .NET Framework 4 (viene con Windows).
$ErrorActionPreference = 'Stop'
$src = Join-Path $PSScriptRoot 'src'
$out = Join-Path $PSScriptRoot 'out'
New-Item -ItemType Directory -Force $out | Out-Null
$fw = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $fw 'csc.exe'
$refs = @("/r:$fw\System.Web.Extensions.dll", '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Security.dll')
$common = @("$src\Common.cs", "$src\Config.cs")

& $csc /nologo /target:winexe /optimize+ /codepage:65001 "/out:$out\Launcher.exe" "/win32icon:$src\app.ico" $refs $common "$src\Launcher.cs" "$src\Options.cs" "$src\Phase2.cs" "$src\License.cs" "$src\Visuals.cs" "$src\Icons.cs" "$src\Brand.cs"
if ($LASTEXITCODE -ne 0) { throw 'Error compilando Launcher' }
& $csc /nologo /target:winexe /optimize+ /codepage:65001 "/out:$out\Publicador.exe" "/win32icon:$src\app.ico" $refs $common "$src\Publisher.cs" "$src\Placer.cs" "$src\Icons.cs" "$src\Brand.cs"
if ($LASTEXITCODE -ne 0) { throw 'Error compilando Publicador' }
Get-ChildItem $out\*.exe | ForEach-Object { '{0}  {1:N0} bytes' -f $_.Name, $_.Length }
