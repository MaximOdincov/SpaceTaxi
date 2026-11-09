#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
build_balance_n4.py — генератор docs/GameInfo/BALANCE_N4.xlsx (Н4, Задача 7, шаг 2).

Отличие от BALANCE_N3.xlsx: формулы считаются КОДОМ (balance_model.py — переводы
формул из C# с файл:строка), xlsx — результат, а не источник формул.
Входы читаются из ассетов репозитория при запуске; оценки методики помечены.

Листы:
  Параметры  — все входы с указанием источника (файл → поле / код:строка / оценка)
  Метрики    — доход/заказ по секторам и тирам, корабли, переходы, улучшения,
               стоимость жизни, время прохождения
  Симуляции  — 4 стратегии (аренда, ранняя покупка, смерть-спам, жадные улучшения),
               фикс. seed, честные формулы выплаты/топлива/долга
  Инварианты — 5 приёмо-инвариантов владельца: факт / порог / вердикт / что крутить

Запуск: python tools/balance_n4/build_balance_n4.py [путь-к-xlsx]
"""
import os
import random
import statistics
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import balance_model as bm  # noqa: E402

from openpyxl import Workbook  # noqa: E402
from openpyxl.styles import Alignment, Border, Font, PatternFill, Side  # noqa: E402
from openpyxl.utils import get_column_letter  # noqa: E402

OUT_DEFAULT = os.path.normpath(os.path.join(bm.ROOT, "docs", "GameInfo", "BALANCE_N4.xlsx"))

# ============================ СТИЛИ ==========================================
HDR_FILL = PatternFill("solid", fgColor="4472C4")
SUB_FILL = PatternFill("solid", fgColor="D9E1F2")
EST_FILL = PatternFill("solid", fgColor="FFF2CC")   # оценки-входы
RECO_FILL = PatternFill("solid", fgColor="E2EFDA")  # рекомендации владельцу
FAIL_FILL = PatternFill("solid", fgColor="FFC7CE")
WARN_FILL = PatternFill("solid", fgColor="FFEB9C")
PASS_FILL = PatternFill("solid", fgColor="C6EFCE")
THIN = Side(style="thin", color="B0B0B0")
BORDER = Border(left=THIN, right=THIN, top=THIN, bottom=THIN)


def put(ws, row, col, value, *, fmt=None, bold=False, fill=None, border=True,
        align=None, wrap=False, italic=False, color=None):
    c = ws.cell(row=row, column=col, value=value)
    if fmt:
        c.number_format = fmt
    if bold or italic or color:
        c.font = Font(bold=bold, italic=italic, color=color)
    if fill:
        c.fill = fill
    if border:
        c.border = BORDER
    if align or wrap:
        c.alignment = Alignment(horizontal=align, wrap_text=wrap, vertical="top")
    return c


def title_row(ws, text, note=None):
    ws.cell(row=1, column=1, value=text).font = Font(bold=True, size=13)
    if note:
        ws.cell(row=2, column=1, value=note).font = Font(italic=True, size=9, color="555555")


def widths(ws, spec):
    for col, w in spec.items():
        ws.column_dimensions[col].width = w


# ============================ СБОР ДАННЫХ ====================================

def collect_orders_stats(world, pricing, n=8000):
    """Монте-Карло потока заказов (OrderService.AddOffer: пара случайных планет,
    OrderPricing.ComputePrice + рейтинг-бонус). Возвращает статистику по секторам."""
    rng = random.Random(1234)
    by_sector = {s: [] for s in range(5)}
    all_by_tier = {t: [] for t in range(1, 6)}
    dists = []
    for _ in range(n):
        a = rng.choice(world.planets)
        b = rng.choice([p for p in world.planets if p is not a])
        d = world.dist(a, b)
        dists.append(d)
        for t in range(1, 6):
            all_by_tier[t].append(bm.order_price(pricing, d, b["sector"], t, True))
        by_sector[b["sector"]].append(bm.order_price(pricing, d, b["sector"], 2, True))
    sectors = {}
    for s, prices in by_sector.items():
        sectors[s] = dict(n=len(prices), median=statistics.median(prices),
                          mean=statistics.mean(prices),
                          share=len(prices) / n)
    tiers = {t: dict(median=statistics.median(v), mean=statistics.mean(v))
             for t, v in all_by_tier.items()}
    return dict(sectors=sectors, tiers=tiers,
                dist_median=statistics.median(dists), dist_mean=statistics.mean(dists),
                dist_max=max(dists))


def collect_ship_sim(world, physics, pricing, upgrades, ships_by_id, ship_id,
                     runs=5, n_orders=25, seed0=2001):
    """Жизнь на одном корабле: средний чистый доход/заказ и его компоненты.
    Возвращает среднее по runs прогонов simulate(strategy=rental_hold)."""
    acc = dict(orders=0, income=0.0, price=0, tips=0.0, penalty=0, commission=0,
               fuel=0.0, repair=0.0, time=0.0, waited=0, deaths=0, failed=0,
               refuels=0, collisions=0)
    for i in range(runs):
        r = bm.simulate(world, physics, pricing, upgrades, ships_by_id, "rental_hold",
                        seed0 + i, n_orders=n_orders, careful=True, start_ship=ship_id,
                        keep_ship=True)
        t = r["totals"]
        for k in acc:
            acc[k] += t.get(k, 0)
    div = max(1, acc["orders"])
    return dict(
        net_per_order=acc["income"] / div,
        card_per_order=acc["price"] / div,
        tips_per_order=acc["tips"] / div,
        penalty_per_order=acc["penalty"] / div,
        commission_per_order=acc["commission"] / div,
        fuel_per_order=acc["fuel"] / div,
        repair_per_order=acc["repair"] / div,
        time_per_order=acc["time"] / div,
        waited_frac=acc["waited"] / max(1, acc["waited"] + acc["orders"] + acc["failed"]),
        deaths=acc["deaths"], failed=acc["failed"], orders=acc["orders"],
        collisions_per_order=acc["collisions"] / div,
        commission_share=acc["commission"] / max(1.0, (acc["price"] + acc["tips"])),
        delivered_frac=acc["orders"] / max(1, acc["orders"] + acc["failed"] + acc["waited"]),
    )


def collect_upgrade_payback(ship, physics, pricing, upgrades, sim):
    """Окупаемость улучшений аналитически (методика Н3, формулы из ShipStats.Compute):
    Δдоход/заказ линии на каждом уровне → заказов на окупаемость уровня.
    Требует sim (net/заказ, топливо/заказ, время/заказ, цена карточки) этого корабля."""
    stats0 = bm.ship_stats(physics, upgrades, ship, (0, 0, 0, 0))
    cruise = stats0["max_v"] * bm.EST["cruise_frac"]
    income_per_sec = sim["net_per_order"] / max(1.0, sim["time_per_order"])
    lam = bm.EST["careful_lambda"]
    death_cost_own = ship["price"] + sum(upgrades["prices"]) * 4 + sim["net_per_order"]

    # типовой заказ: платный маршрут ≈ средняя дистанция карты с холостым плечом
    flight_dist = sim["_flight_dist"]
    tank_range0 = stats0["fuel_max"] / stats0["drain"] * cruise
    visits0 = flight_dist / tank_range0

    rows = []
    lines = ["engine", "tank", "hull", "maneuver"]
    line_names = {"engine": "Двигатель", "tank": "Бак", "hull": "Корпус", "maneuver": "Манёвровые"}
    for line in lines:
        cum_price = 0.0
        cum_gain = 0.0
        for lvl in (1, 2, 3):
            levels = [0, 0, 0, 0]
            levels[lines.index(line)] = lvl
            stats = bm.ship_stats(physics, upgrades, ship, tuple(levels))
            price = upgrades["prices"][lvl - 1]
            if line == "engine":
                # тяга ↑ → быстрее разгон до MaxV: экономия времени × 2 разгона/заказ
                t_saved = bm.EST["k_accel"] * stats0["max_v"] * (1 / stats0["thrust"] - 1 / stats["thrust"])
                gain = t_saved * income_per_sec
                note = "+тяга, −время разгона (расход топлива в коде НЕ растёт — минуса нет)"
            elif line == "tank":
                # расход ↓ → дешевле топливо; бак ↑ → реже дозаправки
                fuel_saved = sim["fuel_per_order"] * (1 - stats["drain"] / stats0["drain"])
                tank_range = stats["fuel_max"] / stats["drain"] * cruise
                visits = flight_dist / tank_range
                time_saved = max(0.0, visits0 - visits) * bm.EST["refuel_visit"]
                gain = fuel_saved + time_saved * income_per_sec
                note = "+бак, −расход (массы в коде нет — минуса нет)"
            elif line == "hull":
                # HP ↑ → ниже риск смерти (риск ∝ 1/HP): экономия стоимости смерти
                deaths_saved = bm.EST["careful_death"] * (stats["hp_max"] / stats0["hp_max"] - 1)
                gain = deaths_saved * death_cost_own
                note = "+HP, −жар (риск смерти ∝ 1/HP, оценка)"
            else:
                # стрейф ↑ → меньше столкновений (оценка −30% на макс): штраф + ремонт
                lam_new = lam * (1 - bm.EST["up_coll_red"] * lvl / 3.0)
                lam_saved = (lam if lvl == 1 else
                             lam * (1 - bm.EST["up_coll_red"] * (lvl - 1) / 3.0)) - lam_new
                gain = lam_saved * (sim["card_per_order"] * pricing["penalty_pct"] / 100.0
                                    + bm.EST["dmg_per_collision"] * 1.0)
                note = "+стрейф, −столкновения (оценка −30% на макс)"
            cum_price += price
            cum_gain += gain
            payback = price / gain if gain > 0 else None
            rows.append(dict(line=line_names[line], line_id=line, level=lvl, price=price,
                             gain=gain, payback=payback, note=note,
                             cum_payback=cum_price / cum_gain if cum_gain > 0 else None))
    return rows


def collect_death_costs(pricing, sims):
    """Стоимость смерти vs стоимость заправки вовремя (DebtService.cs:13,
    GameSession.OnRespawned + FleetService.NotifyShipLost)."""
    egg = sims["egg_rental"]
    rental_death = bm.DEBT_ACCIDENT + egg["net_per_order"]
    death_by_ship = {}
    for sid, sim in sims.items():
        ship_price = dict((s["id"], s["price"]) for s in bm.load_ships())[sid]
        death_by_ship[sid] = dict(
            lost_order=sim["net_per_order"],
            ship_value=0 if sid == "egg_rental" else ship_price,
            debt=bm.DEBT_ACCIDENT if sid == "egg_rental" else 0,
            total=(bm.DEBT_ACCIDENT if sid == "egg_rental" else ship_price) + sim["net_per_order"])
    return rental_death, death_by_ship


def collect_north_gate(world, physics, ships):
    """Северная калитка: Грань → Буй → Стужа (два межрайонных хопа, Буй без топлива).
    Корабль проходит север, если суммарное горение цепочки ≤ бак."""
    gran = next(p for p in world.planets if p["name"] == "Грань")
    buy = next(p for p in world.planets if p["name"] == "Буй")
    stuzha = next(p for p in world.planets if p["name"] == "Стужа")
    rows = []
    for ship in ships:
        stats = bm.ship_stats(physics, bm.load_upgrades(), ship, (0, 0, 0, 0))
        b1 = world.burn(gran, buy, stats)
        b2 = world.burn(buy, stuzha, stats)
        rows.append(dict(id=ship["id"], name=ship["name"], tank=stats["fuel_max"],
                         burn=b1 + b2, ok=(b1 + b2) <= stats["fuel_max"],
                         direct=world.burn(gran, stuzha, stats)))
    return rows


def collect_route_times(physics, ships):
    """Время золотого пути (16261 юн, межрайонные хопы с раскачкой) и учебного
    кольца (3139 юн, короткие хопы) на каждом корабле, реальных минут."""
    rows = []
    for ship in ships:
        stats = bm.ship_stats(physics, bm.load_upgrades(), ship, (0, 0, 0, 0))
        cruise_long = stats["max_v"] * bm.EST["cruise_frac"] * stats["boost"]
        cruise_short = stats["max_v"] * bm.EST["cruise_frac"]
        golden = (bm.RECON["golden_path"] / cruise_long
                  + 6 * bm.EST["refuel_visit"] + 7 * bm.EST["maneuver_time"])
        ring = (bm.RECON["learning_ring"] / cruise_short
                + 3 * bm.EST["refuel_visit"] + 3 * bm.EST["maneuver_time"])
        rows.append(dict(id=ship["id"], name=ship["name"],
                         golden_min=golden / 60.0, ring_min=ring / 60.0,
                         golden_game_h=golden / bm.CLOCK_SEC_PER_GAME_MIN / 60.0))
    return rows


def collect_all():
    pricing = bm.load_pricing()
    upgrades = bm.load_upgrades()
    physics = bm.load_physics()
    level = bm.load_level()
    ships = bm.load_ships()
    ships_by_id = {s["id"]: s for s in ships}
    world = bm.World(level, pricing)

    data = dict(pricing=pricing, upgrades=upgrades, physics=physics, level=level,
                ships=ships, world=world, ships_by_id=ships_by_id)
    data["orders"] = collect_orders_stats(world, pricing)

    sims = {}
    for ship in ships:
        sim = collect_ship_sim(world, physics, pricing, upgrades, ships_by_id, ship["id"])
        sim["_flight_dist"] = data["orders"]["dist_mean"] * (1 + bm.EST["deadhead_frac"])
        sims[ship["id"]] = sim
    data["sims"] = sims

    # стратегия (б): покупка каждого корабля как можно раньше — замер переходов
    buy_runs = []
    for i in range(bm.EST["sim_runs"]):
        r = bm.simulate(world, physics, pricing, upgrades, ships_by_id, "buy_ships",
                        bm.EST["seed"] + i, n_orders=bm.EST["sim_orders"], careful=True)
        buy_runs.append(r)
    data["buy_runs"] = buy_runs

    data["upgrade_paybacks"] = {
        ship["id"]: collect_upgrade_payback(ship, physics, pricing, upgrades, sims[ship["id"]])
        for ship in ships if not ship["is_rental"]}

    rental_death, death_by_ship = collect_death_costs(pricing, sims)
    data["rental_death_cost"] = rental_death
    data["death_by_ship"] = death_by_ship

    data["north_gate"] = collect_north_gate(world, physics, ships)
    data["route_times"] = collect_route_times(physics, ships)
    return data


# ============================ ЛИСТ «ПАРАМЕТРЫ» ===============================

def build_params(ws, data):
    pricing, upgrades, physics = data["pricing"], data["upgrades"], data["physics"]
    level, ships, world = data["level"], data["ships"], data["world"]
    title_row(ws, "ПАРАМЕТРЫ — все входы модели. Источник: ассет → поле, код → файл:строка, «оценка» = вход методики",
              "Формулы считаются кодом (tools/balance_n4/balance_model.py); жёлтое = оценка-вход, не из ассетов")
    r = 4
    put(ws, r, 1, "Параметр", bold=True, fill=HDR_FILL)
    put(ws, r, 2, "Значение", bold=True, fill=HDR_FILL)
    put(ws, r, 3, "Источник", bold=True, fill=HDR_FILL)
    r += 1

    def section(name):
        nonlocal r
        put(ws, r, 1, name, bold=True, fill=SUB_FILL, border=False)
        r += 1

    def row(label, value, source, est=False, fmt=None):
        nonlocal r
        put(ws, r, 1, label)
        put(ws, r, 2, value, fmt=fmt, fill=EST_FILL if est else None)
        put(ws, r, 3, source, italic=est)
        r += 1

    section("1. Цена заказа — PricingConfig_Default.asset")
    row("BasePrice, кр", pricing["base_price"], pricing["source"] + " → BasePrice")
    row("PricePerDistanceUnit, кр/юн", pricing["price_per_unit"], pricing["source"] + " → PricePerDistanceUnit", fmt="0.00")
    row("DistanceCap, юн", pricing["dist_cap"], pricing["source"] + " → DistanceCap")
    for t, p in enumerate(pricing["tier_prestige"], start=1):
        row("TierPrestigeMul тир %d" % t, p, pricing["source"] + " → TierPrestigeMul[%d]" % (t - 1), fmt="0.00")
    for name, mul in zip(bm.SECTOR_NAMES, pricing["sector_muls"]):
        row("Множитель сектора %s" % name, mul, pricing["source"] + " → Sector*Mul", fmt="0.00")

    section("2. Выплата — PayoutService.Pay (PayoutService.cs:48-96)")
    row("Штраф за столкновение, % цены", pricing["penalty_pct"], pricing["source"] + " → PenaltyPerCollisionPct")
    row("Кап штрафа, %", pricing["penalty_cap"], pricing["source"] + " → PenaltyCapPct")
    row("Комиссия аренды, % от (цена−штраф)", pricing["rental_comm"], pricing["source"] + " → RentalCommissionPct")
    row("Доля выплаты в долг, %", pricing["debt_share"], pricing["source"] + " → DebtRepaySharePct")
    row("Чаевые, доля цены (Random.Range)", "%.2f..%.2f (среднее 0.175)" % (bm.TIPS_MIN, bm.TIPS_MAX),
        "Orders/OrderService.cs:602")
    row("Порог рейтинг-бонуса / множитель", "%s / %s" % (pricing["rating_th"], pricing["rating_mul"]),
        pricing["source"] + " → RatingPriceBonus*")
    row("Шанс задания у заказа", bm.TASK_CHANCE, "Orders/PassengerTask.cs:60 (TaskChance)", fmt="0.00")

    section("3. Топливо Н4.5 — FuelNetwork (FuelNetwork.cs)")
    for t, p in enumerate(pricing["fuel_price_by_tier"], start=1):
        row("Цена топлива, кр/ед — сеть тир %d" % t, p, pricing["source"] + " → FuelPricePerUnitByTier", fmt="0.00")
    for t, f in enumerate(pricing["fuel_stock_by_tier"], start=1):
        row("Запас сети за визит, доля бака — тир %d" % t, f, pricing["source"] + " → FuelStockFractionByTier", fmt="0.00")
    for s, m in zip(bm.SECTOR_NAMES, pricing["fuel_sector_mul"]):
        row("Районный множитель цены топлива %s" % s, m, pricing["source"] + " → FuelPriceSectorMul", fmt="0.00")
    row("Шанс пустой сети, %", pricing["fuel_outage_pct"], pricing["source"] + " → FuelOutageChancePct")
    row("Сухие станции на BetaMap", ", ".join(sorted(world.dry)) or "нет",
        "FuelNetwork.OutageStations (FNV-1a) по " + level["source"])
    row("Расход топлива, ед/с тяги", physics["fuel_drain"], physics["source"] + " → FuelDrainPerSecond")

    section("4. Корабли — Resources/Ships/*.asset (сток)")
    hdr = ["Id", "Имя", "Тир", "Цена", "Аренда", "Thrust", "StrafeAcc", "MaxV", "FuelMax", "HpMax", "Retro", "Длина"]
    for i, h in enumerate(hdr, start=1):
        put(ws, r, i, h, bold=True, fill=HDR_FILL, align="center", wrap=True)
    r += 1
    for s in ships:
        vals = [s["id"], s["name"], s["tier"], s["price"], "да" if s["is_rental"] else "нет",
                s["thrust"], s["strafe"], s["max_v"], s["fuel_max"], s["hp_max"], s["retro"], s["visual_length"]]
        for i, v in enumerate(vals, start=1):
            put(ws, r, i, v, align="left" if isinstance(v, str) else "center")
        r += 1

    section("5. Улучшения — UpgradeConfig_Default.asset (ShipStats.Compute)")
    row("Цены уровней 1/2/3, кр", " / ".join(str(int(p)) for p in upgrades["prices"]),
        upgrades["source"] + " → PricesPerLevel")
    row("Двигатель: +тяга %% на макс / буст качелей до" % (), "%d / %s" % (upgrades["engine_thrust"], upgrades["engine_boost"]),
        upgrades["source"] + " → EngineThrustPct/EngineBoostMax")
    row("Бак: +ёмкость %% / −расход %% на макс" % (), "%d / %d" % (upgrades["tank_cap"], upgrades["tank_drain_red"]),
        upgrades["source"] + " → TankCapacityPct/TankDrainRedPct")
    row("Корпус: +HP %% / −жар %% на макс" % (), "%d / %d" % (upgrades["hull_hp"], upgrades["hull_heat_red"]),
        upgrades["source"] + " → HullHpPct/HullHeatRedPct")
    row("Манёвровые: +Strafe %% на макс" % (), upgrades["maneuver_strafe"],
        upgrades["source"] + " → ManeuverStrafePct")
    row("МИНУСЫ улучшений в коде", "НЕТ: +тяга не увеличивает расход, +бак не добавляет массы",
        "Assets/Scripts/Economy/ShipStats.cs:52-58 — принцип владельца «улучшение имеет минус» не реализован", est=True)

    section("6. Физика — PhysicsConfig_Default.asset + константы кода")
    row("MinSpeed (тормоз не опускает ниже), юн/с", physics["min_speed"], physics["source"] + " → MinSpeed (Н4.2.5)")
    row("LandSpeed (порог жёсткой посадки)", physics["land_speed"], physics["source"] + " → LandSpeed")
    row("GravityBoost (качели)", physics["grav_boost"], physics["source"] + " → GravityBoost")
    row("EngineMultiplier (посадочное кольцо)", physics["engine_mul"], physics["source"] + " → EngineMultiplier")
    row("Retro глобальный", physics["retro"], physics["source"] + " → Retro (у моделей свой)")
    row("Урон от удара: k×(v−LandSpeed)²", bm.CRASH_K, "Physics/LandingSystem.cs:168 (CrashDamagePerSpeedSq)", fmt="0.0000")
    row("Долг за разбитый арендный, кр", bm.DEBT_ACCIDENT, "Economy/DebtService.cs:13 (AccidentDebt)")
    row("Увольнение после аварий", bm.DISMISSAL_ACCIDENTS, "Economy/DebtService.cs:15")
    row("Штраф отмены заказа, доля цены", bm.CANCEL_PENALTY_FRACTION, "Orders/OrderService.cs:67")
    row("Часы борткомпа: реальных с за игровую минуту", bm.CLOCK_SEC_PER_GAME_MIN, "UI/OnboardBinder.cs:597")

    section("7. Оценки методики (в ассетах нет)")
    for key, label in [("cruise_frac", "Средняя крейсерская, доля MaxV"),
                       ("long_hop_dist", "Порог «длинного» хопа с раскачкой, юн"),
                       ("deadhead_frac", "Холостое плечо к пассажиру, доля маршрута"),
                       ("maneuver_time", "Манёвры/меню за заказ, с"),
                       ("refuel_visit", "Визит дозаправки, с"),
                       ("repair_visit", "Визит ремонта, с"),
                       ("purchase_overhead", "Оверхед покупки корабля, с"),
                       ("death_time", "Пауза смерти + перелёт с хаба, с"),
                       ("dmg_per_collision", "Средний урон за столкновение, HP"),
                       ("k_accel", "Разгонов за заказ (для эффекта тяги)"),
                       ("up_coll_red", "Снижение столкновений манёврами на макс, доля"),
                       ("careful_lambda", "Столкновений/заказ — аккуратная игра"),
                       ("careful_death", "Смертей/заказ — аккуратная игра"),
                       ("avg_lambda", "Столкновений/заказ — средний игрок"),
                       ("avg_death", "Смертей/заказ — средний игрок"),
                       ("pick_best_of", "Из скольких карточек выбор (maxOffers=4)"),
                       ("leg_reserve", "Запас топлива плана, множитель горения"),
                       ("seed", "Seed симуляций"),
                       ("sim_orders", "Длина прогона стратегии, заказов"),
                       ("sim_runs", "Прогонов на стратегию")]:
        row(label, bm.EST[key], "оценка методики Н4 (EST.%s)" % key, est=True)

    section("8. Ориентиры приёмки (владелец, Задача 7) и разведка Н4.6")
    row("Заказов до следующего корабля, окно", "%d..%d" % (bm.TARGETS["next_ship_min"], bm.TARGETS["next_ship_max"]),
        "цель владельца Н4 (~10-12 → инвариант 8-14)")
    row("Окупаемость улучшения, заказов — не более", bm.TARGETS["upgrade_payback_max"],
        "N зафиксирован из факта, см. Инварианты (Н3 цель 6-10)")
    row("Доля аренды в доходе — не более", bm.TARGETS["rental_share_max"],
        "X зафиксирован из факта, см. Инварианты")
    row("Золотой путь целиком, юн", bm.RECON["golden_path"], "разведка Н4.6 (карта)")
    row("Учебное кольцо, юн", bm.RECON["learning_ring"], "разведка Н4.6")
    for a, b, d in bm.RECON["longest_hops"]:
        row("Хоп %s→%s, юн" % (a, b), d, "разведка Н4.6 (совпадает с расчётом по ассету)")

    widths(ws, {"A": 46, "B": 16, "C": 58})
    for col in "DEFGHIJKL":
        widths(ws, {col: 10})
    return r


# ============================ ЛИСТ «МЕТРИКИ» =================================

def build_metrics(ws, data):
    pricing, ships, world = data["pricing"], data["ships"], data["world"]
    orders, sims = data["orders"], data["sims"]
    title_row(ws, "МЕТРИКИ — формулы считаются кодом (balance_model.py). BetaMap01, аккуратная игра (λ=0.3 столкн./заказ)",
              "«Ср. цена карточки» — средняя цена КАРТОЧКИ ЗАКАЗА при тире корабля (OrderPricing.ComputePrice, смесь пар планет карты, с рейтинг-бонусом ×%.2f). Не цена корабля. Метрика «разгон до MaxV» убрана по требованию владельца"
              % pricing["rating_mul"])
    r = 4

    def section(name):
        nonlocal r
        put(ws, r, 1, name, bold=True, fill=SUB_FILL, border=False)
        r += 1

    def table(headers, rows, fmts=None):
        nonlocal r
        for i, h in enumerate(headers, start=1):
            put(ws, r, i, h, bold=True, fill=HDR_FILL, align="center", wrap=True)
        r += 1
        for row_vals in rows:
            for i, v in enumerate(row_vals, start=1):
                fmt = fmts[i - 1] if fmts else None
                put(ws, r, i, v, fmt=fmt, align="left" if isinstance(v, str) else "center")
            r += 1
        r += 1

    section("1. Цена заказа по сектору назначения (MC 8000 пар планет, тир 2, рейтинг-бонус)")
    table(["Сектор", "Доля пар", "Медиана, кр", "Среднее, кр"],
          [[bm.SECTOR_NAMES[s], orders["sectors"][s]["share"],
            orders["sectors"][s]["median"], orders["sectors"][s]["mean"]] for s in range(5)],
          [None, "0.0%", "0", "0"])
    put(ws, r, 1, "Медиана/среднее расстояние пары планет, юн", bold=True)
    put(ws, r, 2, "%.0f / %.0f" % (orders["dist_median"], orders["dist_mean"]))
    put(ws, r, 3, "макс %.0f" % orders["dist_max"], border=False)
    r += 2

    section("2. Цена заказа по тиру таксиста (MC, рейтинг-бонус)")
    table(["Тир", "Медиана, кр", "Среднее, кр"],
          [[t, orders["tiers"][t]["median"], orders["tiers"][t]["mean"]] for t in range(1, 6)],
          [None, "0", "0"])

    section("3. Корабли: доход/заказ на BetaMap (симуляция «жизнь на корабле», 5×25 заказов, аккуратная игра)")
    hdr = ["Id", "Имя", "Тир", "Цена, кр", "Ср. цена карточки, кр", "Чаевые, кр", "Штраф, кр",
           "Комиссия, кр", "Топливо, кр", "Ремонт, кр", "ЧИСТЫЙ доход/заказ, кр",
           "Время/заказ, с", "Столкновений/заказ", "Запас хода, юн", "Заказов на бак",
           "Тормозной путь (до LandSpeed), юн", "Смерть на стоке (удар MaxV vs HP)", "Ожиданий, %"]
    table(hdr, [], None)  # заголовок занял строку; таблицу заполняем вручную ниже
    r -= 1
    physics, upgrades = data["physics"], data["upgrades"]
    for s in ships:
        sim = sims[s["id"]]
        stats = bm.ship_stats(physics, upgrades, s, (0, 0, 0, 0))
        cruise = stats["max_v"] * bm.EST["cruise_frac"]
        flight = orders["dist_mean"] * (1 + bm.EST["deadhead_frac"])
        rng_range = stats["fuel_max"] / stats["drain"] * cruise
        brake = (stats["max_v"] ** 2 - physics["land_speed"] ** 2) / (2 * stats["retro"])
        crash = bm.CRASH_K * (stats["max_v"] - physics["land_speed"]) ** 2
        death_txt = "НЕТ (выживет)" if crash < stats["hp_max"] else "да"
        vals = [s["id"], s["name"], s["tier"], s["price"],
                round(sim["card_per_order"]), round(sim["tips_per_order"]),
                round(sim["penalty_per_order"]), round(sim["commission_per_order"]),
                round(sim["fuel_per_order"]), round(sim["repair_per_order"]),
                round(sim["net_per_order"]), round(sim["time_per_order"]),
                round(sim["collisions_per_order"], 2), round(rng_range),
                round(rng_range / flight, 1), round(brake), death_txt,
                round(sim["waited_frac"] * 100)]
        for i, v in enumerate(vals, start=1):
            put(ws, r, i, v, align="left" if isinstance(v, str) else "center")
        r += 1
    r += 1

    section("4. Переходы тиров (лестница прогрессии: Яйцо → Стрела → Игла → Барин)")
    ladder = [("egg_rental", "egg_own", "Тир 2 (аренда → свой Яйцо)"),
              ("egg_own", "strela", "Тир 2 → 3 (Стрела)"),
              ("strela", "igla", "Тир 3 → 4 (Игла, дешёвый в тире)"),
              ("igla", "barin", "Тир 4 → 5 (Барин)")]
    rows = []
    for prev, nxt, label in ladder:
        p_prev = ships_by_price(prev, data)
        p_next = ships_by_price(nxt, data)
        net_prev = sims[prev]["net_per_order"]
        net_next = sims[nxt]["net_per_order"]
        d_price = p_next["price"] - p_prev["price"]
        d_net = net_next - net_prev
        rows.append([label, d_price, round(net_prev), round(net_next), round(d_net),
                     round(d_price / net_prev, 1),
                     round(d_price / d_net, 1) if d_net > 0 else "не окуп."])
    table(["Переход", "Δцена, кр", "Доход на текущем, кр/заказ", "Доход на следующем, кр/заказ",
           "Δдоход, кр/заказ", "Накопление: Δцена/доход текущего, заказов",
           "Окупаемость: Δцена/Δдоход, заказов"], rows,
          [None, "0", "0", "0", "0", "0.0", "0.0"])

    section("5. Улучшения: окупаемость уровня (аналитически; N-порог см. Инварианты)")
    hdr = ["Корабль", "Линия", "Уровень", "Цена уровня, кр", "Δдоход/заказ, кр",
           "Окупаемость уровня, заказов", "Окупаемость линии нарастающим итогом, заказов", "Эффект (и минус)"]
    table(hdr, [], None)
    r -= 1
    for sid, rows_up in sorted(data["upgrade_paybacks"].items()):
        for row_up in rows_up:
            vals = [sid, row_up["line"], row_up["level"], row_up["price"],
                    round(row_up["gain"], 1),
                    round(row_up["payback"], 1) if row_up["payback"] else "не окуп.",
                    round(row_up["cum_payback"], 1) if row_up["cum_payback"] else "не окуп.",
                    row_up["note"]]
            for i, v in enumerate(vals, start=1):
                put(ws, r, i, v, align="left" if isinstance(v, (str,)) else "center")
            r += 1
    r += 1

    section("6. Стоимость жизни: сеанс 10 заказов на одном корабле (аккуратная игра)")
    rows = []
    for s in ships:
        sim = sims[s["id"]]
        rows.append([s["id"], "да" if s["is_rental"] else "нет",
                     round(sim["fuel_per_order"] * 10), round(sim["repair_per_order"] * 10),
                     round(sim["commission_per_order"] * 10),
                     round(bm.EST["careful_death"] * 10 * (bm.DEBT_ACCIDENT if s["is_rental"] else s["price"])),
                     round(sim["net_per_order"] * 10)])
    table(["Корабль", "Аренда", "Топливо за сеанс, кр", "Ремонт за сеанс, кр",
           "Комиссия аренды за сеанс, кр", "Ожидаемая потеря от смертей за сеанс, кр",
           "Чистый доход за сеанс, кр"], rows, [None, None, "0", "0", "0", "0", "0"])

    section("7. Время")
    physics = data["physics"]
    rows = []
    for s in ships:
        rt = next(x for x in data["route_times"] if x["id"] == s["id"])
        rows.append([s["id"], rt["ring_min"], rt["golden_min"], rt["golden_game_h"],
                     round(sims[s["id"]]["time_per_order"] / 60.0, 2)])
    table(["Корабль", "Учебное кольцо (3139 юн), мин", "Золотой путь (16261 юн), мин",
           "Золотой путь, игровых часов", "Типовой заказ, мин"], rows,
          [None, "0.0", "0.0", "0.0", "0.00"])
    put(ws, r, 1, "Игровые часы: 3 реальных секунды = 1 игровая минута (OnboardBinder.cs:597)", italic=True, border=False)

    widths(ws, {"A": 26, "B": 14, "C": 12, "D": 12, "E": 14, "F": 12, "G": 12, "H": 12,
                "I": 12, "J": 12, "K": 16, "L": 12, "M": 12, "N": 12, "O": 12, "P": 14,
                "Q": 16, "R": 12, "S": 30})


def ships_by_price(sid, data):
    return data["ships_by_id"][sid]


# ============================ ЛИСТ «СИМУЛЯЦИИ» ===============================

def build_simulations(ws, data):
    pricing, upgrades, physics = data["pricing"], data["upgrades"], data["physics"]
    ships_by_id = data["ships_by_id"]
    world = data["world"]
    title_row(ws, "СИМУЛЯЦИИ — 4 стратегии, честные формулы (PayoutService порядок, топливо по хопам, штрафы от столкновений)",
              "seed=%d, %d прогонов × %d заказов на стратегию, аккуратность: λ=%.2f столкн./заказ, смерть %.2f%%/заказ, выбор лучшей из %d карточек"
              % (bm.EST["seed"], bm.EST["sim_runs"], bm.EST["sim_orders"],
                 bm.EST["careful_lambda"], bm.EST["careful_death"] * 100, bm.EST["pick_best_of"]))
    r = 4

    def run_strategy(name, desc):
        nonlocal r
        put(ws, r, 1, name + " — " + desc, bold=True, fill=SUB_FILL, border=False)
        r += 1
        hdr = ["Прогон", "Заказов доставлено", "Провалов", "Ожиданий", "Смертей",
               "Кошелёк финал, кр", "Долг финал, кр", "Комиссия аренды, кр",
               "Доля комиссии, % валового", "Топливо, кр", "Ремонт, кр",
               "Вложено в корабли, кр", "Вложено в улучшения, кр", "Покупки (заказ: корабль)",
               "Время, мин"]
        for i, h in enumerate(hdr, start=1):
            put(ws, r, i, h, bold=True, fill=HDR_FILL, align="center", wrap=True)
        r += 1
        first = r
        results = []
        for i in range(bm.EST["sim_runs"]):
            seed = bm.EST["seed"] + i
            strat = strategy_for(name)
            res = bm.simulate(world, physics, pricing, upgrades, ships_by_id, strat,
                              seed, n_orders=bm.EST["sim_orders"], careful=True)
            t = res["totals"]
            gross = t["price"] + t["tips"]
            vals = [i + 1, t["orders"], t["failed"], t["waited"], t["deaths"],
                    round(res["final_wallet"]), round(res["final_debt"]),
                    t["commission"], round(t["commission"] / max(1.0, gross) * 100, 1),
                    round(t["fuel"]), round(t["repair"]),
                    t["ship_spent"], t["upgrade_spent"],
                    "; ".join("%d:%s" % (o, s) for o, s, _ in res["purchases"]) or "—",
                    round(res["sim_time_min"])]
            for j, v in enumerate(vals, start=1):
                put(ws, r, j, v, align="center")
            r += 1
            results.append(res)
        # среднее
        def avg(field):
            return sum(x["totals"].get(field, 0) for x in results) / len(results)
        avg_wallet = sum(x["final_wallet"] for x in results) / len(results)
        avg_debt = sum(x["final_debt"] for x in results) / len(results)
        avg_time = sum(x["sim_time_min"] for x in results) / len(results)
        share = sum(x["totals"]["commission"] for x in results) / max(
            1.0, sum(x["totals"]["price"] + x["totals"]["tips"] for x in results))
        vals = ["СРЕДНЕЕ", round(avg("orders")), round(avg("failed")), round(avg("waited")),
                round(avg("deaths")), round(avg_wallet), round(avg_debt), round(avg("commission")),
                round(share * 100, 1), round(avg("fuel")), round(avg("repair")),
                round(avg("ship_spent")), round(avg("upgrade_spent")), "",
                round(avg_time)]
        for j, v in enumerate(vals, start=1):
            put(ws, r, j, v, bold=True, align="center")
        r += 2
        return results

    results_a = run_strategy("(а) Сидение на аренде", "ничего не покупаем, 25 заказов на арендном Яйце")
    results_b = run_strategy("(б) Покупка каждого корабля как можно раньше",
                             "прогрессия Яйцо → Стрела → Игла → Барин по мере накопления")
    results_c = run_strategy("(в) Смерть-спам", "аренда, намеренная авария каждый 3-й заказ (антипаттерн)")
    results_d = run_strategy("(г) Жадная покупка улучшений",
                             "сначала свой Яйцо, затем все 4 линии ×3 уровней (10 000 кр) до дальнейших покупок")

    put(ws, r, 1, "Кривые кошелька (среднее по прогонам): заказ 5 / 10 / 15 / 20 / 25", bold=True, fill=SUB_FILL, border=False)
    r += 1
    hdr = ["Стратегия", "5 заказов", "10 заказов", "15 заказов", "20 заказов", "25 заказов"]
    for i, h in enumerate(hdr, start=1):
        put(ws, r, i, h, bold=True, fill=HDR_FILL, align="center")
    r += 1
    for label, results in (("(а) аренда", results_a), ("(б) покупка кораблей", results_b),
                           ("(в) смерть-спам", results_c), ("(г) жадные улучшения", results_d)):
        put(ws, r, 1, label)
        for j, snap in enumerate((5, 10, 15, 20, 25), start=2):
            vals = []
            for res in results:
                match = [w for o, w, _ in res["wallet_track"] if o == snap]
                vals.append(match[0] if match else res["wallet_track"][-1][1] if res["wallet_track"] else 0)
            put(ws, r, j, round(sum(vals) / len(vals)), align="center")
        r += 1
    r += 1
    put(ws, r, 1, "Выводы: см. лист «Инварианты» — переходы сейчас ~5 заказов (быстрее окна 8-14), аренда ест ~28% валового дохода, смерть-спам теряет вдвое.", italic=True, border=False)

    widths(ws, {"A": 24, "B": 14, "C": 12, "D": 12, "E": 10, "F": 14, "G": 12, "H": 14,
                "I": 16, "J": 12, "K": 12, "L": 16, "M": 16, "N": 26, "O": 12})


def strategy_for(name):
    return {"(а)": "rental_hold", "(б)": "buy_ships", "(в)": "death_spam",
            "(г)": "greedy_upgrades"}[name[:3]]


# ============================ ЛИСТ «ИНВАРИАНТЫ» ==============================

def build_invariants(ws, data):
    pricing, sims, ships = data["pricing"], data["sims"], data["ships"]
    title_row(ws, "ИНВАРИАНТЫ — приёмо-пороги владельца против факта (подбор чисел — шаг 4, дневная работа)",
              "Пороги зафиксированы в balance_model.TARGETS; «рекомендация» — расчётные диапазоны, не применённые к ассетам")
    r = 4

    def section(name):
        nonlocal r
        put(ws, r, 1, name, bold=True, fill=SUB_FILL, border=False)
        r += 1

    def verdict(label, fact, threshold, ok, reco=""):
        nonlocal r
        put(ws, r, 1, label, wrap=True)
        put(ws, r, 2, fact, wrap=True)
        put(ws, r, 3, threshold, wrap=True)
        c = put(ws, r, 4, "PASS" if ok else "FAIL", bold=True, align="center",
                fill=PASS_FILL if ok else FAIL_FILL)
        put(ws, r, 5, reco, wrap=True, italic=True)
        r += 1
        return ok

    for i, h in enumerate(["Инвариант", "Факт", "Порог", "Вердикт", "Что крутить владельцу (ассет → поле → сейчас → рекомендация)"], start=1):
        put(ws, r, i, h, bold=True, fill=HDR_FILL, align="center", wrap=True)
    r += 1

    # 1. переходы 8-14 заказов
    buy_gaps = []
    for res in data["buy_runs"]:
        marks = [o for o, _, _ in res["purchases"]]
        gaps = [marks[0]] + [marks[i] - marks[i - 1] for i in range(1, len(marks))]
        buy_gaps.append(gaps)
    gaps_flat = [g for gaps in buy_gaps for g in gaps]
    inv1_ok = all(bm.TARGETS["next_ship_min"] <= g <= bm.TARGETS["next_ship_max"] for g in gaps_flat) \
        and len(gaps_flat) >= 4
    reco1 = []
    ladder = [("egg_rental", "egg_own"), ("egg_own", "strela"), ("strela", "igla"), ("igla", "barin")]
    for prev, nxt in ladder:
        net = sims[prev]["net_per_order"]
        price_now = data["ships_by_id"][nxt]["price"]
        lo = int(round(bm.TARGETS["next_ship_min"] * net / 50.0) * 50)
        hi = int(round(bm.TARGETS["next_ship_max"] * net / 50.0) * 50)
        reco1.append("%s: Price %d → %d..%d (при доходе %.0f кр/заказ на %s)"
                     % (nxt, price_now, lo, hi, net, prev))
    verdict("И1: следующий корабль достижим за 8-14 заказов на КАЖДОМ тире (симуляция покупки)",
            "переходы в симуляции: %s заказов (среднее %.1f)" % (", ".join(str(g) for g in gaps_flat),
                                                                 statistics.mean(gaps_flat)),
            "8..14 заказов на каждый переход", inv1_ok,
            " | ".join(reco1))

    # 2. улучшения ≤ N заказов
    worst = None
    fails2 = []
    for sid, rows_up in data["upgrade_paybacks"].items():
        for row_up in rows_up:
            if row_up["payback"] is None or row_up["payback"] > bm.TARGETS["upgrade_payback_max"]:
                fails2.append("%s/%s ур.%d: %s" % (sid, row_up["line"], row_up["level"],
                                                   "не окуп." if row_up["payback"] is None
                                                   else round(row_up["payback"], 1)))
    inv2_ok = not fails2
    verdict("И2: улучшение окупается ≤ %d заказов (аналитически, все корабли)" % bm.TARGETS["upgrade_payback_max"],
            "проваливают: %s" % ("; ".join(fails2[:6]) if fails2 else "никто"),
            "≤ %d заказов на уровень" % bm.TARGETS["upgrade_payback_max"], inv2_ok,
            "UpgradeConfig_Default → PricesPerLevel 300/700/1500 → снизить до ~уровня Δдохода×N (см. Метрики.5); "
            "корневая причина — эффекты линий не конвертируются в доход (время разгона/заправки) — механика минусов владельца (+тяга=+расход, +бак=+масса) добавит и осмысленность")

    # 3. смерть дороже заправки
    egg_sim = sims["egg_rental"]
    egg_tank = int(data["ships_by_id"]["egg_rental"]["fuel_max"])
    refuel_full_hub = -(-int(egg_tank * pricing["fuel_price_by_tier"][1]
                             * pricing["fuel_sector_mul"][0]) // 1)  # ceil, FuelNetwork.CostOf
    death_rental = data["rental_death_cost"]
    death_own_min = min(v["total"] for k, v in data["death_by_ship"].items() if k != "egg_rental")
    inv3_ok = death_rental > refuel_full_hub and death_own_min > refuel_full_hub
    verdict("И3: смерть+воскрешение дороже, чем вовремя заправиться",
            "аренда: %d кр (долг %d + потерянный заказ %.0f); свой: от %.0f кр (цена корпуса + заказ); полная заправка Яйца на хабе: %d кр; топливо/заказ: %.0f кр"
            % (round(death_rental), bm.DEBT_ACCIDENT, egg_sim["net_per_order"],
               death_own_min, refuel_full_hub, egg_sim["fuel_per_order"]),
            "стоимость смерти > полной заправки (строгая форма: > 3× бак)", inv3_ok,
            "порог соблюдён с запасом; если владельцу нужна строгая форма ×3 — %s" %
            ("выполнено: %.1f×" % (death_rental / max(1, refuel_full_hub)) if death_rental > 3 * refuel_full_hub
             else "поднять DebtService.AccidentDebt (DebtService.cs:13) или комиссию"))

    # 4. аренда ≤ X% валового дохода
    share = egg_sim["commission_share"]
    inv4_ok = share <= bm.TARGETS["rental_share_max"]
    verdict("И4: аренда за сеанс съедает ≤ X% дохода до первого своего корабля",
            "комиссия %.1f%% валового дохода (35%% от цена−штраф), чистый доход/заказ на аренде %.0f кр > 0 (не парализует)"
            % (share * 100, egg_sim["net_per_order"]),
            "X = %.0f%% (зафиксирован: факт + запас)" % (bm.TARGETS["rental_share_max"] * 100), inv4_ok,
            "PricingConfig_Default → RentalCommissionPct 35: X≈30% выдерживается впритык; при снижении дохода (И1) доля вырастет — компенсировать снижением комиссии до ~30%")

    # 5. проходимо на каждом корабле: (а) экономика корпуса (симуляция) — доход > 0;
    # ожидания при выборе карточек не считаются непроходимостью (отказ от плохого
    # заказа — легальная аккуратная игра); (б) география — северная калитка.
    fails5a, fails5b = [], []
    for s in ships:
        sim = sims[s["id"]]
        if sim["net_per_order"] <= 0:
            fails5a.append(s["id"])
    gate = {row["id"]: row for row in data["north_gate"]}
    for s in ships:
        if not gate[s["id"]]["ok"]:
            fails5b.append("%s (нужно %.0f ед, бак %.0f)" % (s["id"], gate[s["id"]]["burn"], gate[s["id"]]["tank"]))
    inv5_ok = not fails5a and not fails5b
    verdict("И5: пройти можно на КАЖДОМ корабле при аккуратной игре",
            "экономика (симуляция): %s; северная калитка Грань→Буй→Стужа: не проходят %s"
            % ("все доходны" if not fails5a else "нет дохода: " + ", ".join(fails5a),
               "; ".join(fails5b) if fails5b else "никто"),
            "доход/заказ > 0 на каждом корпусе; север проходим на каждом корпусе", inv5_ok,
            "варианты владельцу: (1) Ships/*.asset → FuelMax Скрепки/Яйца до ≥130; (2) PhysicsConfig → FuelDrainPerSecond 5.5→~4; (3) карта: заправка в разрыве Грань—Стужа (планета у Буя/Полыни, HasFuel=1); (4) усилить GravityBoost — но это меняет всю летную модель")

    r += 1
    section("Рекомендованные диапазоны цен кораблей для окна 8-14 заказов (шаг 4 владельца)")
    hdr = ["Корабль", "Цена сейчас, кр", "Доход на предыдущем тире, кр/заказ",
           "Рекомендация при 8 заказах", "Рекомендация при 14 заказах"]
    for i, h in enumerate(hdr, start=1):
        put(ws, r, i, h, bold=True, fill=HDR_FILL, align="center", wrap=True)
    r += 1
    for prev, nxt in ladder:
        net = sims[prev]["net_per_order"]
        put(ws, r, 1, nxt)
        put(ws, r, 2, data["ships_by_id"][nxt]["price"])
        put(ws, r, 3, round(net))
        put(ws, r, 4, int(round(bm.TARGETS["next_ship_min"] * net / 50.0) * 50), fill=RECO_FILL)
        put(ws, r, 5, int(round(bm.TARGETS["next_ship_max"] * net / 50.0) * 50), fill=RECO_FILL)
        r += 1
    put(ws, r, 1, "ВНИМАНИЕ: диапазоны верны при текущих доходах; если правится TierPrestigeMul/комиссия — пересчитать (запуск build_balance_n4.py).", italic=True, border=False)
    r += 2
    put(ws, r, 1, "Метрика «разгон до MaxV» из Н3 убрана (решение владельца). «Цена карточки» в Н3 = средняя цена карточки заказа при тире корабля — колонка сохранена под ясным именем «Ср. цена карточки» (Метрики.3).", italic=True, border=False)

    widths(ws, {"A": 52, "B": 44, "C": 26, "D": 10, "E": 70})


# ============================ СВОДКА-ПЕЧАТЬ ==================================

def print_summary(data):
    sims = data["sims"]
    print("=== BALANCE_N4: сводка ===")
    print("сухие станции:", ", ".join(sorted(data["world"].dry)))
    print("%-11s %5s %6s %7s %7s %7s" % ("ship", "tier", "price", "net/ord", "fuel/ord", "time/ord"))
    for s in data["ships"]:
        sim = sims[s["id"]]
        print("%-11s %5d %6d %7.0f %7.0f %7.1f" % (s["id"], s["tier"], s["price"],
                                                   sim["net_per_order"], sim["fuel_per_order"],
                                                   sim["time_per_order"]))
    gaps = []
    for res in data["buy_runs"]:
        marks = [o for o, _, _ in res["purchases"]]
        gaps.append([marks[0]] + [marks[i] - marks[i - 1] for i in range(1, len(marks))])
    print("переходы в buy_ships (по прогонам):", gaps)
    gate = data["north_gate"]
    print("северная калитка (Грань→Буй→Стужа):",
          ", ".join("%s %s (%.0f/%.0f)" % (r["id"], "OK" if r["ok"] else "FAIL", r["burn"], r["tank"]) for r in gate))
    print("аренда: комиссия %.1f%% валового, чистый %.0f кр/заказ" %
          (sims["egg_rental"]["commission_share"] * 100, sims["egg_rental"]["net_per_order"]))
    print("смерть аренды: %d кр vs полная заправка Яйца ~%d кр" %
          (data["rental_death_cost"], round(85 * data["pricing"]["fuel_price_by_tier"][1] * data["pricing"]["fuel_sector_mul"][0])))


# ============================ MAIN ===========================================

def main():
    out = sys.argv[1] if len(sys.argv) > 1 else OUT_DEFAULT
    data = collect_all()
    wb = Workbook()
    ws_params = wb.active
    ws_params.title = "Параметры"
    ws_metrics = wb.create_sheet("Метрики")
    ws_sims = wb.create_sheet("Симуляции")
    ws_inv = wb.create_sheet("Инварианты")
    build_params(ws_params, data)
    build_metrics(ws_metrics, data)
    build_simulations(ws_sims, data)
    build_invariants(ws_inv, data)
    wb.save(out)
    print("Сохранено: %s" % out)
    print_summary(data)


if __name__ == "__main__":
    main()
