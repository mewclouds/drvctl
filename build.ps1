$ErrorActionPreference = "Stop"

dotnet --info

dotnet publish .\drvctl.csproj `
    -c Release `
    -r win-x64

$Exe = Join-Path `
    $PSScriptRoot `
    "bin\Release\net10.0-windows\win-x64\publish\drvctl.exe"

Write-Host ""
Write-Host "Native AOT publish complete:"
Write-Host "  $Exe"

if (Test-Path -LiteralPath $Exe) {
    $Size = (Get-Item -LiteralPath $Exe).Length
    Write-Host ("  Size: {0:N2} MiB" -f ($Size / 1MB))
}
