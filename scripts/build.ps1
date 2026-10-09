#Requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet('windows', 'linux', 'all')][string]$Target = 'all',
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $repoRoot 'Hwmon.csproj'
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$version = @($projectXml.Project.PropertyGroup.Version | Where-Object { $_ })[0].ToString()
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repoRoot ('artifacts/build-' + [Guid]::NewGuid().ToString('N'))
} elseif (-not [IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot $OutputDirectory
}
$publishRoot = [IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $publishRoot) -and (Get-ChildItem -LiteralPath $publishRoot -Force | Select-Object -First 1)) {
    throw "Build output is not empty: $publishRoot. Choose a new -OutputDirectory."
}
New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
$result = [ordered]@{ version = $version; root = $publishRoot; windows = $null; linux = $null }
$targets = if ($Target -eq 'all') { @('windows', 'linux') } else { @($Target) }
Push-Location $repoRoot
try {
    foreach ($platform in $targets) {
        $framework = if ($platform -eq 'windows') { 'net8.0-windows' } else { 'net8.0' }
        $rid = if ($platform -eq 'windows') { 'win-x64' } else { 'linux-x64' }
        $folder = Join-Path $publishRoot ('hwmon-' + $platform)
        & dotnet publish $project -f $framework -c Release -r $rid --self-contained true -p:RestoreLockedMode=true -o $folder --nologo | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $platform." }
        foreach ($name in @('README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'CHANGELOG.md')) {
            Copy-Item -LiteralPath (Join-Path $repoRoot $name) -Destination $folder
        }
        Copy-Item -LiteralPath (Join-Path $repoRoot 'docs') -Destination (Join-Path $folder 'docs') -Recurse
        $licenseFolder = Join-Path $folder 'licenses'
        New-Item -ItemType Directory -Path $licenseFolder | Out-Null
        foreach ($license in Get-ChildItem -LiteralPath (Join-Path $repoRoot 'licenses') -File) {
            if (-not $license.Name.StartsWith('dotnet-')) { Copy-Item -LiteralPath $license.FullName -Destination $licenseFolder }
        }
        # Use notices from the exact runtime that was published, rather than a
        # hardcoded runtime patch version or a previously built bundle.
        $config = Get-Content -LiteralPath (Join-Path $folder 'hwmon.runtimeconfig.json') -Raw | ConvertFrom-Json
        $runtimeVersion = @($config.runtimeOptions.includedFrameworks | Where-Object { $_.name -eq 'Microsoft.NETCore.App' })[0].version
        $packageRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget/packages' }
        $runtimeRoot = Join-Path $packageRoot "microsoft.netcore.app.runtime.$rid/$runtimeVersion"
        foreach ($name in @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')) {
            Copy-Item -LiteralPath (Join-Path $runtimeRoot $name) -Destination (Join-Path $licenseFolder ('dotnet-' + $rid + '-' + $name))
        }
        if (($platform -eq 'windows' -and $IsWindows) -or ($platform -eq 'linux' -and $IsLinux)) {
            $executable = Join-Path $folder $(if ($IsWindows) { 'hwmon.exe' } else { 'hwmon' })
            & $executable self-test | Out-Host
            if ($LASTEXITCODE -ne 0) { throw "Published $platform executable failed self-test." }
            $reportedVersion = & $executable --version
            if ($reportedVersion -ne "hwmon $version") { throw "CLI version does not match project version: $reportedVersion" }
        }
        $result[$platform] = $folder
    }
} finally { Pop-Location }
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $publishRoot 'build.json') -Encoding utf8NoBOM
Write-Host "Build complete: $publishRoot"
[pscustomobject]$result
