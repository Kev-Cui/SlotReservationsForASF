# Run before "Upload/Update on Steam Workshop" in RimWorld.
# RimWorld uploads the whole mod folder as-is, so this rebuilds the DLLs,
# deletes build intermediates and lists what will be uploaded.
$ErrorActionPreference = 'Stop'
$src = $PSScriptRoot
$mod = Split-Path $src -Parent

foreach ($v in '1.6', '1.7') {
    dotnet build "$src\SlotReservations.csproj" -c Release -p:RWVersion=$v -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $v" }
}

foreach ($d in 'obj', 'bin') {
    if (Test-Path "$src\$d") { Remove-Item "$src\$d" -Recurse -Force }
}

[xml](Get-Content "$mod\About\About.xml" -Raw -Encoding UTF8) | Out-Null
$preview = Get-Item "$mod\About\Preview.png"
if ($preview.Length -ge 1MB) { throw "Preview.png must be under 1 MB" }

Write-Host "`nFiles RimWorld will upload (from $mod):" -ForegroundColor Cyan
Get-ChildItem $mod -Recurse -File -Force |
    ForEach-Object { $_.FullName.Substring($mod.Length + 1) } |
    Sort-Object
