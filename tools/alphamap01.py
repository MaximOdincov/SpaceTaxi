#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Инструмент левел-дизайнера Space Taxi: альфа-карта AlphaMap01 («Файловая система»).

Тема: планеты названы командами Linux (транслит), звёзды — «разрушители» (Рм, Дд).
Проверка + генерация LevelAsset. Физика проекта:
  a = min(26000*r*mult/d^2, 240); кольцо посадки r+90; качель r*3.3+30;
  v_круг = sqrt(g/d); MAXV = 330; бак 100, расход 5.5/с только на тяге
  (разгон до MAXV + торможение ~35 топлива на пролёт любой длины —
   топливо тратится на манёвры, не на километры)

Запуск:  python3 alphamap01.py          — отчёт-проверка
         python3 alphamap01.py --emit   — ещё и записать AlphaMap01.asset (+meta)
"""
import math
import os
import re
import sys
import uuid

K = 26000.0
CLAMP = 240.0
MAXV = 330.0
LAND_RING = 90.0
THRUST = 95.0
ENG_MUL = 3.2
FUEL_MAX = 100.0

MIN_CENTER_DIST = 300.0
HEAT_MARGIN = 50.0
HEAT_FROM_SPAWN = 550.0
BORDER_MARGIN = 100.0        # тела не ближе этого к границе мира
STATION_REACH_WARN = 1800.0  # прямой маршрут до ближайшей станции на одном баке

WORLD_RADIUS = 2600.0

# (имя, тип P/S, x, y, r, mass, heat, станция, сектор, роль)
# v2 (2026-09-29, правка координатора): планеты плотнее (~15%) и крупнее (+4..+8),
# звёзды и их жар без изменений; WorldRadius 2600 оставлен (больше воздуха у краёв)
MAP = [
    # ХАБ и учебное кольцо
    ("Рут",     "P",     0,     0, 36, 1.0,   0, 1, "ХОУМ", "хаб-каталог, старт, станция"),
    ("Ман",     "P",   540,  200, 24, 1.0,   0, 1, "ХОУМ", "учебная (мануал), станция"),
    ("Кэт",     "P",  -450,  330, 22, 1.0,   0, 0, "ХОУМ", "запад кольца"),
    ("Пвд",     "P",  -230,  590, 26, 1.0,   0, 0, "ХОУМ", "север кольца («ты здесь»)"),
    ("Апропос", "P",  -550, -210, 26, 1.0,   0, 0, "ХОУМ", "юго-запад кольца"),
    ("Лс",      "P",   210, -480, 20, 1.0,   0, 0, "ХОУМ", "юг кольца, мелкая"),
    # Восток: конвейер обработки текста
    ("Пинг",    "P",   860,   90, 22, 1.0,   0, 0, "БИН", "релей (эхо)"),
    ("Греп",    "P",  1200,  350, 30, 1.0,   0, 0, "БИН", "вход конвейера"),
    ("Сед",     "P",  1600,  700, 28, 1.0,   0, 1, "БИН", "середина конвейера, станция"),
    ("Аук",     "P",  1850,  180, 26, 1.0,   0, 0, "БИН", "конец конвейера, край карты"),
    # Северо-запад: права и редактор
    ("Чмод",    "P", -1150,  740, 32, 1.0,   0, 0, "ЕТС", "права, близнец Човна"),
    ("Човн",    "P", -1450, 1030, 28, 1.0,   0, 0, "ЕТС", "права, близнец Чмода"),
    ("Вим",     "P", -1060, 1400, 30, 1.0,   0, 1, "ЕТС", "СЗ форпост, станция; соперник Эмакса"),
    # Юг и юго-восток: дисковый карман без станций
    ("Фдиск",   "P",   600,-1100, 38, 1.0,   0, 0, "ВАР", "диски: тяжёлая, без станции"),
    ("Мкфс",    "P",   170,-1400, 34, 1.0,   0, 0, "ВАР", "диски: форматирование, без станции"),
    ("Эмакс",   "P",  1400, -950, 28, 1.0,   0, 0, "ВАР", "ЮВ форпост; соперник Вима"),
    # Звёзды-разрушители
    ("Рм",      "S",  2050, -400, 50, 4.5, 180, 0, "ОПАСНОСТЬ", "rm: стирает всё, охраняет ЮВ угол"),
    ("Дд",      "S",  -300,-1800, 55, 4.5, 198, 0, "ОПАСНОСТЬ", "dd: разрушитель дисков, юг"),
]

START_INDEX = 0
SPAWN_ANGLE = 90.0
SHIP_OFFSET = 7.0


def gparam(b):
    return K * b[4] * b[5]


def dist(a, b):
    return math.hypot(a[2] - b[2], a[3] - b[3])


def report():
    errors, warnings = [], []
    planets = [b for b in MAP if b[1] == "P"]
    stars = [b for b in MAP if b[1] == "S"]
    stations = [b for b in MAP if b[7] == 1]
    hub = MAP[START_INDEX]

    print("=" * 78)
    print("АЛЬФА-КАРТА AlphaMap01 «Файловая система»: %d планет + %d звёзд, станций: %d"
          % (len(planets), len(stars), len(stations)))
    print("=" * 78)

    # 1. Попарные дистанции
    pairs = sorted((dist(MAP[i], MAP[j]), MAP[i][0], MAP[j][0])
                   for i in range(len(MAP)) for j in range(i + 1, len(MAP)))
    print("\n[1] Попарные дистанции: %d пар, мин %.0f (%s—%s), макс %.0f (%s—%s)"
          % (len(pairs), pairs[0][0], pairs[0][1], pairs[0][2],
             pairs[-1][0], pairs[-1][1], pairs[-1][2]))
    for d, a, b in pairs:
        if d < MIN_CENTER_DIST:
            msg = "дистанция %.0f < %.0f: %s — %s" % (d, MIN_CENTER_DIST, a, b)
            (errors if d < 260 else warnings).append(msg)
            print("    !! %.0f  %s — %s" % (d, a, b))
    if all(p[0] >= MIN_CENTER_DIST for p in pairs):
        print("    ok: все >= %.0f" % MIN_CENTER_DIST)

    # 2. Жар vs планеты
    print("\n[2] Жар vs планеты (зазор = d - жар - r):")
    for s in stars:
        worst = min(((dist(s, p) - s[6] - p[4], p[0]) for p in planets), key=lambda t: t[0])
        print("    %-6s heat=%3.0f: ближайшая %-6s зазор %.0f" % (s[0], s[6], worst[1], worst[0]))
        if worst[0] < 0:
            errors.append("жар %s задевает %s" % (s[0], worst[1]))
        elif worst[0] < HEAT_MARGIN:
            warnings.append("малый зазор жар %s — %s: %.0f" % (s[0], worst[1], worst[0]))

    # 3. Жар vs спавн
    print("\n[3] Край жара до спавна (норма >= %.0f):" % HEAT_FROM_SPAWN)
    for s in stars:
        edge = math.hypot(s[2] - hub[2], s[3] - hub[3]) - s[6] - hub[4]
        print("    %-6s: %.0f" % (s[0], edge))
        if edge < HEAT_FROM_SPAWN:
            errors.append("жар %s ближе %.0f к спавну" % (s[0], HEAT_FROM_SPAWN))

    # 4. Граница мира
    print("\n[4] Граница мира %.0f (тела с зонами должны быть внутри с запасом %.0f):"
          % (WORLD_RADIUS, BORDER_MARGIN))
    for b in MAP:
        edge = math.hypot(b[2], b[3]) + max(b[4], b[6])
        room = WORLD_RADIUS - edge
        flag = "ok" if room >= BORDER_MARGIN else ("! МЕСТО" if room >= 0 else "!! НАРУШЕНИЕ")
        print("    %-6s край %6.0f, до границы %6.0f  %s" % (b[0], edge, room, flag))
        if room < 0:
            errors.append("%s выходит за границу мира" % b[0])
        elif room < BORDER_MARGIN:
            warnings.append("%s ближе %.0f к границе" % (b[0], BORDER_MARGIN))

    # 5. Звёзды: невозврат
    print("\n[5] Звёзды (невозврат: v_спас = MAXV %.0f):" % MAXV)
    for s in stars:
        g = gparam(s)
        d_nr = 2 * g / MAXV ** 2
        print("    %-6s g=%8.0f невозврат=%5.1f жар=%3.0f запас=%4.1f (%s)"
              % (s[0], g, d_nr, s[6], s[6] - d_nr,
                 "окунуться можно" if s[6] - d_nr > 40 else "ОПАСНО"))

    # 6. Планеты: орбиты и соседи
    print("\n[6] Планеты (v на краю кольца r+90; тяга в кольце %.0f):" % (THRUST * ENG_MUL))
    print("    %-6s %3s %6s %6s  %-12s %5s" % ("имя", "r", "v_кр", "v_сп", "сосед", "ус/с²"))
    for p in planets:
        ring = p[4] + LAND_RING
        g = gparam(p)
        ext = sorted(((min(gparam(o) / dist(p, o) ** 2, CLAMP), o[0])
                      for o in MAP if o is not p), reverse=True)
        print("    %-6s %3d %6.0f %6.0f  %-12s %5.1f"
              % (p[0], p[4], math.sqrt(g / ring), math.sqrt(2 * g / ring), ext[0][1], ext[0][0]))

    # 7. Станции: достижимость на одном баке (прямой маршрут)
    print("\n[7] Станции (%s) — прямой маршрут до ближайшей:" % ", ".join(s[0] for s in stations))
    worst = 0
    for p in planets:
        if p[7]:
            print("    %-6s — станция" % p[0])
            continue
        d, name = min(((dist(p, s), s[0]) for s in stations), key=lambda t: t[0])
        worst = max(worst, d)
        print("    %-6s → %-6s %5.0f" % (p[0], name, d))
    print("    максимум до станции: %.0f (порог предупреждения %.0f, бак %d)"
          % (worst, STATION_REACH_WARN, FUEL_MAX))
    if worst > STATION_REACH_WARN:
        warnings.append("до станции дальше %.0f: %.0f" % (STATION_REACH_WARN, worst))

    # 8. Разброс заказов (А→Б случайно по всем планетам)
    pp = sorted((dist(a, b), a[0], b[0])
                for i, a in enumerate(planets) for b in planets[i + 1:])
    print("\n[8] Заказы А→Б: ближний %.0f (%s→%s), дальний %.0f (%s→%s)"
          % (pp[0][0], pp[0][1], pp[0][2], pp[-1][0], pp[-1][1], pp[-1][2]))

    # 9. Ключевые маршруты
    idx = {b[0]: b for b in MAP}
    print("\n[9] Ключевые маршруты:")
    routes = [
        ("учебный круг", ["Рут", "Ман", "Пинг", "Лс", "Апропос", "Кэт", "Пвд", "Рут"]),
        ("конвейер текста", ["Кэт", "Пинг", "Греп", "Сед", "Аук"]),
        ("в гости к Виму", ["Рут", "Кэт", "Чмод", "Човн", "Вим"]),
        ("дисковый карман", ["Рут", "Лс", "Фдиск", "Мкфс", "Эмакс"]),
        ("срез мимо Рм", ["Аук", "Фдиск"]),
        ("война редакторов", ["Вим", "Эмакс"]),
    ]
    for label, r in routes:
        hops = ", ".join("%s→%s %.0f" % (a, b, dist(idx[a], idx[b])) for a, b in zip(r, r[1:]))
        print("    %s: %s" % (label, hops))

    xs = [(b[2] - max(b[4], b[6]), b[2] + max(b[4], b[6])) for b in MAP]
    ys = [(b[3] - max(b[4], b[6]), b[3] + max(b[4], b[6])) for b in MAP]
    print("\n[10] Границы: X %.0f…%.0f  Y %.0f…%.0f" %
          (min(x[0] for x in xs), max(x[1] for x in xs),
           min(y[0] for y in ys), max(y[1] for y in ys)))

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
    head = """%%YAML 1.1
%%TAG !u! tag:unity3d.com,2011:
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
  m_Name: AlphaMap01
  m_EditorClassIdentifier: SpaceTaxi.Gameplay::SpaceTaxi.World.LevelAsset
  LevelName: AlphaMap01
  WorldRadius: %g
  Bodies:
""" % WORLD_RADIUS
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
    HasStation: %d
    LandingRingRadius: 0
    CaptureRingRadius: 0
""" % (esc(b[0]), 0 if b[1] == "P" else 1, b[2], b[3], b[4], "%g" % b[5], b[6], b[7]))
    tail = """  SpawnMode: 0
  StartPlanetIndex: %d
  SpawnAngleDeg: %g
  SpawnPoint: {x: 0, y: 0}
  SpawnHeading: 0
""" % (START_INDEX, SPAWN_ANGLE)
    # ВНИМАНИЕ: путь захардкожен под этот Mac — на другой машине заменить на реальный путь проекта.
    base = "/Users/maxim/Desktop/SpaceTaxi/SpaceTaxiUnity/SpaceTaxi/Assets/ScriptableObjects/AlphaMap01.asset"
    # GUID существующего ассета сохраняем: ссылки (сцены и др.) не должны ломаться
    guid = uuid.uuid4().hex
    meta_path = base + ".meta"
    if os.path.exists(meta_path):
        with open(meta_path) as f:
            m = re.search(r"guid: ([a-f0-9]{32})", f.read())
            if m:
                guid = m.group(1)
    meta = """fileFormatVersion: 2
guid: %s
NativeFormatImporter:
  externalObjects: {}
  mainObjectFileID: 11400000
  userData:
  assetBundleName:
  assetBundleVariant:
""" % guid
    with open(base, "w") as f:
        f.write(head + "".join(entries) + tail)
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
