param(
    [ValidateSet('Build', 'Clean', 'Run', 'Publish')]
    [string]$Target = 'Build',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [string]$Project = '.\AudioSrtPlayer\AudioSrtPlayer.csproj',

    [string]$Output = '.\publish\win'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Project)) {
    throw "Project file not found: $Project"
}

Write-Host "Target: $Target" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration" -ForegroundColor Cyan
Write-Host "Project: $Project" -ForegroundColor Cyan

switch ($Target) {
    'Build' {
        dotnet build $Project -c $Configuration
        break
    }
    'Clean' {
        dotnet clean $Project -c $Configuration
        break
    }
    'Run' {
        dotnet run --project $Project
        break
    }
    'Publish' {
        dotnet publish $Project -c $Configuration -o $Output
        break
    }
}
