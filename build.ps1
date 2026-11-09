param(
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $outputPath = Join-Path $projectRoot 'dist'
} else {
    $outputPath = [System.IO.Path]::GetFullPath($OutputDirectory)
}
dotnet publish (Join-Path $projectRoot 'src/MouseBattery.csproj') -c Release -r win-x64 --self-contained false -o $outputPath --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
