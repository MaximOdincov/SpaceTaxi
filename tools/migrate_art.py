#!/usr/bin/env python3
# Миграция пользовательских спрайтов в Assets/Resources/Art + генерация .meta.
# GUID детерминированы (uuid5 от пути) — на них ссылаются ShipConfig-ассеты и префабы.
import os, json, uuid

ROOT = "/Users/maxim/Desktop/SpaceTaxi/SpaceTaxiUnity/SpaceTaxi"

MOVES = []  # (src_rel, dst_rel) — пути от корня проекта
ICONS_IN_PLACE = []

def add(src, dst):
    MOVES.append((src, dst))

# планеты (9 вариантов)
for i in range(1, 10):
    add(f"Assets/Sprites/planet/{i}.png", f"Assets/Resources/Art/planets/{i}.png")
# заправки: poor/norm/rich/high = тиры топлива 1..4
for tier in ("poor", "norm", "rich", "high"):
    add(f"Assets/Sprites/planet/Gas station/{tier}.png", f"Assets/Resources/Art/stations/gas_{tier}.png")
# астероиды/кометы (9 кандидатов)
for i in range(1, 10):
    add(f"Assets/Sprites/asteroids/main{i}.png", f"Assets/Resources/Art/asteroids/main{i}.png")
# корабли: папки-модели → Resources/Art/ships/<модель>
for model in ("барон", "дельта", "игла", "кабан", "меридиан", "скрепка", "стрела", "яйцо"):
    src_dir = os.path.join(ROOT, f"Assets/Sprites/rockets/{model}")
    for name in sorted(os.listdir(src_dir)):
        if not name.endswith(".png"):
            continue
        if name.startswith("photo_"):
            continue  # мусорный carve.photos preview — не переносим
        add(f"Assets/Sprites/rockets/{model}/{name}", f"Assets/Resources/Art/ships/{model}/{name}")
# NPC: готовые + слои сборки
add("Assets/Sprites/npc/ready_npc/dispetcher.png", "Assets/Resources/Art/npc/ready/dispetcher.png")
for i in range(1, 12):
    add(f"Assets/Sprites/npc/ready_npc/{i}.png", f"Assets/Resources/Art/npc/ready/{i}.png")
for src_sub, dst_sub in (("body_templates", "body"), ("clother", "clothes"), ("eyes", "eyes"), ("mouth", "mouth")):
    d = os.path.join(ROOT, f"Assets/Sprites/npc/{src_sub}")
    for name in sorted(os.listdir(d)):
        if name.endswith(".png"):
            add(f"Assets/Sprites/npc/{src_sub}/{name}", f"Assets/Resources/Art/npc/{dst_sub}/{name}")
# иконки остаются на месте (ссылаются из префаба по guid, коллаж соберёт PIL)
for name in sorted(os.listdir(os.path.join(ROOT, "Assets/Sprites/icons"))):
    if name.endswith(".png"):
        ICONS_IN_PLACE.append(f"Assets/Sprites/icons/{name}")

META = """fileFormatVersion: 2
guid: {guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
    flipGreenChannel: 0
  isReadable: {readable}
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMipmapLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 50
  spriteMode: 1
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {{x: 0.5, y: 0.5}}
  spritePixelsToUnits: 100
  spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}
  spriteGenerateFallbackPhysicsShape: 1
  alphaUsage: 1
  alphaIsTransparency: 1
  spriteTessellationMethod: 0
  spriteTessellationDetail: -1
  spriteGeometrySubdivision: -1
  textureType: 8
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  swizzle: 50462976
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 1
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    physicsShape: []
  spritePackingTag:
  userData:
  assetBundleName:
  assetBundleVariant:
"""

def guid_for(rel):
    return uuid.uuid5(uuid.NAMESPACE_URL, "spacetaxi-art:" + rel).hex

manifest = {}
for src, dst in MOVES + [(p, p) for p in ICONS_IN_PLACE]:
    # слои NPC читаются кодом (композиция портрета) — isReadable: 1
    readable = 1 if "/npc/" in src and "/ready/" not in src else 0
    g = guid_for(dst)
    manifest[dst] = {"guid": g, "readable": readable}
    dst_abs = os.path.join(ROOT, dst)
    if src != dst:
        os.makedirs(os.path.dirname(dst_abs), exist_ok=True)
        os.rename(os.path.join(ROOT, src), dst_abs)
    with open(dst_abs + ".meta", "w") as f:
        f.write(META.format(guid=g, readable=readable))

# пустые исходные папки убрать (icons остаётся)
for empty in ("Assets/Sprites/asteroids", "Assets/Sprites/npc", "Assets/Sprites/planet", "Assets/Sprites/rockets"):
    p = os.path.join(ROOT, empty)
    for sub, dirs, files in os.walk(p, topdown=False):
        if sub != p and not dirs and not files:
            os.rmdir(sub)
        elif sub != p and not dirs and all(f == ".DS_Store" for f in files):
            for f in files:
                os.remove(os.path.join(sub, f))
            os.rmdir(sub)
    if os.path.isdir(p) and not [f for f in os.listdir(p) if f != ".DS_Store"]:
        leftover = os.listdir(p)
        for f in leftover:
            os.remove(os.path.join(p, f))
        os.rmdir(p)

with open(os.path.join(ROOT, "tools/art_manifest.json"), "w") as f:
    json.dump(manifest, f, indent=1, ensure_ascii=False)

print(f"moved+meta: {len(MOVES)}, icons meta: {len(ICONS_IN_PLACE)}")
for src, dst in MOVES[:3]:
    print(" ", src, "->", dst)
