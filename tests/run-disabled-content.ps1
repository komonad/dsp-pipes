$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'run-copper-interactions.ps1') -WithoutBuiltInContent
