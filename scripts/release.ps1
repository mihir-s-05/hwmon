#Requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet('windows', 'linux', 'all')][string]$Target = 'all',
    [string]$OutputDirectory = 'dist',
    [string]$PythonExecutable = 'python',
    [switch]$Force
)
$ErrorActionPreference = 'Stop'
$build = & (Join-Path $PSScriptRoot 'build.ps1') -Target $Target
$arguments = @((Join-Path $PSScriptRoot 'package.py'), '--platform', $Target, '--output', $OutputDirectory)
if ($build.windows) { $arguments += @('--windows', $build.windows) }
if ($build.linux) { $arguments += @('--linux', $build.linux) }
if ($Force) { $arguments += '--force' }
& $PythonExecutable @arguments
if ($LASTEXITCODE -ne 0) { throw 'Release packaging failed.' }
