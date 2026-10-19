<#
Reports @using directives in Razor files that are already in scope, so they can be removed:
  - imported by an _Imports.razor in the same folder or a parent folder (up to the root),
  - imported implicitly by Razor itself,
  - repeated earlier in the same file.
An _Imports.razor only inherits from its parent folders, never from itself.

Usage: .\check-razor-imports.ps1 [-Root <directory>]   (default: src)
Exits with 1 when something is reported, so it can run in CI.
#>

param(
    [string]$Root = 'src'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Entrypoint of project
$Root = (Resolve-Path -LiteralPath $Root).Path

# Global usings in Blazor projects
$Implicit = @(
    'Microsoft.AspNetCore.Builder'
    'Microsoft.AspNetCore.Components'
    'Microsoft.AspNetCore.Hosting'
    'Microsoft.AspNetCore.Http'
    'Microsoft.AspNetCore.Routing'
    'Microsoft.Extensions.Configuration'
    'Microsoft.Extensions.DependencyInjection'
    'Microsoft.Extensions.Hosting'
    'Microsoft.Extensions.Logging'
    'System'
    'System.Collections.Generic'
    'System.Linq'
    'System.IO'
    'System.Net.Http'
    'System.Net.Http.Json'
    'System.Threading'
    'System.Threading.Tasks'
)

function Get-NamespaceOf
{
    param([string]$Line)

    $result = $Line -replace '^\s*@using\s+', ''
    $result = $result -replace '\s*;?\s*$', ''

    return $result
}

function New-OrdinalDictionary
{
    return [System.Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
}

$found = 0
$razorFiles = Get-ChildItem -LiteralPath $Root -Recurse -Filter '*.razor' -File | Where-Object { $_.FullName -notmatch '[\\/]obj[\\/]' }

foreach ($fileInfo in $razorFiles)
{
    $file = $fileInfo.FullName
    $directory = [System.IO.Path]::GetDirectoryName($file)

    if ($fileInfo.Name -eq '_Imports.razor')
    {
        $start = [System.IO.Path]::GetDirectoryName($directory)
    }
    else
    {
        $start = $directory
    }

    # Namespace -> 'implicit' or path of the _Imports.razor that provides it
    $inherited = New-OrdinalDictionary

    foreach ($namespace in $Implicit)
    {
        $inherited[$namespace] = 'implicit'
    }

    $current = $start

    while ($true)
    {
        $imports = Join-Path $current '_Imports.razor'

        if (Test-Path -LiteralPath $imports -PathType Leaf)
        {
            $importMatches = Select-String -LiteralPath $imports -Pattern '^\s*@using\s'

            foreach ($match in $importMatches)
            {
                $namespace = Get-NamespaceOf -Line $match.Line

                if (-not $inherited.ContainsKey($namespace))
                {
                    $inherited[$namespace] = $imports
                }
            }
        }

        if ($current -eq $Root)
        {
            break
        }

        $parent = [System.IO.Path]::GetDirectoryName($current)
    
        if ( [string]::IsNullOrEmpty($parent))
        {
            break
        }
    
        $current = $parent
    }

    # Namespace -> first line number in this file
    $localLines = New-OrdinalDictionary
    $fileMatches = Select-String -LiteralPath $file -Pattern '^\s*@using\s'

    foreach ($match in $fileMatches)
    {
        $number = $match.LineNumber
        $namespace = Get-NamespaceOf -Line $match.Line

        if ( $inherited.ContainsKey($namespace))
        {
            Write-Output "${file}:${number}: $namespace (already in $( $inherited[$namespace] ))"
    
            $found = 1
        }
    
        elseif ($localLines.ContainsKey($namespace))
        {
            Write-Output "${file}:${number}: $namespace (repeated, first on line $( $localLines[$namespace] ))"
            
            $found = 1
        }
        else
        {
            $localLines[$namespace] = $number
        }
    }
}

if ($found -eq 0)
{
    Write-Output "No redundant @using found in the .razor files under $Root."
}

exit $found
