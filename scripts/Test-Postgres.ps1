param(
    [string]$SettingsPath = "$PSScriptRoot/../backend/src/TaskFlow.Api/appsettings.Development.json",
    [switch]$ProbeOnly
)
$ErrorActionPreference = 'Stop'
$taskFlowRoot = [IO.Path]::GetFullPath("$PSScriptRoot/..")
$settings = Get-Content -LiteralPath $SettingsPath -Raw | ConvertFrom-Json
$migrationConnection = $settings.ConnectionStrings.TestMigration
$readConnection = $settings.ConnectionStrings.TestRead
if (-not $migrationConnection -or -not $readConnection) {
    throw 'Set ConnectionStrings.TestMigration and TestRead for a disposable PostgreSQL test database. Credentials will not be printed.'
}
$oldWrite = $env:TASKFLOW_TEST_WRITE
$oldRead = $env:TASKFLOW_TEST_READ
try {
    $env:TASKFLOW_TEST_WRITE = $migrationConnection
    $env:TASKFLOW_TEST_READ = $readConnection
    Push-Location $taskFlowRoot
    try {
        if ($ProbeOnly) {
            dotnet test backend/tests/TaskFlow.IntegrationTests --no-restore --filter FullyQualifiedName~ConnectivityAndRoleBoundary
        } else {
            dotnet test backend/tests/TaskFlow.IntegrationTests --no-restore
        }
        if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL verification failed; inspect redacted test results.' }
    } finally { Pop-Location }
} finally {
    $env:TASKFLOW_TEST_WRITE = $oldWrite
    $env:TASKFLOW_TEST_READ = $oldRead
}
