param(
    [Parameter(Mandatory = $true)]
    [string]$SourceWwisePath
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$targetRoot = Join-Path $projectRoot 'UnityProj\Assets\Wwise'
$sourceRoot = (Resolve-Path -LiteralPath $SourceWwisePath).Path
$expectedVersion = '2025.1.11.4331'

# 验证 SDK 来源和版本，避免覆盖项目配置或引入不匹配的程序集。
if ($sourceRoot.TrimEnd('\') -eq $targetRoot.TrimEnd('\')) {
    throw 'SDK 来源不能是当前项目的 Wwise 目录。'
}
$versionPath = Join-Path $sourceRoot 'Version.txt'
if (!(Test-Path -LiteralPath $versionPath) -or
    !([System.IO.File]::ReadAllText($versionPath).Contains($expectedVersion))) {
    throw "需要 Wwise Unity Integration $expectedVersion 的 Assets/Wwise 目录。"
}
$sdkDirectories = @(
    'API', 'Gizmos', 'Logging', 'MonoBehaviour', 'OpenXR',
    'ProjectDatabase', 'Timeline', 'Tools', 'Utilities'
)
foreach ($directoryName in $sdkDirectories) {
    if (!(Test-Path -LiteralPath (Join-Path $sourceRoot $directoryName) -PathType Container)) {
        throw "SDK 缺少目录：$directoryName"
    }
}

# 只恢复第三方 SDK，保留本项目的 ScriptableObjects 和 Resources 配置及其 GUID。
New-Item -ItemType Directory -Path $targetRoot -Force | Out-Null
foreach ($directoryName in $sdkDirectories) {
    Copy-Item -LiteralPath (Join-Path $sourceRoot $directoryName) -Destination $targetRoot -Recurse -Force
    $metadataPath = Join-Path $sourceRoot ($directoryName + '.meta')
    if (Test-Path -LiteralPath $metadataPath) {
        Copy-Item -LiteralPath $metadataPath -Destination $targetRoot -Force
    }
}
foreach ($fileName in @('Version.txt', 'Version.txt.meta', 'LauncherInfo.json', 'LauncherInfo.json.meta')) {
    $sourceFile = Join-Path $sourceRoot $fileName
    if (Test-Path -LiteralPath $sourceFile) {
        Copy-Item -LiteralPath $sourceFile -Destination $targetRoot -Force
    }
}
Write-Host 'Wwise SDK 已恢复。请使用 Unity 打开 UnityProj。'