param(
    [Parameter(Mandatory=$true)][string]$Package,
    [Parameter(Mandatory=$true)][string]$Validator,
    [Parameter(Mandatory=$true)][string]$InstallerEngine,
    [Parameter(Mandatory=$true)][string]$BuildDirectory
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$taskBrokenFixture = Join-Path $BuildDirectory 'JoinAssist-missing-modern-metadata.test.vsix'
if (Test-Path -LiteralPath $taskBrokenFixture) { Remove-Item -LiteralPath $taskBrokenFixture }
$taskInput = [System.IO.Compression.ZipFile]::OpenRead($Package)
try {
    $taskOutput = [System.IO.Compression.ZipFile]::Open($taskBrokenFixture,[System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($taskEntry in $taskInput.Entries) {
            if ($taskEntry.FullName -in @('manifest.json','catalog.json')) { continue }
            $taskCopy = $taskOutput.CreateEntry($taskEntry.FullName)
            $taskSourceStream = $taskEntry.Open()
            $taskTargetStream = $taskCopy.Open()
            try { $taskSourceStream.CopyTo($taskTargetStream) }
            finally { $taskTargetStream.Dispose(); $taskSourceStream.Dispose() }
        }
    } finally { $taskOutput.Dispose() }
} finally { $taskInput.Dispose() }
$taskFailureLog = Join-Path $BuildDirectory 'packaging-negative-test.txt'
& $Validator $taskBrokenFixture $InstallerEngine *> $taskFailureLog
if ($LASTEXITCODE -ne 1) { throw 'Regressione: il lettore non ha rifiutato nel modo previsto il pacchetto privo di metadati moderni.' }
if (!(Select-String -LiteralPath $taskFailureLog -Pattern 'InvalidExtensionPackageException' -Quiet)) { throw 'Il pacchetto di controllo è fallito per una causa diversa dal difetto originale.' }
Write-Output 'PASS: un pacchetto senza manifest.json e catalog.json riproduce il difetto originale e viene rifiutato.'
