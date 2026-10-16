param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string] $Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $repoRoot 'artifacts'
$project = Join-Path $repoRoot 'src/ScreenTranslator/ScreenTranslator.csproj'
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null

foreach ($kind in @('portable', 'lite')) {
    $packageName = "ScreenTranslator-$Runtime-$kind"
    $output = Join-Path $artifactRoot $packageName
    if (Test-Path $output) { Remove-Item -Recurse -Force -LiteralPath $output }
    $selfContained = if ($kind -eq 'portable') { 'true' } else { 'false' }
    $publishArgs = @('publish', $project, '-c', 'Release', '-r', $Runtime,
        '--self-contained', $selfContained, '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=None',
        '-p:DebugSymbols=false', '-o', $output)
    if ($kind -eq 'portable') { $publishArgs += '-p:EnableCompressionInSingleFile=true' }
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $packageName" }
    Copy-Item (Join-Path $repoRoot 'README.md') (Join-Path $output 'README.md')
    New-Item -ItemType Directory -Force -Path (Join-Path $output 'docs') | Out-Null
    Copy-Item (Join-Path $repoRoot 'docs/windows-validation.md') (Join-Path $output 'docs/windows-validation.md')
    $archive = Join-Path $artifactRoot "$packageName.zip"
    Compress-Archive -Path "$output/*" -DestinationPath $archive -Force
}

$hashes = Get-ChildItem $artifactRoot -Filter "ScreenTranslator-$Runtime-*.zip" | Sort-Object Name | ForEach-Object {
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLowerInvariant()
    "$hash  $($_.Name)"
}
$hashes | Set-Content (Join-Path $artifactRoot 'SHA256SUMS.txt') -Encoding ascii
Write-Host "Packages written to $artifactRoot"
