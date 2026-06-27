#!/bin/bash
set -e

# --- VARIABLES ---
MOD_NAME="QuartermasterMod"
MODS_PATH="$HOME/.config/VintagestoryData/Mods"
STAGING_DIR="bin/staging"
BUILD_OUTPUT="bin/Debug/Mods/Quartermaster"

echo "--- 1. CLEANING ---"
dotnet clean -v q
rm -rf "$STAGING_DIR"
rm -f "$MOD_NAME.zip"

echo "--- 2. BUILDING (net10.0 / VS 1.22) ---"
dotnet build -c Debug
if [ ! -f "$BUILD_OUTPUT/Quartermaster.dll" ]; then
    echo "ERROR: Build failed — Quartermaster.dll not found."
    exit 1
fi

echo "--- 3. PACKAGING ---"
mkdir -p "$STAGING_DIR"
cp modinfo.json "$STAGING_DIR/"
cp modicon.png "$STAGING_DIR/"
cp "$BUILD_OUTPUT/Quartermaster.dll" "$STAGING_DIR/"
cp -r assets "$STAGING_DIR/"

cd "$STAGING_DIR"
zip -r -q ../../"$MOD_NAME.zip" *
cd ../..

echo "--- 4. DEPLOYING ZIP ---"
# Remove any old version
rm -f "$MODS_PATH/$MOD_NAME.zip"
mv "$MOD_NAME.zip" "$MODS_PATH/"

echo "Deploy Complete: $MODS_PATH/$MOD_NAME.zip"
