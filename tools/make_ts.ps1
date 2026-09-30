param(
    [Parameter(Mandatory = $true)][string]$ProjectFilePath,
    [Parameter(Mandatory = $true)][string]$TargetPath
)

$ErrorActionPreference = "Stop"
$project = [xml](Get-Content -Raw -LiteralPath $ProjectFilePath)
$assemblyName = (Select-Xml -Xml $project -XPath "//AssemblyName").Node.InnerText
$root = Split-Path -Parent (Split-Path -Parent $ProjectFilePath)
$outputPath = Split-Path -Parent $TargetPath
$packageRoot = Join-Path $root "temp\package"
$archivePath = Join-Path $OutputPath ($assemblyName + ".zip")

Remove-Item -LiteralPath (Join-Path $root "temp") -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null

Copy-Item -LiteralPath (Join-Path $root "manifest.json") -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $root "README.md") -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $root "requirements.txt") -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $root "LICENSE") -Destination $packageRoot
if (Test-Path -LiteralPath (Join-Path $root "icon.png")) {
    Copy-Item -LiteralPath (Join-Path $root "icon.png") -Destination $packageRoot
}
Copy-Item -LiteralPath $TargetPath -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $OutputPath ($assemblyName + ".dll.mdb")) -Destination $packageRoot -ErrorAction SilentlyContinue
Copy-Item -LiteralPath (Join-Path $OutputPath "jump sounds") -Destination $packageRoot -Recurse

Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $archivePath -Force
Remove-Item -LiteralPath (Join-Path $root "temp") -Recurse -Force
