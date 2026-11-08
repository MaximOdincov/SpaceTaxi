#!/usr/bin/env python3
# Коллажи иконок для бумажной карты (PaperMapArt):
#   Assets/Resources/Map/map_icons.png  3×3: 0 планета, 1 звезда, 2 дыра, 3 пояс,
#                                       4 комета, 5 буй, 6 корабль, 7 цель, 8 метка
#   Assets/Resources/Map/map_stamps.png 2×3: 0 заправка, 1 мастерская, 2 хаб,
#                                       3 метка-waypoint, 4 потеря связи, 5 резерв
# Белые глифы-иконки перекрашиваются в карандашную палитру карты.
import os, uuid
from PIL import Image

ROOT = "/Users/maxim/Desktop/SpaceTaxi/SpaceTaxiUnity/SpaceTaxi"
ICONS = os.path.join(ROOT, "Assets/Sprites/icons")
OUT = os.path.join(ROOT, "Assets/Resources/Map")
CELL = 256

INK = (61, 48, 38)
STAR = (212, 153, 64)
HOLE = (38, 31, 41)
BELT = (115, 102, 87, 153)
COMET = (102, 128, 158, 235)
RUST = (173, 82, 43)
HERO = (41, 84, 140)
GRAPHITE = (112, 105, 94)
HUB = (217, 166, 51)

# (файл, цвет)
MAP_ICONS = [
    ("planet_icon.png", INK),
    ("sun_icon.png", STAR),
    ("black_hole_icon.png", HOLE),
    ("asteroid's_tail_icon.png", BELT),
    ("comet_icon.png", COMET),
    ("chain.png", RUST),
    ("Group 1000011328.png", HERO),   # шашка такси = корабль
    ("danger_png.png", RUST),         # цель
    ("Group 1000011330.png", HERO),   # стрелки = метка
]
MAP_STAMPS = [
    ("gas.png", RUST),                # заправка (канистра)
    ("knock.png", GRAPHITE),          # мастерская
    ("Group 1000011328.png", HUB),    # хаб — шашка
    ("Group 1000011330.png", HERO),   # waypoint
    ("danger_png.png", RUST),         # потеря связи
    ("Star 2.png", RUST),             # резерв
]

def tinted(name, color):
    img = Image.open(os.path.join(ICONS, name)).convert("RGBA")
    r, g, b = color[0], color[1], color[2]
    a = color[3] if len(color) > 3 else 255
    px = img.load()
    for y in range(img.height):
        for x in range(img.width):
            pr, pg, pb, pa = px[x, y]
            if pa == 0:
                continue
            # белый глиф -> цвет, чёрные детали остаются тёмными
            px[x, y] = (pr * r // 255, pg * g // 255, pb * b // 255,
                        min(pa, a) if len(color) > 3 else pa)
    return img

def compose(cells, cols, rows, path):
    canvas = Image.new("RGBA", (cols * CELL, rows * CELL), (0, 0, 0, 0))
    for i, (name, color) in enumerate(cells):
        cell = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
        icon = tinted(name, color)
        icon.thumbnail((int(CELL * 0.8), int(CELL * 0.8)), Image.LANCZOS)
        cell.paste(icon, ((CELL - icon.width) // 2, (CELL - icon.height) // 2), icon)
        canvas.paste(cell, ((i % cols) * CELL, (i // cols) * CELL))
    os.makedirs(OUT, exist_ok=True)
    canvas.save(path)
    with open(path + ".meta", "w") as f:
        f.write(META.format(guid=uuid.uuid5(uuid.NAMESPACE_URL, "spacetaxi-art:" +
                os.path.relpath(path, ROOT)).hex))
    print("->", os.path.relpath(path, ROOT), canvas.size)

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
  isReadable: 0
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
  spritePixelsToUnits: 256
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

compose(MAP_ICONS, 3, 3, os.path.join(OUT, "map_icons.png"))
compose(MAP_STAMPS, 2, 3, os.path.join(OUT, "map_stamps.png"))
