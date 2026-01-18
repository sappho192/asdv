param(
    [string]$Configuration = "Release",
    [string[]]$Rids = @("win-x64"),
    [string]$Project = "src/Agent.Cli/Agent.Cli.csproj",
    [string]$BuildDir = "build"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$buildRoot = Join-Path $repoRoot $BuildDir
$publishRoot = Join-Path $repoRoot "artifacts/publish"

New-Item -ItemType Directory -Force $buildRoot | Out-Null
New-Item -ItemType Directory -Force $publishRoot | Out-Null

foreach ($rid in $Rids) {
    $publishDir = Join-Path $publishRoot $rid
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

    dotnet publish (Join-Path $repoRoot $Project) `
        -c $Configuration `
        -r $rid `
        --self-contained false `
        /p:PublishSingleFile=true `
        -o $publishDir

    $targetDir = Join-Path $buildRoot $rid
    if (Test-Path $targetDir) { Remove-Item $targetDir -Recurse -Force }
    New-Item -ItemType Directory -Force $targetDir | Out-Null

    Copy-Item -Path (Join-Path $publishDir "*") -Destination $targetDir -Recurse -Force
    Write-Host "Published $rid -> $targetDir"
}
