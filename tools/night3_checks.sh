#!/bin/zsh
# Ночь 3 — цепочка проверок Н3.0. Все вызовы с -quit (без него batchmode виснет).
# ВНИМАНИЕ: U/P/D захардкожены под этот Mac (/Users/maxim, Unity 6000.6.0f1) — на другой машине поправить пути.
U="/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/MacOS/Unity"
P="/Users/maxim/Desktop/SpaceTaxi/SpaceTaxiUnity/SpaceTaxi"
D="/Users/maxim/Desktop/SpaceTaxi"

echo "=== 1/5 COMPILE ==="
"$U" -projectPath "$P" -batchmode -quit -nographics -logFile "$D/night_compile3.log"; echo "COMPILE_EXIT: $?"
echo "=== 2/5 REBUILD PREFAB ==="
"$U" -projectPath "$P" -batchmode -quit -nographics -executeMethod SpaceTaxi.Tools.OnboardComputerBuilder.RebuildPrefab -logFile "$D/night3_rebuild.log"; echo "REBUILD_EXIT: $?"
echo "=== 3/5 COMPILE AFTER REBUILD ==="
"$U" -projectPath "$P" -batchmode -quit -nographics -logFile "$D/night3_compile_post_rebuild.log"; echo "COMPILE2_EXIT: $?"
echo "=== 4/5 ARTSELFCHECK ==="
"$U" -projectPath "$P" -batchmode -quit -nographics -executeMethod SpaceTaxi.EditorTools.ArtSelfCheck.RunFromCLI -logFile "$D/night3_selfcheck.log"; echo "SELFCHECK_EXIT: $?"
echo "=== 5/5 SHIPPROBE ==="
"$U" -projectPath "$P" -batchmode -quit -nographics -executeMethod SpaceTaxi.EditorTools.ShipProbe.Run -logFile "$D/night3_shipprobe.log"; echo "SHIPPROBE_EXIT: $?"
echo "=== CHAIN DONE ==="
