param(
    [string]$Configuration = "Release",
    [string[]]$Rids = @("win-x64"),
    [string]$CliProject = "src/Agent.Cli/Agent.Cli.csproj",
    [string]$ServerProject = "src/Agent.Server/Agent.Server.csproj",
    [string]$BuildDir = "build"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$buildRoot = Join-Path $repoRoot $BuildDir
$publishRoot = Join-Path $repoRoot "artifacts/publish"

New-Item -ItemType Directory -Force $buildRoot | Out-Null
New-Item -ItemType Directory -Force $publishRoot | Out-Null

foreach ($rid in $Rids) {
    $publishCliDir = Join-Path $publishRoot "cli/$rid"
    $publishServerDir = Join-Path $publishRoot "server/$rid"
    if (Test-Path $publishCliDir) { Remove-Item $publishCliDir -Recurse -Force }
    if (Test-Path $publishServerDir) { Remove-Item $publishServerDir -Recurse -Force }

    dotnet publish (Join-Path $repoRoot $CliProject) `
        -c $Configuration `
        -r $rid `
        --self-contained false `
        /p:PublishSingleFile=true `
        -o $publishCliDir

    dotnet publish (Join-Path $repoRoot $ServerProject) `
        -c $Configuration `
        -r $rid `
        --self-contained false `
        /p:PublishSingleFile=true `
        -o $publishServerDir

    $targetCliDir = Join-Path $buildRoot "cli/$rid"
    $targetServerDir = Join-Path $buildRoot "server/$rid"
    if (Test-Path $targetCliDir) { Remove-Item $targetCliDir -Recurse -Force }
    if (Test-Path $targetServerDir) { Remove-Item $targetServerDir -Recurse -Force }
    New-Item -ItemType Directory -Force $targetCliDir | Out-Null
    New-Item -ItemType Directory -Force $targetServerDir | Out-Null

    Copy-Item -Path (Join-Path $publishCliDir "*") -Destination $targetCliDir -Recurse -Force
    Copy-Item -Path (Join-Path $publishServerDir "*") -Destination $targetServerDir -Recurse -Force
    Write-Host "Published CLI $rid -> $targetCliDir"
    Write-Host "Published Server $rid -> $targetServerDir"
}
