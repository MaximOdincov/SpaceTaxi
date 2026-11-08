#!/usr/bin/env python3
# Префабы моделей кораблей (Assets/Prefabs/Resources/Ships/<id>.prefab):
# SpriteRenderer со спрайтом варианта 1 + пустые слоты ExhaustMain/ExhaustRetro.
# Позиции слотов владелец правит в префабе — EngineEffects подхватывает по имени.
# Заодно: PPU спрайтов кораблей = maxDim/4.6667 (спрайт = 4.67 юнита при корне x3 = 14).
import json, os, re, uuid
from PIL import Image

ROOT = "/Users/maxim/Desktop/SpaceTaxi/SpaceTaxiUnity/SpaceTaxi"
manifest = json.load(open(os.path.join(ROOT, "tools/art_manifest.json")))

def guid_for(rel, salt):
    return uuid.uuid5(uuid.NAMESPACE_URL, salt + rel).hex

MODELS = {
    "barin": "барон", "delta": "дельта", "igla": "игла", "kaban": "кабан",
    "meridian": "меридиан", "skrepka": "скрепка", "strela": "стрела",
    "egg_rental": "яйцо", "egg_own": "яйцо",
}

# --- PPU корабельных спрайтов: длинная сторона = 4.6667 локальных юнита ---
def nat(name):
    m = re.search(r"(\d+)", name)
    return int(m.group(1)) if m else 0

for asset_id, model in MODELS.items():
    d = os.path.join(ROOT, "Assets/Resources/Art/ships", model)
    for name in sorted((f for f in os.listdir(d) if f.endswith(".png")), key=nat):
        rel = f"Assets/Resources/Art/ships/{model}/{name}"
        w, h = Image.open(os.path.join(ROOT, rel)).size
        ppu = round(max(w, h) / 4.6667)
        meta_path = os.path.join(ROOT, rel + ".meta")
        meta = open(meta_path, encoding="utf-8").read()
        meta = re.sub(r"spritePixelsToUnits: \d+", f"spritePixelsToUnits: {ppu}", meta)
        open(meta_path, "w", encoding="utf-8").write(meta)

# --- префабы ---
PREFAB = """%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1 &100000
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  serializedVersion: 6
  m_Component:
  - component: {{fileID: 200000}}
  - component: {{fileID: 300000}}
  m_Layer: 0
  m_Name: ModelVisual
  m_TagString: Untagged
  m_Icon: {{fileID: 0}}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!4 &200000
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 100000}}
  serializedVersion: 2
  m_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}
  m_LocalPosition: {{x: 0, y: 0, z: 0}}
  m_LocalScale: {{x: 1, y: 1, z: 1}}
  m_ConstrainProportionsScale: 0
  m_Children:
  - {{fileID: 400000}}
  - {{fileID: 500000}}
  m_Father: {{fileID: 0}}
  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: 0}}
--- !u!212 &300000
SpriteRenderer:
  serializedVersion: 3
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 100000}}
  m_Enabled: 1
  m_CastShadows: 0
  m_ReceiveShadows: 0
  m_DynamicOccludee: 1
  m_StaticShadowCaster: 0
  m_MotionVectors: 1
  m_LightProbeUsage: 1
  m_ReflectionProbeUsage: 0
  m_RayTracingMode: 0
  m_RayTraceProcedural: 0
  m_RenderingLayerMask: 1
  m_RendererPriority: 0
  m_Materials:
  - {{fileID: -876546973899608171, guid: bde135b320597544b9ac325092409f8b, type: 3}}
  m_StaticBatchInfo:
    firstSubMesh: 0
    subMeshCount: 0
  m_StaticBatchRoot: {{fileID: 0}}
  m_ProbeAnchor: {{fileID: 0}}
  m_LightProbeVolumeOverride: {{fileID: 0}}
  m_ScaleInLightmap: 1
  m_ReceiveGI: 1
  m_PreserveUVs: 0
  m_IgnoreNormalsForChartDetection: 0
  m_ImportantGI: 0
  m_StitchLightmapSeams: 1
  m_SelectedEditorRenderState: 3
  m_MinimumChartSize: 4
  m_AutoUVMaxDistance: 0.5
  m_AutoUVMaxAngle: 89
  m_LightmapParameters: {{fileID: 0}}
  m_SortingLayerID: 0
  m_SortingLayer: 0
  m_SortingOrder: 2
  m_MaskInteraction: 0
  m_Sprite: {{fileID: 2800000, guid: {sprite_guid}, type: 3}}
  m_Color: {{r: 1, g: 1, b: 1, a: 1}}
  m_FlipX: 0
  m_FlipY: 0
  m_DrawMode: 0
  m_Size: {{x: 1, y: 1}}
  m_AdaptiveModeThreshold: 0.5
  m_SpriteTileMode: 0
  m_WasSpriteAssigned: 1
  m_SpriteSortPoint: 0
  m_BlendShapeWeights: []
--- !u!1 &600000
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  serializedVersion: 6
  m_Component:
  - component: {{fileID: 400000}}
  m_Layer: 0
  m_Name: ExhaustMain
  m_TagString: Untagged
  m_Icon: {{fileID: 0}}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!4 &400000
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 600000}}
  serializedVersion: 2
  m_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}
  m_LocalPosition: {{x: 0, y: -2.3, z: 0}}
  m_LocalScale: {{x: 1, y: 1, z: 1}}
  m_ConstrainProportionsScale: 0
  m_Children: []
  m_Father: {{fileID: 200000}}
  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: 0}}
--- !u!1 &700000
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  serializedVersion: 6
  m_Component:
  - component: {{fileID: 500000}}
  m_Layer: 0
  m_Name: ExhaustRetro
  m_TagString: Untagged
  m_Icon: {{fileID: 0}}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!4 &500000
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 700000}}
  serializedVersion: 2
  m_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}
  m_LocalPosition: {{x: 0, y: 2.5, z: 0}}
  m_LocalScale: {{x: 1, y: 1, z: 1}}
  m_ConstrainProportionsScale: 0
  m_Children: []
  m_Father: {{fileID: 200000}}
  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: 0}}
"""

PREFAB_META = """fileFormatVersion: 2
guid: {guid}
PrefabImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""

out_dir = os.path.join(ROOT, "Assets/Prefabs/Resources/Ships")
os.makedirs(out_dir, exist_ok=True)
for asset_id, model in MODELS.items():
    files = sorted((f for f in os.listdir(os.path.join(ROOT, "Assets/Resources/Art/ships", model)) if f.endswith(".png")), key=nat)
    sprite_guid = manifest[f"Assets/Resources/Art/ships/{model}/{files[0]}"]["guid"]
    path = os.path.join(out_dir, asset_id + ".prefab")
    open(path, "w", encoding="utf-8").write(PREFAB.format(sprite_guid=sprite_guid))
    open(path + ".meta", "w", encoding="utf-8").write(
        PREFAB_META.format(guid=guid_for("Assets/Prefabs/Resources/Ships/" + asset_id + ".prefab", "spacetaxi-prefab:")))
    print(asset_id, "<-", files[0])
print("PPU кораблей обновлён (длинная сторона = 4.67 юнита)")
