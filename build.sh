#!/bin/bash
# Builds KeepRight.dll into dist/ (and a zip with the README).
# Usage: ./build.sh [folder with the ETS2LA assemblies]
set -e
cd "$(dirname "$0")"
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"

args=(-c Release -o build-out)
[ -n "$1" ] && args+=("-p:ETS2LABin=$1")

rm -rf build-out
dotnet build "${args[@]}"

mkdir -p dist
cp build-out/KeepRight.dll dist/
rm -f dist/KeepRight.zip
python3 -c "import zipfile; z=zipfile.ZipFile('dist/KeepRight.zip','w',zipfile.ZIP_DEFLATED); z.write('dist/KeepRight.dll','KeepRight.dll'); z.write('README.md','README.md'); z.close()"
echo "dist/KeepRight.dll and dist/KeepRight.zip ready"
