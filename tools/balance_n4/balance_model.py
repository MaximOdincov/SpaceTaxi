#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
balance_model.py — модель экономики SpaceTaxi для Н4 (Задача 7, шаги 1-3).

Все формулы — переводы кода игры в python (файл:строка в комментариях).
Входы читаются ИЗ АССЕТОВ репозитория при запуске (не захардкожены):
  Assets/ScriptableObjects/Resources/PricingConfig_Default.asset
  Assets/ScriptableObjects/Resources/UpgradeConfig_Default.asset
  Assets/ScriptableObjects/Resources/Ships/*.asset
  Assets/ScriptableObjects/PhysicsConfig_Default.asset
  Assets/ScriptableObjects/BetaMap01.asset
Константы, живущие в C#-коде, продублированы ниже с указанием файла и строки.

Оценки-входы методики (в ассетах их нет) — в словаре EST, помечаются «(оценка)».

Чистая модель без openpyxl: генератор xlsx и night4_checks.sh импортируют её.
"""
import math
import os
import random
import re
import struct

# ============================ ПУТИ ==========================================
ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
SO = os.path.join(ROOT, "Assets", "ScriptableObjects")
PRICING_ASSET = os.path.join(SO, "Resources", "PricingConfig_Default.asset")
UPGRADE_ASSET = os.path.join(SO, "Resources", "UpgradeConfig_Default.asset")
PHYSICS_ASSET = os.path.join(SO, "PhysicsConfig_Default.asset")
LEVEL_ASSET = os.path.join(SO, "BetaMap01.asset")
SHIPS_DIR = os.path.join(SO, "Resources", "Ships")

SECTOR_NAMES = ["Hub", "Near", "Mid1", "Mid2", "Far"]  # LevelAsset.Sector (enum 0..4)

# ============================ ЧТЕНИЕ YAML-АССЕТОВ ===========================
# Ассеты маленькие и плоские — достаточно построчного парсинга, без yaml-библиотеки.

def _read(path):
    with open(path, encoding="utf-8") as fh:
        return fh.read()

def _scalar(text, field, cast=float):
    m = re.search(r"^\s*%s: ([-\w.,]+)\s*$" % re.escape(field), text, re.M)
    if m is None:
        raise KeyError("поле %s не найдено" % field)
    raw = m.group(1).rstrip(",")
    return cast(raw)

def _float_list(text, field):
    """Массив из YAML-ассета: список '- N' либо компактная запись Unity 6
    примитивных массивов '2c010000...i' (hex little-endian, суффикс i=int32/f=float32)."""
    m = re.search(r"^\s*%s:\s*\n((?:\s*-\s*[-\d.]+\s*\n)+)" % re.escape(field), text, re.M)
    if m is not None:
        return [float(x) for x in re.findall(r"-\s*([-\d.]+)", m.group(1))]
    m = re.search(r"^\s*%s: ([0-9a-fA-F]+[if])\s*$" % re.escape(field), text, re.M)
    if m is None:
        raise KeyError("массив %s не найден" % field)
    return _bin_array(m.group(1))

def _bin_array(raw):
    """'2c010000bc020000dc050000i' → [300.0, 700.0, 1500.0] (int32/float32 LE по 4 байта)."""
    body, kind = raw[:-1], raw[-1]
    fmt = "i" if kind == "i" else "f"
    return [struct.unpack("<" + fmt, bytes.fromhex(body[j:j + 8]))[0]
            for j in range(0, len(body), 8)]

def load_pricing(path=PRICING_ASSET):
    """Assets/Scripts/Economy/PricingConfig.cs — имена полей 1:1."""
    t = _read(path)
    return dict(
        source=os.path.relpath(path, ROOT),
        base_price=_scalar(t, "BasePrice", int),                      # :12
        price_per_unit=_scalar(t, "PricePerDistanceUnit"),            # :13
        dist_cap=_scalar(t, "DistanceCap"),                           # :15
        tier_prestige=_float_list(t, "TierPrestigeMul"),              # :19
        sector_muls=[                                                 # :22-26
            _scalar(t, "SectorHubMul"), _scalar(t, "SectorNearMul"),
            _scalar(t, "SectorMid1Mul"), _scalar(t, "SectorMid2Mul"),
            _scalar(t, "SectorFarMul")],
        penalty_pct=_scalar(t, "PenaltyPerCollisionPct", int),        # :30
        penalty_cap=_scalar(t, "PenaltyCapPct", int),                 # :32
        rental_comm=_scalar(t, "RentalCommissionPct", int),           # :34
        debt_share=_scalar(t, "DebtRepaySharePct", int),              # :36
        rating_th=_scalar(t, "RatingPriceBonusThreshold"),            # :40
        rating_mul=_scalar(t, "RatingPriceBonusMul"),                 # :42
        low_rating_th=_scalar(t, "LowRatingSpawnThreshold"),          # :44
        low_rating_slow=_scalar(t, "LowRatingSpawnSlowdown"),         # :46
        meridian_tips=_scalar(t, "MeridianTipsBonusMul"),             # :50
        fuel_price_by_tier=_float_list(t, "FuelPricePerUnitByTier"),  # :54
        fuel_stock_by_tier=_float_list(t, "FuelStockFractionByTier"), # :56
        fuel_sector_mul=_float_list(t, "FuelPriceSectorMul"),         # :60
        fuel_outage_pct=_scalar(t, "FuelOutageChancePct", int),       # :62
        repair_pack_frac=_float_list(t, "RepairPackFraction"),        # :66
        repair_pack_mul=_float_list(t, "RepairPackPriceMul"),         # :68
        repair_tier_cap=_float_list(t, "RepairTierCapByTier"),        # :72
    )

def load_upgrades(path=UPGRADE_ASSET):
    """Assets/Scripts/Economy/UpgradeConfig.cs — имена полей 1:1."""
    t = _read(path)
    prices = _float_list(t, "PricesPerLevel")          # общие цены уровней 1..3 (фолбэк)
    return dict(
        source=os.path.relpath(path, ROOT),
        prices=prices,
        # по-линейные цены (D1, UpgradeConfig.GetPrice): поля нет/пуст — общий prices
        engine_prices=_line_prices(t, "EnginePrices", prices),
        tank_prices=_line_prices(t, "TankPrices", prices),
        hull_prices=_line_prices(t, "HullPrices", prices),
        maneuver_prices=_line_prices(t, "ManeuverPrices", prices),
        engine_thrust=_scalar(t, "EngineThrustPct"),      # +тяга %
        engine_boost=_scalar(t, "EngineBoostMax"),        # буст качелей до
        tank_cap=_scalar(t, "TankCapacityPct"),           # +бак %
        tank_drain_red=_scalar(t, "TankDrainRedPct"),     # −расход %
        hull_hp=_scalar(t, "HullHpPct"),                  # +HP %
        hull_heat_red=_scalar(t, "HullHeatRedPct"),       # −жар %
        maneuver_strafe=_scalar(t, "ManeuverStrafePct"),  # +Strafe %
    )

def _line_prices(text, field, fallback):
    """Цена линии из ассета (массив уровней 1..3); поля нет или пуст —
    общий PricesPerLevel, как фолбэк в UpgradeConfig.GetPrice."""
    try:
        vals = _float_list(text, field)
    except KeyError:
        return fallback
    return vals if vals else fallback

def load_physics(path=PHYSICS_ASSET):
    """Assets/Scripts/Physics/PhysicsConfig.cs — имена полей 1:1."""
    t = _read(path)
    return dict(
        source=os.path.relpath(path, ROOT),
        retro=_scalar(t, "Retro"),                  # глобальный тормозной декел (модельный ретро)
        min_speed=_scalar(t, "MinSpeed"),           # Н4.2.5: тормоз не опускает скорость ниже
        land_speed=_scalar(t, "LandSpeed"),         # порог жёсткой посадки (урон)
        takeoff_v=_scalar(t, "TakeoffV"),
        engine_mul=_scalar(t, "EngineMultiplier"),  # посадочное кольцо
        fuel_drain=_scalar(t, "FuelDrainPerSecond"),
        grav_k=_scalar(t, "GravityParamK"),
        grav_clamp=_scalar(t, "GravityAccelClamp"),
        grav_boost=_scalar(t, "GravityBoost"),
    )

def _yaml_name(raw):
    """DisplayName: строка с unicode-эскейпами → человекочитаемое имя."""
    body = raw.strip().strip('"')
    return body.encode("latin1", "backslashreplace").decode("unicode_escape") if "\\u" in body else body

def load_level(path=LEVEL_ASSET):
    """BetaMap01 — тела уровня: имя/тип/позиция/сектор/топливо/ремонт (порядок ассета важен:
    FuelNetwork.OutageStations держит тай-брейк по порядку списка)."""
    t = _read(path)
    bodies = []
    for chunk in t.split("- DisplayName:")[1:]:
        name_m = re.match(r'\s*"(.*)"', chunk)
        if name_m is None:
            continue
        mm = re.search(r"Position: \{x: ([-\d.]+), y: ([-\d.]+)\}", chunk)
        body = dict(
            name=_yaml_name(name_m.group(1)),
            type=int(re.search(r"^\s*Type: (\d+)", chunk, re.M).group(1)),
            x=float(mm.group(1)), y=float(mm.group(2)),
            has_fuel=_scalar(chunk, "HasFuel", int) == 1,
            fuel_tier=int(_scalar(chunk, "FuelTier")),
            has_repair=_scalar(chunk, "HasRepair", int) == 1,
            repair_tier=int(_scalar(chunk, "RepairTier")),
            is_hub=_scalar(chunk, "IsHub", int) == 1,
            sector=int(_scalar(chunk, "Sector")),
        )
        bodies.append(body)
    return dict(
        source=os.path.relpath(path, ROOT),
        name=_yaml_name(re.search(r"^\s*LevelName:\s*\"(.*)\"", t, re.M).group(1)),
        world_radius=_scalar(t, "WorldRadius"),
        start_index=int(_scalar(t, "StartPlanetIndex")),
        bodies=bodies,
    )

def load_ships(dir_path=SHIPS_DIR):
    """ShipConfig-ассеты: Id/DisplayName/Tier/статы/Price/IsRental/Retro."""
    ships = []
    for fname in sorted(os.listdir(dir_path)):
        if not fname.endswith(".asset"):
            continue
        t = _read(os.path.join(dir_path, fname))
        ships.append(dict(
            source="Assets/ScriptableObjects/Resources/Ships/" + fname,
            id=_scalar(t, "Id", str),
            name=_yaml_name(re.search(r'DisplayName: "(.*)"', t).group(1)),
            tier=_scalar(t, "Tier", int),
            thrust=_scalar(t, "Thrust"),
            strafe=_scalar(t, "StrafeAcc"),
            max_v=_scalar(t, "MaxV"),
            fuel_max=_scalar(t, "FuelMax"),
            hp_max=_scalar(t, "HpMax"),
            visual_length=_scalar(t, "VisualLength"),
            price=_scalar(t, "Price", int),
            is_rental=_scalar(t, "IsRental", int) == 1,
            retro=_scalar(t, "Retro"),
        ))
    return ships

# ============================ КОНСТАНТЫ КОДА =================================
# Каждая константа — с источником (файл:строка); при правке кода обновить здесь.

DEBT_ACCIDENT = 400          # Assets/Scripts/Economy/DebtService.cs:13
DISMISSAL_ACCIDENTS = 3      # Assets/Scripts/Economy/DebtService.cs:15
CRASH_K = 0.0032             # Assets/Scripts/Physics/LandingSystem.cs:168 (CrashDamagePerSpeedSq)
REPAIR_BASE_PRICE = 1.0      # Assets/Scripts/Economy/RepairNetwork.cs:46 (1 кр/HP)
CLOCK_SEC_PER_GAME_MIN = 3.0 # Assets/Scripts/UI/OnboardBinder.cs:597 (Time.time / 3)
CANCEL_PENALTY_FRACTION = 0.25  # Assets/Scripts/Orders/OrderService.cs:67
TIPS_MIN, TIPS_MAX = 0.10, 0.25   # Assets/Scripts/Orders/OrderService.cs:602 (Random.Range)
TASK_CHANCE = 0.5            # Assets/Scripts/Orders/PassengerTask.cs:60
FUEL_NEIGHBOR_COUNT = 2      # Assets/Scripts/Economy/FuelNetwork.cs:24
FUEL_OUTAGE_SALT = 0x9E3779B9     # Assets/Scripts/Economy/FuelNetwork.cs:27
FUEL_HASH_SEED = 2166136261       # Assets/Scripts/Economy/FuelNetwork.cs:142 (FNV offset)
FUEL_HASH_PRIME = 16777619        # Assets/Scripts/Economy/FuelNetwork.cs:146 (FNV prime)
OFFER_LIFETIME = 60.0        # OrderService.offerLifetime (инспектор сцены)
SPAWN_MIN, SPAWN_MAX = 15.0, 30.0  # OrderService.spawnMin/spawnMax
MAX_OFFERS = 4               # OrderService.maxOffers
RATING_START = 3.0           # Assets/Scripts/Economy/RatingService.cs:11
RATING_DELIVERY = 0.15       # RatingService.cs:14
RATING_COLLISION = 0.10      # RatingService.cs:15
RATING_TASK = 0.10           # RatingService.cs:16-17
RATING_FAIL_LOSS = 0.50      # RatingService.cs:19 (гибель заказа)

# Оценки-входы методики (в ассетах/коде чисел нет; в таблицах помечаются «(оценка)»)
EST = dict(
    cruise_frac=0.60,       # средняя крейсерская = 0.6 x MaxV (Н3, оценка)
    long_hop_dist=1500.0,   # хопы длиннее — межрайонные, летят с гравитационной раскачкой
    deadhead_frac=1.00,     # подлёт к пассажиру ~ длина платного маршрута (Н3, оценка)
    maneuver_time=25.0,     # посадка/взлёт/меню за заказ, с
    refuel_visit=90.0,      # визит дозаправки, с
    repair_visit=90.0,      # визит ремонта, с
    purchase_overhead=90.0, # перелёт в док + меню покупки, с
    death_time=120.0,       # пауза смерти + перелёт с хаба, с
    dmg_per_collision=40.0, # средний урон за столкновение, HP (оценка Н3)
    k_accel=2.0,            # «разгонов» за заказ (Н3, оценка: старт + после посадки)
    up_coll_red=0.30,       # снижение столкновений манёвровой линией на макс (оценка Н3)
    # аккуратность игры для симуляций: матожидание столкновений за заказ и риск смерти
    careful_lambda=0.30, careful_death=0.005,   # «аккуратная игра»
    avg_lambda=1.00, avg_death=0.020,           # «средний игрок»
    pick_best_of=3,         # из скольких карточек игрок выбирает (maxOffers=4)
    repair_threshold=0.60,  # чиниться, когда HP ниже доли максимума
    leg_reserve=1.05,       # запас топлива плана против расчётного горения (аккуратная игра)
    seed=14042026,          # фикс. seed симуляций Н4
    sim_orders=25,          # длина прогона стратегии
    sim_runs=5,             # прогонов на стратегию
)

# Ориентиры владельца (Н4, Задача 7 — приёмка)
TARGETS = dict(
    next_ship_min=8, next_ship_max=14,   # заказов до следующего корабля на каждом тире
    upgrade_payback_max=10,              # окупаемость улучшения, заказов (N фиксируем из факта, см. отчёт)
    rental_share_max=0.30,               # доля дохода, съедаемая арендой до первого корабля (X фиксируем из факта)
)

# Разведка Н4.6 (карта/плей) — входы-справка, код пересчитывает их же из ассета
RECON = dict(golden_path=16261, learning_ring=3139,
             longest_hops=[("Ворон", "М-14", 1393), ("Буй", "Полынь", 1342), ("Кузня", "Буй", 1235)],
             egg_burn_units=30, median_order_prestige1=605, mean_order_prestige1=628)

# ============================ ФОРМУЛЫ КОДА ===================================

def round_away(x):
    """C# Math.Round(x, MidpointRounding.AwayFromZero) для неотрицательных."""
    return int(math.floor(x + 0.5))

def order_price(pricing, dist, dropoff_sector, tier, rating_bonus=False):
    """OrderPricing.ComputePrice (OrderPricing.cs:11-21) + рейтинг-бонус спавна
    (OrderService.PriceWithRatingBonus:625-633)."""
    eff = min(dist, pricing["dist_cap"])
    raw = ((pricing["base_price"] + eff * pricing["price_per_unit"])
           * pricing["sector_muls"][dropoff_sector] * pricing["tier_prestige"][tier - 1])
    price = round_away(raw)
    if rating_bonus:
        price = round_away(price * pricing["rating_mul"])
    return price

def payout(pricing, price, rental, collisions, tips_frac, meridian=False, debt_balance=0):
    """PayoutService.Pay (PayoutService.cs:48-96): штраф → комиссия → чаевые → доля в долг → Net."""
    counted = min(max(collisions, 0), 3)                                # :56
    penalty = round_away(min(price * counted * pricing["penalty_pct"] / 100.0,
                             price * pricing["penalty_cap"] / 100.0))   # :57-59
    commission = round_away((price - penalty) * pricing["rental_comm"] / 100.0) if rental else 0  # :61-64
    tips = round_away(price * max(tips_frac, 0.0) * (pricing["meridian_tips"] if meridian else 1.0))  # :66-68
    gross = price - penalty - commission
    to_debt = min(round_away(gross * pricing["debt_share"] / 100.0), debt_balance) \
        if debt_balance > 0 and gross > 0 else 0                        # :72-78
    return dict(price=price, penalty=penalty, commission=commission,
                tips=tips, to_debt=to_debt, net=gross + tips - to_debt, collisions=collisions)

def fuel_price_per_unit(pricing, tier, sector):
    """FuelNetwork.PricePerUnit (FuelNetwork.cs:45-49): тир сети x район."""
    return pricing["fuel_price_by_tier"][tier - 1] * pricing["fuel_sector_mul"][sector]

def fuel_cost(pricing, units, tier, sector):
    """FuelNetwork.CostOf (FuelNetwork.cs:52-57): ceil(единицы x цена)."""
    return int(math.ceil(units * fuel_price_per_unit(pricing, tier, sector)))

def fuel_stock_level(pricing, fuel_max, tier):
    """FuelNetwork.StockLevel (FuelNetwork.cs:30-34): floor(бак x доля тира)."""
    return math.floor(fuel_max * pricing["fuel_stock_by_tier"][tier - 1])

def _fnv1a(name):
    """FuelNetwork.StableHash (FuelNetwork.cs:138-150) — FNV-1a по кодам символов строки."""
    h = FUEL_HASH_SEED
    for ch in name:
        h = ((h ^ ord(ch)) * FUEL_HASH_PRIME) & 0xFFFFFFFF
    return h

def outage_stations(level, pricing):
    """FuelNetwork.OutageStations (FuelNetwork.cs:64-89): детерминированные сухие сети.
    Хаб не пустеет; у пары сухих соседей сухим остаётся только ранний по списку."""
    fuel_bodies = [b for b in level["bodies"] if b["has_fuel"]]
    n = len(fuel_bodies)
    raw_dry = []
    for b in fuel_bodies:
        bucket = (_fnv1a(b["name"]) ^ FUEL_OUTAGE_SALT) % 100  # :134
        raw_dry.append(not b["is_hub"] and bucket < pricing["fuel_outage_pct"])
    dry = set()
    for i, b in enumerate(fuel_bodies):
        if not raw_dry[i]:
            continue
        if _has_earlier_dry_neighbor(i, n, fuel_bodies, raw_dry):
            continue
        dry.add(b["name"])
    return dry

def _is_neighbor(i, j, bodies):
    """FuelNetwork.IsNeighbor/AmongNearest (FuelNetwork.cs:108-127): j входит в 2 ближайших
    к i ИЛИ i — в 2 ближайших к j; равные дистанции — по порядку списка."""
    dij = math.hypot(bodies[i]["x"] - bodies[j]["x"], bodies[i]["y"] - bodies[j]["y"])
    for src, dst in ((i, j), (j, i)):
        closer = 0
        for k in range(len(bodies)):
            if k == src or k == dst:
                continue
            dk = math.hypot(bodies[src]["x"] - bodies[k]["x"], bodies[src]["y"] - bodies[k]["y"])
            if dk < dij:
                closer += 1
        if closer < FUEL_NEIGHBOR_COUNT:
            return True
    return False

def _has_earlier_dry_neighbor(index, count, bodies, raw_dry):
    for j in range(index):  # FuelNetwork.cs:93-101
        if raw_dry[j] and _is_neighbor(index, j, bodies):
            return True
    return False

def ship_stats(physics, upgrades, ship, levels=(0, 0, 0, 0)):
    """ShipStats.Compute (ShipStats.cs:28-60): эффективные статы с уровнями линий
    [Engine, Tank, Hull, Maneuver]."""
    frac = [lv / 3.0 for lv in levels]
    thrust = ship["thrust"] * (1 + upgrades["engine_thrust"] / 100.0 * frac[0])
    boost = physics["grav_boost"] + (upgrades["engine_boost"] - physics["grav_boost"]) * frac[0]
    fuel_max = ship["fuel_max"] * (1 + upgrades["tank_cap"] / 100.0 * frac[1])
    drain = physics["fuel_drain"] * (1 - upgrades["tank_drain_red"] / 100.0 * frac[1])
    hp_max = ship["hp_max"] * (1 + upgrades["hull_hp"] / 100.0 * frac[2])
    heat = 1 - upgrades["hull_heat_red"] / 100.0 * frac[2]
    strafe = ship["strafe"] * (1 + upgrades["maneuver_strafe"] / 100.0 * frac[3])
    retro = ship["retro"] if ship["retro"] > 0 else physics["retro"]
    return dict(thrust=thrust, strafe=strafe, fuel_max=fuel_max, drain=drain, hp_max=hp_max,
                heat=heat, retro=retro, boost=boost, max_v=ship["max_v"])

def brake_distance(stats, physics, target_speed=None):
    """Тормозной путь от MaxV до скорости посадки: (MaxV²−v²)/(2xRetro) (метрика Н3);
    тормоз не опускает ниже MinSpeed (ShipController.cs:213-215, Н4.2.5) — полный
    останов путь считается до MinSpeed."""
    v = physics["land_speed"] if target_speed is None else target_speed
    return (stats_max_v(stats) ** 2 - v ** 2) / (2.0 * stats["retro"])

def stats_max_v(stats_or_ship):
    """MaxV улучшениями не меняется (ShipStats.cs:22)."""
    return stats_or_ship["max_v"]

def crash_damage(speed, land_speed):
    """LandingSystem (LandingSystem.cs:173): k x (v−LandSpeed)²."""
    delta = speed - land_speed
    return CRASH_K * delta * delta if delta > 0 else 0.0

def repair_cost(pricing, missing_hp, station_tier, pack_index=1):
    """RepairNetwork (RepairNetwork.cs:27-51): средний пакет, зажатый потолком сети;
    цена ceil(HP x множитель пакета)."""
    pack = pricing["repair_pack_frac"][pack_index]
    cap = pricing["repair_tier_cap"][station_tier - 1] if station_tier >= 1 else 1.0
    hp = min(missing_hp * min(pack, cap), missing_hp)
    return int(math.ceil(hp * pricing["repair_pack_mul"][pack_index])), hp

# ============================ МИР: ПРОИЗВОДНЫЕ ===============================

class World:
    """Планеты уровня + топливные станции (сухие — FuelNetwork.OutageStations)."""

    def __init__(self, level, pricing):
        self.level = level
        self.pricing = pricing
        self.planets = [b for b in level["bodies"] if b["type"] == 0]
        self.dry = outage_stations(level, pricing)
        self.stations = [b for b in self.planets if b["has_fuel"] and b["name"] not in self.dry]

    def dist(self, a, b):
        return math.hypot(a["x"] - b["x"], a["y"] - b["y"])

    def nearest_station(self, planet, exclude=None):
        """Ближайшая живая заправка (плей: план по карте, цены видны на M)."""
        best, best_d = None, None
        for s in self.stations:
            if exclude is not None and s["name"] == exclude:
                continue
            d = self.dist(planet, s)
            if best is None or d < best_d:
                best, best_d = s, d
        return best, (best_d if best is not None else 0.0)

    def refuel_plan(self, planet):
        """Где и почём заправляться, стоя на planet: сама станция или ближайшая."""
        if self.is_wet(planet):
            return planet, 0.0
        return self.nearest_station(planet)

    def is_wet(self, planet):
        """Станция с топливом сейчас: HasFuel И не в outage (Луг сухая при HasFuel=1)."""
        return planet["has_fuel"] and planet["name"] not in self.dry

    def hop_cruise(self, dist, stats):
        """Крейсерская скорость хопа. Длинные (межрайонные, > 1500 юн = DistanceCap)
        пролёты идут с гравитационной раскачкой: средняя скорость умножается на
        GravityBoost (PhysicsConfig.GravityBoost = 1.15; перк двигателя до 1.3).
        Короткие хопы — оценка Н3: 0.6 x MaxV."""
        cruise = stats["max_v"] * EST["cruise_frac"]
        if dist > EST["long_hop_dist"]:
            cruise *= stats["boost"]
        return cruise

    def burn(self, a, b, stats):
        """Топливо на перелёт a→b: время тяги x FuelDrainPerSecond (ShipController.cs:163)."""
        return self.dist(a, b) / self.hop_cruise(self.dist(a, b), stats) * stats["drain"]

    def plan_leg(self, start, fuel, end, stats, pricing, reserve=None, max_stops=6):
        """План перелёта start→end при топливе fuel: прямой перелёт либо цепочка
        остановок на живых заправках (плей: маршрут по карте с дозаправками по пути).
        Жадный выбор следующей точки — минимум расстояния до цели среди достижимых.
        Возвращает (hops, feasible): hops = [(fly_to, burn, stop_or_None)]."""
        if reserve is None:
            reserve = EST["leg_reserve"]
        drain = stats["drain"]
        burn_of = lambda a, b: self.burn(a, b, stats)

        def refuel(fuel_now, station):
            stock = fuel_stock_level(pricing, stats["fuel_max"], station["fuel_tier"])
            units = max(0, stock - int(math.ceil(fuel_now)))   # AvailableUnits:38-42
            return min(stats["fuel_max"], fuel_now + units)

        hops = []
        pos, f = start, fuel
        visited = set()
        for _ in range(max_stops + 1):
            direct = burn_of(pos, end)
            if f >= direct * reserve:  # цель достижима напрямую (или мы уже на ней)
                if direct > 0:
                    hops.append((end, direct, None))
                return hops, True
            # следующая остановка: достижимая станция, ближайшая к цели
            candidates = []
            for s in self.stations:
                if s["name"] in visited or self.dist(pos, s) <= 0:
                    continue
                burn1 = burn_of(pos, s)
                if f < burn1 * reserve:
                    continue
                candidates.append((self.dist(s, end), s, burn1))
            if not candidates:
                return None, False  # застряли: ни цель, ни станция не достижимы
            _, s, burn1 = min(candidates, key=lambda c: c[0])
            hops.append((s, burn1, s))
            visited.add(s["name"])
            f = refuel(f - burn1, s)
            pos = s
        return None, False

    def leg_fuel_cost(self, hops, start_fuel, stats, pricing):
        """Стоимость топлива плана: ceil-покупки на остановках (FuelNetwork.CostOf)."""
        cost = 0
        fuel = start_fuel
        for _, burn, stop in hops:
            fuel -= burn
            if stop is not None:
                stock = fuel_stock_level(pricing, stats["fuel_max"], stop["fuel_tier"])
                units = max(0, stock - int(math.ceil(fuel)))
                cost += fuel_cost(pricing, units, stop["fuel_tier"], stop["sector"])
                fuel = min(stats["fuel_max"], fuel + units)
        return cost

    def walk_fuel(self, hops, start_fuel, stats, pricing):
        """Топливо на конце плана: последовательное списание и покупки-ceil,
        ровно как исполняет fly_leg (иначе оценка расходится с исполнением)."""
        fuel = start_fuel
        for _, burn, stop in hops:
            fuel -= burn
            if stop is not None:
                stock = fuel_stock_level(pricing, stats["fuel_max"], stop["fuel_tier"])
                fuel = min(stats["fuel_max"], fuel + max(0, stock - int(math.ceil(fuel))))
        return fuel

def sample_collisions(rng, lam):
    """Пуассон(λ) — число столкновений за заказ; PayoutService клампит в 0..3."""
    l = math.exp(-lam)
    k, p = 0, 1.0
    while True:
        p *= rng.random()
        if p <= l or k > 30:
            return k
        k += 1

# ============================ СИМУЛЯЦИЯ ======================================

DEBUG_SIM = False  # трассировка решений симуляции (для отладки модели)

MAIN_PATH = ["egg_own", "strela", "igla", "barin"]  # прогрессия по тирам (цена/тир из ассетов)

class SimState(object):
    def __init__(self):
        self.wallet = 0
        self.debt = 0
        self.unpaid_accidents = 0
        self.rating = RATING_START
        self.owned = []           # id купленных
        self.current = "egg_rental"
        self.levels = {}          # id → [E,T,H,M]
        self.fuel = None          # заполняется при старте
        self.hp_frac = 1.0
        self.position = None      # планета стоянки
        self.order_no = 0

def simulate(world, physics, pricing, upgrades, ships_by_id, strategy, seed,
             n_orders=EST["sim_orders"], careful=True, start_ship="egg_rental",
             keep_ship=False):
    """Прогон стратегии длиной n_orders заказов.

    Формулы честные: цена карточки (OrderPricing), выплата (PayoutService.Pay),
    топливо по хопам (FuelNetwork), ремонт (RepairNetwork), долг (DebtService),
    смерть (GameSession+FleetService: аренда → +400 долга, своя → потеря корпуса).
    Аккуратность — параметрами EST (λ столкновений, риск смерти, выбор карточки).
    start_ship — корабль в начале прогона (для метрик «жизнь на одном корабле»).
    keep_ship=True — после смерти своего корпуса респавн на ТОМ ЖЕ (метрики корабля
    изолированно: потеря корпуса идёт в rebuy_spent, пересадки на аренду нет).
    Возвращает сводку прогона."""
    rng = random.Random(seed)
    st = SimState()
    hub = world.planets[world.level["start_index"]]
    st.position = hub
    st.current = start_ship
    if start_ship != "egg_rental":
        st.owned = [start_ship]  # считаем корабль своим (аренда всегда доступна и так)
    totals = dict(income=0, price=0, tips=0, penalty=0, commission=0, fuel=0, repair=0,
                  to_debt=0, orders=0, failed=0, deaths=0, waited=0,
                  time=0.0, refuels=0, repairs_done=0, ship_spent=0, upgrade_spent=0,
                  rebuy_spent=0, dismissals=0, collisions=0)
    ship_cost_total = {}       # id → потрачено на корпус+улучшения (для стоимости смерти)
    wallet_track = []          # (order_no, wallet, ship_id)
    purchases = []             # (order_no, id, price)
    upgrade_buys = []          # (order_no, line, level, price)

    def cur_ship():
        return ships_by_id[st.current]

    def cur_levels():
        return tuple(st.levels.get(st.current, (0, 0, 0, 0)))

    def cur_stats():
        return ship_stats(physics, upgrades, cur_ship(), cur_levels())

    def spend_from_wallet_or_debt(amount):
        """Как GameSession.ChargeCancelPenalty: кошелёк → недостача в долг."""
        from_wallet = min(amount, st.wallet)
        st.wallet -= from_wallet
        rest = amount - from_wallet
        if rest > 0:
            st.debt += rest
        return from_wallet, rest

    def try_buy_ship(sid):
        ship = ships_by_id[sid]
        if st.wallet < ship["price"] or sid in st.owned:
            return False
        st.wallet -= ship["price"]                       # FleetService.TryBuy:84-94
        st.owned.append(sid)
        st.current = sid
        st.levels[sid] = [0, 0, 0, 0]
        st.fuel = stats_of(sid, (0, 0, 0, 0))["fuel_max"]
        st.hp_frac = 1.0
        totals["ship_spent"] += ship["price"]
        ship_cost_total[sid] = ship_cost_total.get(sid, 0) + ship["price"]
        purchases.append((st.order_no, sid, ship["price"]))
        totals["time"] += EST["purchase_overhead"]
        return True

    def try_upgrade(line_idx):
        """FleetService.TryUpgrade:114-134 — только свой корабль, цена уровня из конфига."""
        sid = st.current
        if sid not in st.owned or sid == "egg_rental":
            return False
        levels = st.levels.setdefault(sid, [0, 0, 0, 0])
        if levels[line_idx] >= 3:
            return False
        line_key = ("engine", "tank", "hull", "maneuver")[line_idx]  # UpgradeLine: Engine=0..Maneuver=3
        price = upgrades[line_key + "_prices"][levels[line_idx]]     # по-линейная цена (UpgradeConfig.GetPrice)
        if st.wallet < price:
            return False
        st.wallet -= price
        levels[line_idx] += 1
        totals["upgrade_spent"] += price
        ship_cost_total[sid] = ship_cost_total.get(sid, 0) + price
        upgrade_buys.append((st.order_no, line_key, levels[line_idx], price))
        return True

    def stats_of(sid, levels):
        return ship_stats(physics, upgrades, ships_by_id[sid], levels)

    def do_refuel():
        """Стоя у заправки: докупить до запаса сети (FuelNetwork.StockLevel/AvailableUnits)."""
        planet = st.position
        if not (planet["has_fuel"] and planet["name"] not in world.dry):
            return 0
        stats = cur_stats()
        stock = fuel_stock_level(pricing, stats["fuel_max"], planet["fuel_tier"])
        units = max(0, stock - int(math.ceil(st.fuel)))    # AvailableUnits:38-42
        if units <= 0:
            return 0
        cost = fuel_cost(pricing, units, planet["fuel_tier"], planet["sector"])
        _, to_debt = spend_from_wallet_or_debt(cost)
        totals["fuel"] += cost
        totals["refuels"] += 1
        st.debt += to_debt
        st.fuel = min(stats["fuel_max"], st.fuel + units)
        totals["time"] += EST["refuel_visit"]
        return cost

    def do_repair():
        planet = st.position
        if not planet["has_repair"] or st.hp_frac >= EST["repair_threshold"]:
            return
        stats = cur_stats()
        missing = stats["hp_max"] * (1 - st.hp_frac)
        cost, _ = repair_cost(pricing, missing, planet["repair_tier"], pack_index=2)  # полный пакет
        _, to_debt = spend_from_wallet_or_debt(cost)
        totals["repair"] += cost
        totals["repairs_done"] += 1
        totals["time"] += EST["repair_visit"]
        st.debt += to_debt
        st.hp_frac = 1.0

    def fly_leg(plan_hops, stats):
        """Исполнение плана перелёта: списание топлива, остановки-заправки.
        Возвращает False, если топлива не хватило (нештатная посадка)."""
        for fly_to, burn, stop in plan_hops:
            if st.fuel < burn:
                return False
            st.fuel -= burn
            totals["time"] += burn / stats["drain"]  # сек. тяги = расстояние/крейсер
            st.position = fly_to
            if stop is not None:
                do_refuel()
        return True

    def pick_order():
        """Аккуратная игра: из pick_best_of карточек выбирает самую выгодную проходимую.
        Проходимость — план перелёта World.plan_leg: leg1 (стоянка→пассажир) и
        leg2 (пассажир→доставка) исполнимы с дозаправками по пути; после доставки
        остаётся резерв до ближайшей живой станции. Возвращает план заказа или None."""
        best = None
        stats = cur_stats()
        rental = cur_ship()["is_rental"]
        for _ in range(EST["pick_best_of"] if careful else 1):
            pickup = rng.choice(world.planets)
            dropoff = rng.choice([p for p in world.planets if p is not pickup])
            dist = world.dist(pickup, dropoff)
            bonus = st.rating >= pricing["rating_th"]
            price = order_price(pricing, dist, dropoff["sector"], cur_ship()["tier"], bonus)
            tips = rng.uniform(TIPS_MIN, TIPS_MAX)
            hops1, ok1 = world.plan_leg(st.position, st.fuel, pickup, stats, pricing)
            if not ok1:
                continue
            fuel_after1 = world.walk_fuel(hops1, st.fuel, stats, pricing)
            if world.is_wet(pickup):  # заправка у пассажира перед маршрутом
                stock = fuel_stock_level(pricing, stats["fuel_max"], pickup["fuel_tier"])
                fuel_after1 = min(stats["fuel_max"], fuel_after1 + max(0, stock - int(math.ceil(fuel_after1))))
            hops2, ok2 = world.plan_leg(pickup, fuel_after1, dropoff, stats, pricing)
            if not ok2:
                continue
            # резерв аккуратной игры: после доставки хватит топлива до ближайшей
            # живой станции (пустой бак на сухой планете — мягкий софтлок: взлёт
            # бесплатный, но тяги нет — LandingSystem.Takeoff не тратит топливо,
            # ShipController при Fuel<=0 глушит двигатели)
            fuel_at_dropoff = world.walk_fuel(hops2, fuel_after1, stats, pricing)
            reserve_target = 0.0 if world.is_wet(dropoff) else \
                world.burn(dropoff, world.refuel_plan(dropoff)[0], stats)
            if fuel_at_dropoff < reserve_target * 1.05:
                if DEBUG_SIM:
                    print(f"    decline {pickup['name']}->{dropoff['name']}: "
                          f"arrive {fuel_at_dropoff:.1f} < reserve {reserve_target*1.05:.1f}")
                continue
            est_cost = world.leg_fuel_cost(hops1, st.fuel, stats, pricing) + \
                world.leg_fuel_cost(hops2, fuel_after1, stats, pricing)
            expected = price * (1 + 0.175) * (1 - 0.35 if rental else 1.0) - est_cost
            cand = dict(pickup=pickup, dropoff=dropoff, price=price, tips=tips,
                        dist=dist, hops1=hops1, hops2=hops2, expected=expected)
            if best is None or cand["expected"] > best["expected"]:
                best = cand
        return best

    def die():
        """HullSystem.RespawnAfter → GameSession.OnRespawned → FleetService.NotifyShipLost.
        Аренда: долг +400 (DebtService.RegisterAccident). Свой: корпус списан с
        улучшениями, пересадка на аренду. Респавн на хабе с полными HP/баком."""
        totals["deaths"] += 1
        totals["failed"] += 1
        st.rating = max(0.0, st.rating - RATING_FAIL_LOSS)
        if not cur_ship()["is_rental"]:
            lost = ship_cost_total.get(st.current, cur_ship()["price"])
            totals["rebuy_spent"] += lost  # потеря вложений (чтобы вернуться, надо перекупить)
            if keep_ship:
                # метрический прогон: корпус «перекупается» мгновенно (та же модель),
                # потеря остаётся в rebuy_spent — не смешивать с арендой
                ship_cost_total[st.current] = cur_ship()["price"]
                st.levels[st.current] = [0, 0, 0, 0]
                _, rest = spend_from_wallet_or_debt(cur_ship()["price"])
                st.debt += rest
                st.owned = [s for s in st.owned if s != st.current] + [st.current]
            else:
                ship_cost_total.pop(st.current, None)
                st.levels.pop(st.current, None)
                st.owned.remove(st.current)
                st.current = "egg_rental"
        else:
            st.debt += DEBT_ACCIDENT
            st.unpaid_accidents += 1
            if st.unpaid_accidents >= DISMISSAL_ACCIDENTS:
                totals["dismissals"] += 1
                st.unpaid_accidents = 0
        totals["time"] += EST["death_time"]
        st.fuel = cur_stats()["fuel_max"]
        st.hp_frac = 1.0
        st.position = hub

    # ---- старт прогона
    st.fuel = cur_stats()["fuel_max"]
    if strategy == "rental_hold":
        pass  # сидим на аренде, ничего не покупаем
    elif strategy == "buy_ships":
        pass  # покупаем по мере накопления в цикле ниже
    elif strategy == "death_spam":
        pass
    elif strategy == "greedy_upgrades":
        pass

    spam_every = 3  # «смерть-спам»: намеренная авария раз в 3 заказа

    while totals["orders"] < n_orders and totals["waited"] < n_orders * 3:
        ship = cur_ship()

        # стратегия: покупки на стоянке
        if strategy == "buy_ships":
            for sid in MAIN_PATH:
                if sid not in st.owned and st.wallet >= ships_by_id[sid]["price"]:
                    try_buy_ship(sid)
                    break
        elif strategy == "greedy_upgrades":
            if "egg_own" not in st.owned:
                try_buy_ship("egg_own")
            else:
                line_order = [1, 0, 3, 2]  # бак → двигатель → манёвр → корпус
                for li in line_order:
                    if st.levels.get("egg_own", [0, 0, 0, 0])[li] < 3:
                        try_upgrade(li)
                        break

        do_refuel()
        do_repair()

        order = pick_order()
        if order is None:
            # проходимых карточек нет (топливо/выбор) — ждём спавна
            totals["waited"] += 1
            delay = (SPAWN_MIN + SPAWN_MAX) / 2.0
            if st.rating < pricing["low_rating_th"]:
                delay *= pricing["low_rating_slow"]
            totals["time"] += delay
            continue

        st.order_no += 1
        stats = cur_stats()
        totals["time"] += EST["maneuver_time"]  # посадки/меню на взятие и сдачу
        if DEBUG_SIM:
            print(f"  ORD{st.order_no} {order['pickup']['name']}->{order['dropoff']['name']} "
                  f"fuel={st.fuel:.1f} plan_arrive={world.walk_fuel(order['hops2'], world.walk_fuel(order['hops1'], st.fuel, stats, pricing), stats, pricing):.1f}")
        if not fly_leg(order["hops1"], stats):
            die()
            wallet_track.append((st.order_no, st.wallet, st.current))
            continue
        if world.is_wet(st.position):
            do_refuel()  # заправка у пассажира перед платным маршрутом (как в плане)
        if not fly_leg(order["hops2"], stats):
            die()  # не дотянул — нештатная посадка (заказ провален)
            wallet_track.append((st.order_no, st.wallet, st.current))
            continue
        st.position = order["dropoff"]

        death_p = EST["careful_death"] if careful else EST["avg_death"]
        spam_death = strategy == "death_spam" and st.order_no % spam_every == 0
        if spam_death or rng.random() < death_p:
            die()
            wallet_track.append((st.order_no, st.wallet, st.current))
            continue

        collisions = sample_collisions(rng, EST["careful_lambda"] if careful else EST["avg_lambda"])
        res = payout(pricing, order["price"], ship["is_rental"], collisions,
                     order["tips"], ship["id"] == "meridian", st.debt)
        st.wallet += res["net"]
        st.debt -= res["to_debt"]
        totals["income"] += res["net"]
        totals["price"] += res["price"]
        totals["tips"] += res["tips"]
        totals["penalty"] += res["penalty"]
        totals["commission"] += res["commission"]
        totals["to_debt"] += res["to_debt"]
        totals["collisions"] += collisions
        totals["orders"] += 1
        st.rating = min(5.0, st.rating + RATING_DELIVERY)
        if rng.random() < TASK_CHANCE:  # задание пассажира: аккуратная игра обычно выполняет
            st.rating = min(5.0, st.rating + RATING_TASK)
        st.hp_frac = max(0.0, st.hp_frac - collisions * EST["dmg_per_collision"] / stats["hp_max"])
        wallet_track.append((st.order_no, st.wallet, st.current))

    return dict(totals=totals, wallet_track=wallet_track, purchases=purchases,
                upgrades=upgrade_buys, final_wallet=st.wallet, final_debt=st.debt,
                final_ship=st.current, orders_done=totals["orders"],
                final_position=st.position["name"], final_fuel=st.fuel,
                sim_time_min=totals["time"] / 60.0,
                game_hours=totals["time"] / CLOCK_SEC_PER_GAME_MIN / 60.0)
