param(
    [string]$SsmsRoot = 'C:\Program Files\Microsoft SQL Server Management Studio 22\Release',
    [string]$VssdkToolsPath = ''
)
$ErrorActionPreference = 'Stop'
$taskRepoRoot = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'Build.ps1') -SsmsRoot $SsmsRoot -VssdkToolsPath $VssdkToolsPath
[xml]$taskManifest = Get-Content -Raw -LiteralPath (Join-Path $taskRepoRoot 'packaging\source.extension.vsixmanifest')
$taskVersion = [string]$taskManifest.PackageManifest.Metadata.Identity.Version
$taskAsset = Join-Path $taskRepoRoot ("artifacts\JoinAssist-" + $taskVersion + '.vsix')
$taskDigest = (Get-FileHash -LiteralPath $taskAsset -Algorithm SHA256).Hash.ToLowerInvariant()
$taskChecksum = Join-Path $taskRepoRoot 'artifacts\SHA256SUMS.txt'
[System.IO.File]::WriteAllText($taskChecksum, $taskDigest + '  ' + [System.IO.Path]::GetFileName($taskAsset) + "`n", [System.Text.UTF8Encoding]::new($false))
Write-Output "Release files: $taskAsset and $taskChecksum"
Write-Output "No files have been uploaded. See docs/RELEASING.md."
