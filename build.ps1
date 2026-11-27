# Builds Tweakeroo.exe with the compiler included in Windows. Usage: powershell -ExecutionPolicy Bypass -File build.ps1
param([string]$Out = "$PSScriptRoot\Tweakeroo.exe", [switch]$Shot)
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$src = "$PSScriptRoot\src"
$a = @('/nologo', '/utf8output', '/codepage:65001', '/optimize+', '/platform:x64', '/target:winexe', "/out:$Out",
  "/lib:$fw\WPF", '/r:PresentationFramework.dll', '/r:PresentationCore.dll', '/r:WindowsBase.dll', "/r:$fw\System.Xaml.dll",
  '/r:System.ServiceProcess.dll', '/r:System.Web.Extensions.dll', '/r:Microsoft.CSharp.dll', '/r:System.Management.dll', '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll',
  "/res:$src\Window.xaml,Window.xaml", "/res:$src\terminos.txt,terminos.txt", "/res:$src\terminos.en.txt,terminos.en.txt", "/res:$src\en.txt,en.txt", "/res:$src\personaje.jpg,personaje.jpg", "/res:$src\valorant.jpg,valorant.jpg", "/res:$src\cs2.jpg,cs2.jpg", "/res:$src\card-fortnite.jpg,card-fortnite.jpg", "/res:$src\card-valorant.jpg,card-valorant.jpg", "/res:$src\card-cs2.jpg,card-cs2.jpg", "/res:$src\historial.txt,historial.txt", "/res:$src\canal.txt,canal.txt", "/res:$src\clave-publica.xml,clave-publica.xml", "/win32icon:$src\tweakeroo.ico")
# Every sound in src\sonidos goes in as a resource named after its file; any of them may be missing.
$a += Get-ChildItem "$src\sonidos\*.wav" -ErrorAction SilentlyContinue | ForEach-Object { "/res:$($_.FullName),$($_.Name)" }
# The same for the step photos in src\pasos, shown on hover in "Antes de jugar".
$a += Get-ChildItem "$src\pasos\*.jpg" -ErrorAction SilentlyContinue | ForEach-Object { "/res:$($_.FullName),$($_.Name)" }
# The game screenshot behind the crosshair preview; without it the preview shows a dark gradient.
if (Test-Path "$src\crosshair-fondo.jpg") { $a += "/res:$src\crosshair-fondo.jpg,crosshair-fondo.jpg" }
# -Shot: test build without admin rights, with screenshots and click test.
if ($Shot) { $a += '/define:SHOT', '/nowin32manifest', '/r:UIAutomationProvider.dll', '/r:UIAutomationTypes.dll' } else { $a += "/win32manifest:$src\app.manifest" }
& "$fw\csc.exe" @a (Get-ChildItem "$src\*.cs" | ForEach-Object FullName)
exit $LASTEXITCODE
