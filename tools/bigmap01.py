#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Инструмент левел-дизайнера Space Taxi: проверка карты + генерация LevelAsset.

Физика проекта (01_MECHANICS.md, PhysicsConfig):
  гравитация  a = min(26000 * r * mult / d^2, 240)
  посадочное кольцо  r + 90        (тяга x3.2 внутри)
  кольцо качели      r * 3.3 + 30  (буст x1.15)
  v_круг = sqrt(g/d), v_спас = sqrt(2g/d), g = 26000 * r * mult
  MAXV = 330, THRUST = 95, RETRO = 115

Запуск:  python3 bigmap01.py          — отчёт-проверка
         python3 bigmap01.py --emit   — ещё и записать BigMap01.asset (+meta)
"""
import math
import sys
import uuid

# ---------------- константы физики ----------------
K = 26000.0
CLAMP = 240.0
MAXV = 330.0
LAND_RING = 90.0
THRUST = 95.0
RETRO = 115.0
ENG_MUL = 3.2

MIN_CENTER_DIST = 300.0   # цель между телами (центр-центр)
HARD_MIN_DIST = 260.0     # минимум по прототипу
HEAT_MARGIN = 50.0        # желаемый зазор жар-планета за вычетом радиусов
HEAT_FROM_SPAWN = 550.0   # правило прототипа: край жара не ближе 550 к спавну

# ---------------- карта BigMap01 ----------------
# (имя, тип P/S, x, y, r, mass, heat, сектор, роль)
MAP = [
    # ХАБ
    ("Гавань",    "P",     0,     0, 30, 1.0,   0, "ХАБ", "старт, респавн"),
    # БЛИЖНИЙ — учебное кольцо r~460-475
    ("Колыбель",  "P",   430,  160, 20, 1.0,   0, "БЛИЖНИЙ", "первый прыжок"),
    ("Мишень",    "P",   240, -400, 18, 1.0,   0, "БЛИЖНИЙ", "учебная посадка"),
    ("Тихая",     "P",  -330, -330, 22, 1.0,   0, "БЛИЖНИЙ", "запад кольца"),
    ("Луг",       "P",  -460,  120, 24, 1.0,   0, "БЛИЖНИЙ", "северо-запад кольца"),
    ("Фонарь",    "P",  -180,  440, 16, 0.0 + 1, 0, "БЛИЖНИЙ", "север кольца"),
    ("Одуванчик", "P",   250,  420, 20, 1.0,   0, "БЛИЖНИЙ", "северо-восток кольца"),
    ("Утренняя",  "S",   920,  880, 52, 4.5, 187, "БЛИЖНИЙ", "первая жаровая зона"),
    # СРЕДНИЙ-1 — восточный куст
    ("Веста",     "P",  1250,  350, 28, 1.0,   0, "СРЕДНИЙ-1", "ворота на восток"),
    ("Кузня",     "P",  1950,  750, 32, 1.0,   0, "СРЕДНИЙ-1", "тяжёлая посадка"),
    ("Янтарь",    "P",  2450,  150, 24, 1.0,   0, "СРЕДНИЙ-1", "восточный край"),
    ("Грань",     "P",  2150, -550, 30, 1.0,   0, "СРЕДНИЙ-1", "юг куста"),
    ("Сито",      "P",  1450, -900, 22, 1.0,   0, "СРЕДНИЙ-1", "юго-запад куста"),
    ("Толока",    "P",   850, -250, 26, 1.0,   0, "СРЕДНИЙ-1", "вход из ближнего"),
    ("Факел",     "S",  2900,-1500, 54, 4.5, 194, "СРЕДНИЙ-1", "жар у юго-восточной трассы"),
    # СРЕДНИЙ-2 — северный куст
    ("Буй",       "P",  1500, 1900, 16, 1.0,   0, "СРЕДНИЙ-2", "релей северных ворот"),
    ("Полынь",    "P",   900, 3100, 26, 1.0,   0, "СРЕДНИЙ-2", "вход куста"),
    ("Стужа",     "P",   150, 3600, 30, 1.0,   0, "СРЕДНИЙ-2", "центр севера"),
    ("Гнездо",    "P",  -650, 4000, 24, 1.0,   0, "СРЕДНИЙ-2", "дальний север"),
    ("Ворон",     "P", -1500, 3400, 34, 1.0,   0, "СРЕДНИЙ-2", "тяжёлая, северо-запад"),
    ("Тоннель",   "P",  -900, 2600, 26, 1.0,   0, "СРЕДНИЙ-2", "обратная дорога"),
    ("Полярная",  "S",  1500, 3800, 58, 4.5, 209, "СРЕДНИЙ-2", "соблазн срезать (Буй-Полынь)"),
    # ДАЛЬНИЙ — северо-западный куст, без станций (будущее)
    ("М-14",      "P", -2800, 3900, 18, 1.0,   0, "ДАЛЬНИЙ", "релей входа"),
    ("Д-2",       "P", -3700, 4900, 32, 1.0,   0, "ДАЛЬНИЙ", "первая дальняя"),
    ("Д-4/Бис",   "P", -4900, 4600, 38, 1.0,   0, "ДАЛЬНИЙ", "тяжёлая, сердцедалека"),
    ("К-77",      "P", -5300, 3400, 28, 1.0,   0, "ДАЛЬНИЙ", "западный форпост"),
    ("С-9",       "P", -4600, 2300, 24, 1.0,   0, "ДАЛЬНИЙ", "южный форпост"),
    ("Горн",      "S", -4300, 3700, 62, 4.5, 150, "ДАЛЬНИЙ", "сердцедалека: жар вручную 150 (3.6r=223) — заход в жар почти без возврата"),
]

# Спавн: Landed на Гавани (индекс 0), угол 90 (север). Отступ корабля 7 (WorldBuilder).
START_INDEX = 0
SPAWN_ANGLE = 90.0
SHIP_OFFSET = 7.0

# «Золотой путь» прогрессии
GOLD = ["Гавань", "Мишень", "Толока", "Сито", "Грань", "Янтарь", "Кузня", "Буй",
        "Полынь", "Стужа", "Гнездо", "Ворон", "М-14", "Д-2", "Д-4/Бис", "К-77", "С-9"]
# Учебное кольцо ближнего сектора
NOVICE = ["Гавань", "Колыбель", "Одуванчик", "Фонарь", "Луг", "Тихая", "Мишень", "Гавань"]


def gparam(b):
    return K * b[4] * b[5]


def dist(a, b):
    return math.hypot(a[2] - b[2], a[3] - b[3])


def report():
    errors, warnings = [], []
    planets = [b for b in MAP if b[1] == "P"]
    stars = [b for b in MAP if b[1] == "S"]
    hub = MAP[START_INDEX]
    spawn = (hub[2], hub[3])

    print("=" * 78)
    print("ПРОВЕРКА КАРТЫ: %d планет + %d звёзд = %d тел" % (len(planets), len(stars), len(MAP)))
    print("=" * 78)

    # 1. Попарные дистанции
    pairs = []
    for i in range(len(MAP)):
        for j in range(i + 1, len(MAP)):
            d = dist(MAP[i], MAP[j])
            pairs.append((d, MAP[i][0], MAP[j][0]))
    pairs.sort()
    bad = [p for p in pairs if p[0] < MIN_CENTER_DIST]
    print("\n[1] Попарные дистанции (центр-центр): %d пар" % len(pairs))
    print("    минимум: %.0f  (%s — %s)" % pairs[0])
    print("    максимум: %.0f  (%s — %s)" % pairs[-1])
    for d, a, b in bad:
        msg = "дистанция %.0f < %d: %s — %s" % (d, MIN_CENTER_DIST, a, b)
        (errors if d < HARD_MIN_DIST else warnings).append(msg)
    if bad:
        for d, a, b in bad:
            print("    !! %.0f  %s — %s" % (d, a, b))
    else:
        print("    ok: все >= %.0f" % MIN_CENTER_DIST)

    # 2. Жар vs планеты
    print("\n[2] Жаровые зоны vs планеты (зазор = d - heat - r_planeta):")
    for s in stars:
        worst = min(((dist(s, p) - s[6] - p[4], p[0]) for p in planets), key=lambda t: t[0])
        print("    %-10s heat=%3.0f: ближайшая %-10s, зазор %.0f" % (s[0], s[6], worst[1], worst[0]))
        if worst[0] < 0:
            errors.append("жар %s задевает %s" % (s[0], worst[1]))
        elif worst[0] < HEAT_MARGIN:
            warnings.append("малый зазор жар %s — %s: %.0f" % (s[0], worst[1], worst[0]))

    # 3. Жар vs спавн (правило 550)
    print("\n[3] Край жара до спавна (правило >= %.0f):" % HEAT_FROM_SPAWN)
    for s in stars:
        edge = dist(s, ("x", "x", spawn[0], spawn[1], 0, 1, 0)) - s[6] - hub[4]
        print("    %-10s: %.0f" % (s[0], edge))
        if edge < HEAT_FROM_SPAWN:
            errors.append("жар %s ближе %.0f к спавну: %.0f" % (s[0], HEAT_FROM_SPAWN, edge))

    # 4. Звёзды: граница невозврата
    print("\n[4] Звёзды (g = %.0f, граница невозврата: v_спас = MAXV %.0f):" % (K, MAXV))
    for s in stars:
        g = gparam(s)
        d_nr = 2 * g / MAXV ** 2
        margin = s[6] - d_nr
        note = "окунуться можно" if margin > 40 else ("ОПАСНО: жар почти = невозврат" if margin > 0 else "СМЕРТЬ: невозврат внутри жара")
        print("    %-10s g=%9.0f  невозврат=%6.1f  жар=%3.0f  запас=%5.1f  %s"
              % (s[0], g, d_nr, s[6], margin, note))

    # 5. Планеты: орбитальные скорости и внешние силы
    print("\n[5] Планеты (v на краю посадочного кольца r+90; тяга в кольце %.0f):" % (THRUST * ENG_MUL))
    print("    %-10s %5s %7s %7s  %-22s %6s" % ("имя", "r", "v_круг", "v_спас", "сильнейший сосед", "ус/с²"))
    for p in planets:
        ring = p[4] + LAND_RING
        g = gparam(p)
        vc = math.sqrt(g / ring)
        ve = math.sqrt(2 * g / ring)
        ext = []
        for o in MAP:
            if o is p:
                continue
            a = min(gparam(o) / dist(p, o) ** 2, CLAMP)
            ext.append((a, o[0]))
        ext.sort(reverse=True)
        print("    %-10s %5d %7.0f %7.0f  %-22s %6.1f" % (p[0], p[4], vc, ve, ext[0][1], ext[0][0]))
        if ext[0][0] > CLAMP * 0.25:
            warnings.append("у %s сосед %s тянет %.0f усов/с² на поверхности" % (p[0], ext[0][1], ext[0][0]))

    # 6. Маршруты
    idx = {b[0]: b for b in MAP}
    print("\n[6] Маршруты:")
    for label, route in (("учебное кольцо", NOVICE), ("золотой путь", GOLD)):
        total = 0
        hops = []
        for a, b in zip(route, route[1:]):
            d = dist(idx[a], idx[b])
            total += d
            hops.append("%s→%s %.0f" % (a, b, d))
        print("    %s (%d ед.):" % (label, total))
        print("      " + ", ".join(hops))

    # 7. Границы мира
    xs = [(b[2] - max(b[4], b[6]), b[2] + max(b[4], b[6])) for b in MAP]
    ys = [(b[3] - max(b[4], b[6]), b[3] + max(b[4], b[6])) for b in MAP]
    x0, x1 = min(x[0] for x in xs), max(x[1] for x in xs)
    y0, y1 = min(y[0] for y in ys), max(y[1] for y in ys)
    print("\n[7] Границы мира: X %.0f…%.0f  Y %.0f…%.0f  (размер %.0f x %.0f)" % (x0, x1, y0, y1, x1 - x0, y1 - y0))
    rmax = max(math.hypot(b[2], b[3]) + max(b[4], b[6]) for b in MAP)
    print("    макс. радиус от хаба: %.0f" % rmax)

    print("\n" + "=" * 78)
    if errors:
        print("ОШИБКИ (%d):" % len(errors))
        for e in errors:
            print("  !! " + e)
    if warnings:
        print("Предупреждения (%d):" % len(warnings))
        for w in warnings:
            print("  ?  " + w)
    if not errors and not warnings:
        print("ВСЕ ПРОВЕРКИ ПРОЙДЕНЫ")
    elif not errors:
        print("Блокирующих ошибок нет")
    print("=" * 78)
    return len(errors) == 0


def esc(s):
    return '"' + "".join("\\u%04x" % ord(c) if ord(c) > 127 else c for c in s) + '"'


def emit():
    asset = """%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 0}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: ca1042bad44ab4065bb95d40c109612c, type: 3}
  m_Name: BigMap01
  m_EditorClassIdentifier: SpaceTaxi.Gameplay::SpaceTaxi.World.LevelAsset
  LevelName: BigMap01
  Bodies:
"""
    entries = []
    for b in MAP:
        entries.append("""  - DisplayName: %s
    Type: %d
    Position: {x: %d, y: %d}
    Radius: %d
    MassMultiplier: %s
    HeatRadius: %d
    Visuals:
      Sprite: {fileID: 0}
      Material: {fileID: 0}
      UseColor: 0
      Color: {r: 1, g: 1, b: 1, a: 1}
      SpinSpeed: 0
    LandingRingRadius: 0
    CaptureRingRadius: 0
""" % (esc(b[0]), 0 if b[1] == "P" else 1, b[2], b[3], b[4],
       ("%g" % b[5]), b[6]))
    tail = """  SpawnMode: 0
  StartPlanetIndex: %d
  SpawnAngleDeg: %g
  SpawnPoint: {x: 0, y: 0}
  SpawnHeading: 0
""" % (START_INDEX, SPAWN_ANGLE)
    # ВНИМАНИЕ: путь захардкожен под этот Mac — на другой машине заменить на реальный путь проекта.
    base = "/Users/maxim/Desktop/SpaceTaxi/SpaceTaxiUnity/SpaceTaxi/Assets/ScriptableObjects/BigMap01.asset"
    meta = """fileFormatVersion: 2
guid: %s
NativeFormatImporter:
  externalObjects: {}
  mainObjectFileID: 11400000
  userData:
  assetBundleName:
  assetBundleVariant:
""" % uuid.uuid4().hex
    with open(base, "w") as f:
        f.write(asset + "".join(entries) + tail)
    with open(base + ".meta", "w") as f:
        f.write(meta)
    print("\nЗаписан: %s (+meta)" % base)


if __name__ == "__main__":
    ok = report()
    if "--emit" in sys.argv:
        if ok:
            emit()
        else:
            print("Ассет НЕ записан: есть блокирующие ошибки")
            sys.exit(1)
