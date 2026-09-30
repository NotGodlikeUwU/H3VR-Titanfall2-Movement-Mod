param(
    [string]$Configuration = "Release",
    [string]$ProfilePath = "E:\Games\r2mods\H3VR\profiles\Default",
    [string]$H3VRPath = "H:\SteamLibrary\steamapps\common\H3VR"
)

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "plugin\TitanfallMovement.csproj"
$output = Join-Path $root ("plugin\bin\" + $Configuration + "\net35")
$destination = Join-Path $ProfilePath "BepInEx\plugins\NotGodlike-TitanfallMovement"
$bepInExPath = Join-Path $ProfilePath "BepInEx"

dotnet build $project -c $Configuration "-p:H3VRPath=$H3VRPath" "-p:BepInExPath=$bepInExPath"
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $output "NotGodlike.TitanfallMovement.dll") -Destination $destination -Force
Copy-Item -LiteralPath (Join-Path $output "jump sounds") -Destination $destination -Recurse -Force
Write-Host "Installed Titanfall Movement to $destination"
