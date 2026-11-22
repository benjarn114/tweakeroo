# Builds Tweakeroo.exe with the compiler included in Windows. Usage: powershell -ExecutionPolicy Bypass -File build.ps1
param([string]$Out = "$PSScriptRoot\Tweakeroo.exe", [switch]$Shot)
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$src = "$PSScriptRoot\src"
$a = @('/nologo', '/utf8output', '/codepage:65001', '/optimize+', '/platform:x64', '/target:winexe', "/out:$Out",
  "/lib:$fw\WPF", '/r:PresentationFramework.dll', '/r:PresentationCore.dll', '/r:WindowsBase.dll', "/r:$fw\System.Xaml.dll",
  '/r:System.ServiceProcess.dll', '/r:System.Web.Extensions.dll', '/r:Microsoft.CSharp.dll', '/r:System.Management.dll', '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll',
  "/res:$src\Window.xaml,Window.xaml", "/res:$src\terminos.txt,terminos.txt", "/res:$src\terminos.en.txt,terminos.en.txt", "/res:$src\en.txt,en.txt", "/res:$src\personaje.jpg,personaje.jpg", "/res:$src\historial.txt,historial.txt", "/res:$src\canal.txt,canal.txt", "/res:$src\clave-publica.xml,clave-publica.xml", "/win32icon:$src\tweakeroo.ico")
# -Shot: test build without admin rights, with screenshots and click test.
if ($Shot) { $a += '/define:SHOT', '/nowin32manifest', '/r:UIAutomationProvider.dll', '/r:UIAutomationTypes.dll' } else { $a += "/win32manifest:$src\app.manifest" }
& "$fw\csc.exe" @a (Get-ChildItem "$src\*.cs" | ForEach-Object FullName)
exit $LASTEXITCODE
