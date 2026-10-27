param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist')
)

$ErrorActionPreference = 'Stop'
$version = '1.1.0'
$assemblyPath = Join-Path $PSScriptRoot 'bin/Release/CopperPipes.dll'
if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
    throw 'Build CopperPipes in Release configuration before packaging.'
}

$assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($assemblyPath).Version
if ($assemblyVersion.ToString(3) -ne $version) {
    throw "The compiled assembly version is $assemblyVersion; expected $version."
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$archivePath = Join-Path $OutputDirectory ('CopperPipes-' + $version + '.zip')
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archiveStream = [IO.File]::Open($archivePath, [IO.FileMode]::Create)
try {
    $archive = [IO.Compression.ZipArchive]::new(
        $archiveStream,
        [IO.Compression.ZipArchiveMode]::Create,
        $true
    )
    try {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive,
            $assemblyPath,
            'BepInEx/plugins/CopperPipes/CopperPipes.dll',
            [IO.Compression.CompressionLevel]::Optimal
        ) | Out-Null
    } finally {
        $archive.Dispose()
    }
} finally {
    $archiveStream.Dispose()
}

Get-FileHash -LiteralPath $archivePath -Algorithm SHA256
