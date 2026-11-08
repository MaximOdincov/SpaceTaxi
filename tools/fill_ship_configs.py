#!/usr/bin/env python3
# Вписать ShipSprite + PaintVariants в ShipConfig-ассеты из art_manifest.json.
import json, os, re

ROOT = "/Users/maxim/Desktop/SpaceTaxi/SpaceTaxiUnity/SpaceTaxi"
manifest = json.load(open(os.path.join(ROOT, "tools/art_manifest.json")))

def ref(rel):
    return "{fileID: 2800000, guid: %s, type: 3}" % manifest["Assets/Resources/Art/" + rel]["guid"]

def variants(model):
    d = os.path.join(ROOT, "Assets/Resources/Art/ships", model)
    def nat(name):
        m = re.search(r"(\d+)", name)
        return int(m.group(1)) if m else 0
    return [f"ships/{model}/{n}" for n in sorted((f for f in os.listdir(d) if f.endswith(".png")), key=nat)]

MODELS = {
    "barin": "барон", "delta": "дельта", "igla": "игла", "kaban": "кабан",
    "meridian": "меридиан", "skrepka": "скрепка", "strela": "стрела",
    "egg_rental": "яйцо", "egg_own": "яйцо",
}

for asset_id, model in MODELS.items():
    path = os.path.join(ROOT, "Assets/ScriptableObjects/Resources/Ships", asset_id + ".asset")
    text = open(path, encoding="utf-8").read()
    files = variants(model)
    block = "  ShipSprite: " + ref(files[0]) + "\n  PaintVariants:"
    block += "".join("\n  - " + ref(f) for f in files[1:])
    if "ShipSprite:" in text:
        # повторный запуск — заменить прежний блок
        text = re.sub(r"  ShipSprite:.*?(?=  Price:)", block + "\n", text, flags=re.S)
    else:
        text = text.replace("  Price:", block + "\n  Price:", 1)
    open(path, "w", encoding="utf-8").write(text)
    print(asset_id, "<-", model, len(files), "вариантов")
