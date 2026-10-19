#!/usr/bin/env bash
# Reports @using directives in Razor files that are already in scope, so they can be removed:
#   - imported by an _Imports.razor in the same folder or a parent folder (up to the .csproj),
#   - imported implicitly by Razor itself,
#   - repeated earlier in the same file.
# An _Imports.razor only inherits from its parent folders, never from itself.
#
# Usage: scripts/check-razor-imports.sh [directory]   (default: src)
# Exits with 1 when something is reported, so it can run in CI.

set -euo pipefail

# Entrypoint of project
ROOT="${1:-src}"

# Global usings in Blazor projects
IMPLICIT=(
    Microsoft.AspNetCore.Builder
    Microsoft.AspNetCore.Components
    Microsoft.AspNetCore.Hosting
    Microsoft.AspNetCore.Http
    Microsoft.AspNetCore.Routing
    Microsoft.Extensions.Configuration
    Microsoft.Extensions.DependencyInjection
    Microsoft.Extensions.Hosting
    Microsoft.Extensions.Logging
    System
    System.Collections.Generic
    System.Linq
    System.IO
    System.Net.Http
    System.Net.Http.Json
    System.Threading
    System.Threading.Tasks
)

namespace_of() {
    sed -E 's/^[[:space:]]*@using[[:space:]]+//; s/[[:space:]]*;?[[:space:]]*$//' <<< "$1"
}

found=0

while IFS= read -r -d '' file; do
    directory="$(dirname "$file")"
    
    if [[ $file == */_Imports.razor ]]; then
        start="$(dirname "$directory")"
    else
        start=$directory
    fi
    
    declare -A inherited=()

    for namespace in "${IMPLICIT[@]}"; do
        inherited[$namespace]="implicit"
    done

    current=$start
    
    while true; do
        if [[ -f "$current/_Imports.razor" ]]; then
            imports="$current/_Imports.razor"
            
            while IFS= read -r line; do
                namespace=$(namespace_of "$line")
                inherited[$namespace]=${inherited[$namespace]:-$imports}
            done < <(grep -E '^[[:space:]]*@using[[:space:]]' "$imports" || true)
        fi
        
        if [[ $current == "$ROOT" || $current == . || $current == / ]]; then
            break
        fi
        
        current="$(dirname "$current")"
    done
    
    declare -A local_lines=()

    while IFS=: read -r number line; do
        namespace=$(namespace_of "$line")
        
        if [[ -n ${inherited[$namespace]:-} ]]; then
            echo "$file:$number: $namespace (already in ${inherited[$namespace]})"
            
            found=1
        elif [[ -n ${local_lines[$namespace]:-} ]]; then
            echo "$file:$number: $namespace (repeated, first on line ${local_lines[$namespace]})"
            
            found=1
        else
            local_lines[$namespace]=$number
        fi
    done < <(grep -E -n '^[[:space:]]*@using[[:space:]]' "$file" || true)

    unset inherited
    unset local_lines
done < <(find "$ROOT" -name '*.razor' -not -path '*/obj/*' -print0)

if (( found == 0 )); then
    echo "No redundant @using found in the .razor files under $ROOT."
fi

exit "$found"
