#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
build_balance.py — генератор BALANCE_N3.xlsx (Н3.1, часть A).
Единственный источник баланса: всё на формулах Excel, входы — жёлтая заливка.
Перегенерация: править входные константы ниже и запускать заново.
Запуск: python3 build_balance.py [путь-к-xlsx]
"""
import sys
import math
from openpyxl import Workbook
from openpyxl.styles import Font, PatternFill, Alignment, Border, Side
from openpyxl.formatting.rule import FormulaRule
from openpyxl.utils import get_column_letter

# ============================ ВХОДЫ =========================================
# Assets/ScriptableObjects/Resources/Ships/*.asset (текущие значения)
# (Id, Имя, Tier, Price, IsRental, Thrust, StrafeAcc, MaxV, FuelMax, HpMax, Retro, VisualLength)
# Н3.1: значения после применения баланса (merge 16ea702, сверено с ассетами)
SHIPS = [
    ("skrepka",    "Скрепка",       1,   900, 0,  88,  80, 305,  90,  90,  98, 32),
    ("egg_rental", "Яйцо (аренда)", 2,     0, 1,  95,  85, 330,  85, 100, 120, 34),
    ("egg_own",    "Яйцо",          2,  1550, 0,  95,  85, 330,  85, 100, 120, 34),
    ("strela",     "Стрела",        3,  2400, 0, 118, 100, 410, 120, 110, 198, 36),
    ("kaban",      "Кабан",         3,  3000, 0, 100,  75, 350, 200, 150, 143, 42),
    ("delta",      "Дельта",        3,  3400, 0, 110,  95, 385, 140, 130, 174, 39),
    ("igla",       "Игла",          4,  4600, 0, 135, 125, 470, 130,  90, 276, 35),
    ("meridian",   "Меридиан",      4,  7800, 0, 118, 110, 410, 170, 140, 208, 42),
    ("barin",      "Барин",         5, 10600, 0, 140, 115, 485, 240, 190, 310, 75),
]

# Assets/ScriptableObjects/Resources/UpgradeConfig_Default.asset
UP_PRICES = [300, 700, 1500]
UP_PCT = dict(engine_thrust=25, engine_boost_max=1.3, tank_cap=60, tank_drain_red=15,
              hull_hp=60, hull_heat_red=30, maneuver_strafe=35)

# Assets/ScriptableObjects/Resources/PricingConfig_Default.asset
PR = dict(base_price=45, price_per_unit=0.28, dist_cap=1500,
          sector_muls=[1.0, 1.0, 1.3, 1.6, 2.0],          # Hub/Near/Mid1/Mid2/Far
          sector_weights=[0.0, 1.0, 0.0, 0.0, 0.0],        # ОЦЕНКА: TestLevel01 — все планеты Near
          penalty_pct=15, penalty_cap=45, rental_comm=35, debt_share=50,
          rating_th=4.0, rating_mul=1.1, low_rating_th=2.0, low_rating_slow=1.3,
          meridian_tips=1.1,
          fuel_price=[0.8, 1.0, 1.3, 1.8], fuel_stock=[0.35, 0.7, 1.0, 1.0],
          repair_pack_frac=[0.25, 0.6, 1.0], repair_pack_mul=[0.8, 1.0, 1.2],
          repair_cap=[0.3, 0.7, 1.0])

# ОЦЕНКИ-ВХОДЫ (в ассетах/коде в таком виде нет — на листах помечены «(оценка)»)
EST = dict(tier_prestige=[1.0, 1.26, 1.52, 2.26, 4.28],  # TierPrestigeMul: применён в коде Н3.1 (PricingConfig, OrderPricing.ComputePrice)
           rating_expected=1.1,                       # стационар: рейтинг >= 4.0 (старт 3.0, +0.15/доставка)
           tips_min=0.10, tips_max=0.25,              # OrderService.cs: Random.Range(0.10f, 0.25f)
           task_chance=0.50,                          # PassengerTasks: 50%, бонус = +0.10 рейтинга (не деньги)
           collisions_per_order=1.0,                  # оценка: столкновений за заказ
           avg_damage_per_order=40.0,                 # оценка: средний урон за заказ, HP
           fuel_tier_typ=2,                           # типичная сеть заправки: тир 2 (TestLevel01 FuelTier не задан)
           deadhead=1.0,                              # подлёт к пассажиру ~ длине платного маршрута
           avg_speed_frac=0.60,                       # средняя крейсерская = 0.6 x MaxV
           maneuver_time=25.0,                        # посадка/взлёт/меню за заказ, с (оценка)
           refuel_visit=90.0,                         # визит дозаправки, с (оценка)
           purchase_overhead=90.0,                    # покупка: перелёт в док + меню, с (оценка)
           start_wallet=0)

# Assets/ScriptableObjects/PhysicsConfig_Default.asset
PH = dict(retro=62, land_speed=75, takeoff_v=85, engine_mul=3.2, fuel_drain=5.5,
          grav_k=52000, grav_clamp=240, grav_boost=1.15,
          crash_k=0.0032)  # LandingSystem.CrashDamagePerSpeedSq (константа кода)

# Assets/Scripts/Economy/DebtService.cs
DEBT_ACCIDENT = 400
DEBT_DISMISSAL = 3

# Assets/Scripts/UI/OnboardBinder.cs: 3 реальные секунды = 1 игровая минута
CLOCK_SEC_PER_GAME_MIN = 3.0

# Assets/ScriptableObjects/TestLevel01.asset: планеты (имя, x, y); все Sector=Near (дефолт)
PLANETS = [("Гавань", 0, 0), ("Мишень", 330, 40), ("Верстак", -350, 180),
           ("Маяк", 120, -520), ("Кромка", 760, -260), ("Печь", 250, 950)]

# Цели владельца
TARGET_PASS_MIN, TARGET_PASS_MAX = 30, 35       # мин реального времени прохождения
TARGET_PAYBACK = (6, 10)                        # заказов на переход тира
TARGET_TANK_WEAK = (1.5, 2)                     # заказов на бак (тиры 1-2)
TARGET_TANK_MID = (3, 5)                        # (тиры 3-4)
TARGET_TANK_BARIN = 5                           # Барин >= 5
TARGET_BRAKE = 450                              # тормозной путь <= 450 юн (цель 350-450)

OUT_DEFAULT = "/Users/maxim/Desktop/SpaceTaxi/SpaceTaxiUnity/GameInfo/BALANCE_N3.xlsx"

# ============================ СТИЛИ =========================================
IN_FILL = PatternFill("solid", fgColor="FFFF00")            # входы — жёлтые
HDR_FILL = PatternFill("solid", fgColor="4472C4")
SUB_FILL = PatternFill("solid", fgColor="D9E1F2")
CF_RED = PatternFill("solid", fgColor="FFC7CE"); CF_RED_F = Font(color="9C0006")
CF_YEL = PatternFill("solid", fgColor="FFEB9C"); CF_YEL_F = Font(color="9C6500")
CF_GRN = PatternFill("solid", fgColor="C6EFCE"); CF_GRN_F = Font(color="006100")
THIN = Side(style="thin", color="B0B0B0")
BORDER = Border(left=THIN, right=THIN, top=THIN, bottom=THIN)

def cf_rules():
    return [FormulaRule(formula=["TRUE"]), FormulaRule(formula=["TRUE"]), FormulaRule(formula=["TRUE"])]

def rule3(ws, rng, gf, yf, rf):
    g, y, rd = cf_rules()
    g.formula = [gf]; g.fill = CF_GRN; g.font = CF_GRN_F
    y.formula = [yf]; y.fill = CF_YEL; y.font = CF_YEL_F
    rd.formula = [rf]; rd.fill = CF_RED; rd.font = CF_RED_F
    ws.conditional_formatting.add(rng, g)
    ws.conditional_formatting.add(rng, y)
    ws.conditional_formatting.add(rng, rd)

def put(ws, row, col, value, *, fmt=None, bold=False, fill=None, border=True, align=None, wrap=False):
    c = ws.cell(row=row, column=col, value=value)
    if fmt: c.number_format = fmt
    if bold: c.font = Font(bold=True)
    if fill: c.fill = fill
    if border: c.border = BORDER
    if align or wrap:
        c.alignment = Alignment(horizontal=align, wrap_text=wrap, vertical="top")
    return c

def title_row(ws, text):
    ws.cell(row=1, column=1, value=text).font = Font(bold=True, size=13)

def note_row(ws, text, row):
    ws.cell(row=row, column=1, value=text).font = Font(italic=True, size=9, color="555555")

def widths(ws, spec):
    for col, w in spec.items():
        ws.column_dimensions[col].width = w

E = "'ЭКОНОМИКА'"
K = "'КОРАБЛИ'"
U = "'УЛУЧШЕНИЯ'"
S = "'СЦЕНАРИИ'"

# ============================ ЛИСТ ЭКОНОМИКА ================================
def build_economy(ws):
    """Входной лист. Возвращает dict адресов (лист!$B$row) для ссылок."""
    ec = {}
    title_row(ws, "ЭКОНОМИКА — входные параметры (жёлтое = вход из ассетов/кода или оценка)")
    note_row(ws, "Цена: (BasePrice + min(дист,кап)×ставка)×сектор×престиж_тира×рейтинг — OrderPricing.ComputePrice; выплата — PayoutService.Pay", 2)
    r = 4
    def sect(name):
        nonlocal r
        put(ws, r, 1, name, bold=True, fill=SUB_FILL, border=False)
        r += 1
    def inp(label, value, note="", fmt=None, key=None):
        nonlocal r
        put(ws, r, 1, label)
        put(ws, r, 2, value, fmt=fmt, fill=IN_FILL)
        if note: put(ws, r, 3, note, border=False)
        if key: ec[key] = f"{E}!$B${r}"
        r += 1
        return r - 1
    def calc(label, formula, note="", fmt=None, key=None):
        nonlocal r
        put(ws, r, 1, label)
        put(ws, r, 2, formula, fmt=fmt)
        if note: put(ws, r, 3, note, border=False)
        if key: ec[key] = f"{E}!$B${r}"
        r += 1

    sect("1. Цена заказа (PricingConfig_Default)")
    inp("BasePrice, кр", PR["base_price"], key="base")
    inp("PricePerDistanceUnit, кр/юн", PR["price_per_unit"], key="rate", fmt="0.00")
    inp("DistanceCap, юн", PR["dist_cap"], key="cap")

    sect("2. Множители сектора назначения + средний вес (оценка смеси заказов)")
    first_mul = r
    for name, mul, w in zip(["Hub", "Near", "Mid1", "Mid2", "Far"], PR["sector_muls"], PR["sector_weights"]):
        put(ws, r, 1, f"Сектор {name}")
        put(ws, r, 2, mul, fill=IN_FILL, fmt="0.00")
        put(ws, r, 3, "вес в смеси заказов (оценка):", border=False)
        put(ws, r, 4, w, fill=IN_FILL, fmt="0.00")
        r += 1
    last_mul = r - 1
    put(ws, r, 1, "Средний множитель сектора назначения")
    put(ws, r, 2, f"=SUMPRODUCT($B${first_mul}:$B${last_mul},$D${first_mul}:$D${last_mul})", fmt="0.000")
    ec["avg_sector"] = f"{E}!$B${r}"
    r += 2

    sect("3. Премиальность ТИРА корабля TierPrestigeMul (В КОДЕ НЕТ — добавить в PricingConfig)")
    first_pr = r
    for t, p in enumerate(EST["tier_prestige"], start=1):
        put(ws, r, 1, f"Тир {t}")
        put(ws, r, 2, p, fill=IN_FILL, fmt="0.00")
        if t == 1:
            put(ws, r, 3, "предложение брифа; в OrderPricing механики нет — вносить как изменение механики", border=False)
        r += 1
    ec["pr_first"], ec["pr_last"] = first_pr, r - 1
    r += 1

    sect("4. Рейтинг (старт 3.0, +0.15 доставка, −0.10 столкновение — RatingService)")
    inp("Порог бонуса цены", PR["rating_th"], key="r_th", fmt="0.0")
    inp("Множитель цены при рейтинге >= порога", PR["rating_mul"], key="r_mul", fmt="0.00")
    inp("Ожидаемый множитель рейтинга (оценка)", EST["rating_expected"], "стационар: игрок держит рейтинг >= 4.0", key="r_exp", fmt="0.00")
    inp("Порог редкого спавна", PR["low_rating_th"], key="r_low", fmt="0.0")
    inp("Замедление спавна при низком рейтинге", PR["low_rating_slow"], key="r_slow", fmt="0.00")

    sect("5. Чаевые (OrderService: Random 10–25% от цены, бросок при спавне)")
    r_tmin = inp("Чаевые, минимум (доля цены)", EST["tips_min"], "из кода", key="tips_min", fmt="0.00")
    inp("Чаевые, максимум (доля цены)", EST["tips_max"], "из кода", key="tips_max", fmt="0.00")
    calc("Средние чаевые (доля цены)", f"=($B${r_tmin}+$B${r-1})/2", key="tips_avg", fmt="0.000")
    inp("Множитель чаевых Меридиана (перк)", PR["meridian_tips"], "PayoutService", key="tips_mer", fmt="0.00")

    sect("6. Задания пассажиров (PassengerTasks)")
    inp("Шанс задания у заказа", EST["task_chance"], "50% — из кода", key="task_ch", fmt="0.00")
    inp("Бонус выполнения (+рейтинг)", 0.10, "не денежный: +0.10 рейтинга; деньги — только через порог 4.0", key="task_bonus", fmt="0.00")

    sect("7. Штрафы за столкновения (PayoutService)")
    inp("Штраф за столкновение, % цены", PR["penalty_pct"], key="pen", fmt="0")
    inp("Кап штрафа, % цены", PR["penalty_cap"], key="pen_cap", fmt="0")
    inp("Столкновений за заказ (оценка)", EST["collisions_per_order"], key="coll", fmt="0.0")

    sect("8. Аренда")
    inp("Комиссия аренды, % от (цена−штраф)", PR["rental_comm"], key="comm", fmt="0")

    sect("9. Топливо (PhysicsConfig + FuelNetwork; TestLevel01: FuelTier не задан → сеть тира 2)")
    inp("FuelDrainPerSecond, ед/с", PH["fuel_drain"], key="drain", fmt="0.0")
    fp_first = r
    for i, p in enumerate(PR["fuel_price"], start=1):
        put(ws, r, 1, f"Цена топлива, кр/ед — сеть тира {i}")
        put(ws, r, 2, p, fill=IN_FILL, fmt="0.00")
        r += 1
    fp_last = r - 1
    r_tier = inp("Типичный тир сети заправки (оценка)", EST["fuel_tier_typ"], key="fuel_tier", fmt="0")
    calc("Цена топлива, кр/ед (типичная сеть)", f"=INDEX($B${fp_first}:$B${fp_last},$B${r_tier})", key="fuel_p", fmt="0.00")
    for i, f_ in enumerate(PR["fuel_stock"], start=1):
        put(ws, r, 1, f"Запас сети за визит, доля бака — тир {i}")
        put(ws, r, 2, f_, fill=IN_FILL, fmt="0.00")
        r += 1
    inp("Время визита дозаправки, с (оценка)", EST["refuel_visit"], key="refuel", fmt="0")

    sect("10. Ремонт (RepairNetwork: 1 кр/HP × множитель пакета)")
    inp("Базовая цена ремонта, кр/HP", 1.0, "константа кода RepairNetwork.CostOf", key="rep_base", fmt="0.00")
    inp("Средний множитель пакета (оценка)", 1.0, "пакеты 0.8/1.0/1.2", key="rep_mul", fmt="0.00")
    inp("Средний урон за заказ, HP (оценка)", EST["avg_damage_per_order"], "ОЦЕНКА-вход: мягкие касания + 1 удар", key="dmg", fmt="0")

    sect("11. Долг (DebtService.cs)")
    inp("Долг за разбитый арендный, кр", DEBT_ACCIDENT, "AccidentDebt", key="debt", fmt="0")
    inp("Доля выплаты в долг, %", PR["debt_share"], key="debt_share", fmt="0")
    inp("Увольнение после аварий", DEBT_DISMISSAL, "DismissalAfterAccidents", key="dismiss", fmt="0")

    sect("12. Маршруты (TestLevel01: позиции планет; заказ = случайная пара планет)")
    p_first = r
    for name, x, y in PLANETS:
        put(ws, r, 1, f"Планета «{name}» x")
        put(ws, r, 2, x, fill=IN_FILL, fmt="0")
        put(ws, r, 3, "y:", border=False)
        put(ws, r, 4, y, fill=IN_FILL, fmt="0")
        r += 1
    pair_first = r
    for i in range(len(PLANETS)):
        for j in range(i + 1, len(PLANETS)):
            ri = p_first + i; rj = p_first + j
            put(ws, r, 1, f"{PLANETS[i][0]} ↔ {PLANETS[j][0]}")
            put(ws, r, 2, f"=MIN(SQRT(($B${ri}-$B${rj})^2+($D${ri}-$D${rj})^2),{ec['cap']})", fmt="0")
            r += 1
    pair_last = r - 1
    put(ws, r, 1, "Средняя платная дистанция заказа, юн")
    put(ws, r, 2, f"=AVERAGE($B${pair_first}:$B${pair_last})", fmt="0")
    ec["avgdist"] = f"{E}!$B${r}"
    r += 1
    inp("Холостое плечо (подлёт к пассажиру), доля маршрута (оценка)", EST["deadhead"], key="dead", fmt="0.00")
    put(ws, r, 1, "Полёт за заказ (платный + холостой), юн")
    put(ws, r, 2, f"={ec['avgdist']}*(1+{ec['dead']})", fmt="0")
    ec["flight"] = f"{E}!$B${r}"
    r += 1

    sect("13. Время")
    inp("Средняя скорость, доля MaxV (оценка)", EST["avg_speed_frac"], "константа-вход методики", key="avgfrac", fmt="0.00")
    inp("Манёвры/меню за заказ, с (оценка)", EST["maneuver_time"], key="maneuver", fmt="0")
    inp("Оверхед покупки корабля, с (оценка)", EST["purchase_overhead"], key="pur_over", fmt="0")
    inp("Часы борткомпа: реальных секунд за игровую минуту", CLOCK_SEC_PER_GAME_MIN, "OnboardBinder.cs", key="clock", fmt="0")

    sect("14. Полёт и крушение (PhysicsConfig_Default, LandingSystem)")
    inp("LandSpeed, юн/с", PH["land_speed"], key="ls", fmt="0")
    inp("TakeoffV, юн/с", PH["takeoff_v"], key="tov", fmt="0")
    inp("EngineMultiplier (посадочное кольцо)", PH["engine_mul"], key="eng_mul", fmt="0.0")
    inp("GravityBoost (качели)", PH["grav_boost"], key="grav", fmt="0.00")
    inp("Retro глобальный (если у корабля не задан)", PH["retro"], key="retro_g", fmt="0")
    inp("Урон от удара: к × (v−LandSpeed)²", PH["crash_k"], "LandingSystem.CrashDamagePerSpeedSq", key="crashk", fmt="0.000")

    sect("15. Сценарии")
    inp("Стартовый кошелёк, кр", EST["start_wallet"], key="wallet", fmt="0")

    widths(ws, {"A": 48, "B": 12, "C": 44, "D": 10})
    return ec

# ============================ ЛИСТ УЛУЧШЕНИЯ ================================
def build_upgrades(ws, ec):
    title_row(ws, "УЛУЧШЕНИЯ — линии Engine/Tank/Hull/Maneuver (UpgradeConfig_Default; ShipStats: эффект линейно уровень/3)")
    note_row(ws, "MaxV улучшениями не меняется. Δдоход — через сэкономленное время (двигатель/бак) и снижение аварий (корпус/манёвр); оценки-входы жёлтым", 2)
    r = 4
    put(ws, r, 1, "Конфиг (жёлтое = вход)", bold=True, fill=SUB_FILL, border=False); r += 1
    pc = {}
    def inp(label, value, key, note="", fmt="0"):
        nonlocal r
        put(ws, r, 1, label)
        put(ws, r, 2, value, fill=IN_FILL, fmt=fmt)
        if note: put(ws, r, 3, note, border=False)
        pc[key] = f"{U}!$B${r}"
        r += 1
    inp("Цена уровня 1, кр", UP_PRICES[0], "p1")
    inp("Цена уровня 2, кр", UP_PRICES[1], "p2")
    inp("Цена уровня 3, кр", UP_PRICES[2], "p3")
    inp("Engine: +тяга % на макс", UP_PCT["engine_thrust"], "eng")
    inp("Engine: буст качелей до", UP_PCT["engine_boost_max"], "boost", fmt="0.0")
    inp("Tank: +бак % на макс", UP_PCT["tank_cap"], "tank")
    inp("Tank: −расход % на макс", UP_PCT["tank_drain_red"], "drain_red")
    inp("Hull: +HP % на макс", UP_PCT["hull_hp"], "hp")
    inp("Hull: −жар % на макс", UP_PCT["hull_heat_red"], "heat")
    inp("Maneuver: +Strafe % на макс", UP_PCT["maneuver_strafe"], "strafe")
    inp("Корабль-пример (Id)", "delta", "ship", "для расчёта метрик улучшений")
    inp("Разгонов за заказ (оценка)", 2.0, "k_acc", "разгон после старта и после посадки", fmt="0.0")
    inp("Смертей за заказ без корпуса (оценка)", 0.03, "death", fmt="0.00")
    inp("Стоимость смерти, кр (оценка)", 500.0, "death_cost", "потерянный заказ + разбитый корпус", fmt="0")
    inp("Снижение столкновений Maneuver на макс (оценка)", 0.30, "man_red", fmt="0.00")
    inp("Снижение аварий Hull на макс (оценка)", 0.30, "hull_red", fmt="0.00")
    inp("Урон за одно столкновение, HP (оценка)", 20.0, "dmg_col", fmt="0")

    # ссылки на статы корабля-примера через INDEX/MATCH по Id
    m = f"MATCH({pc['ship']},{K}!$A$4:$A$12,0)"
    sr = lambda col: f"INDEX({K}!${col}$4:${col}$12,{m})"
    r += 1
    hdr = ["Линия", "Уровень", "Доля (ур/3)", "Цена уровня, кр", "Стат на уровне", "Δвремя заказа, с",
           "Δдоход/заказ, кр", "Окупаемость, заказов"]
    for i, h in enumerate(hdr, start=1):
        put(ws, r, i, h, bold=True, fill=HDR_FILL, align="center", wrap=True)
    r += 1
    first_data = r
    for line in ["Двигатель", "Бак", "Корпус", "Манёвровые"]:
        for lvl in (1, 2, 3):
            put(ws, r, 1, line, fill=IN_FILL if lvl == 1 else None)
            put(ws, r, 2, lvl, fill=IN_FILL, align="center")
            put(ws, r, 3, f"=B{r}/3", fmt="0.000")
            put(ws, r, 4, f"=CHOOSE(B{r},{pc['p1']},{pc['p2']},{pc['p3']})", fmt="0")
            if line == "Двигатель":
                put(ws, r, 5, f"={sr('H')}*(1+$B${pc['eng']}/100*C{r})", fmt="0.0")
                f_time = f"=({sr('H')}/{sr('F')}-{sr('H')}/E{r})*$B${pc['k_acc']}"
                f_income = f"=({sr('X')}/{sr('AC')})*F{r}"
            elif line == "Бак":
                put(ws, r, 5, f"={sr('I')}*(1+$B${pc['tank']}/100*C{r})", fmt="0.0")
                q_base = f"({sr('I')}/{ec['drain']}*{sr('H')}*{ec['avgfrac']}/{ec['flight']})"
                q_new = f"(E{r}/({ec['drain']}*(1-$B${pc['drain_red']}/100*C{r}))*{sr('H')}*{ec['avgfrac']}/{ec['flight']})"
                f_time = f"=(1/{q_base}-1/{q_new})*{ec['refuel']}"
                f_income = f"=({sr('X')}/{sr('AC')})*F{r}"
            elif line == "Корпус":
                put(ws, r, 5, f"={sr('J')}*(1+$B${pc['hp']}/100*C{r})", fmt="0.0")
                f_time = "=0"
                f_income = f"=$B${pc['death']}*$B${pc['death_cost']}*$B${pc['hull_red']}*C{r}"
            else:
                put(ws, r, 5, f"={sr('G')}*(1+$B${pc['strafe']}/100*C{r})", fmt="0.0")
                f_time = "=0"
                f_income = f"={ec['coll']}*$B${pc['man_red']}*C{r}*({ec['pen']}/100*{sr('R')}+$B${pc['dmg_col']}*{ec['rep_base']})"
            put(ws, r, 6, f_time, fmt="0.0")
            put(ws, r, 7, f_income, fmt="0.0")
            put(ws, r, 8, f'=IF(G{r}<=0,"не окуп.",ROUND(D{r}/G{r},1))', fmt="0.0")
            r += 1
    last_data = r - 1
    for i in range(4):
        rr = first_data + i * 3
        put(ws, rr, 9, f"=SUM(D{rr}:D{rr+2})", bold=True, fmt="0")
        put(ws, rr, 10, f"=SUM(G{rr}:G{rr+2})", bold=True, fmt="0.0")
        put(ws, rr, 11, f'=IF(J{rr}<=0,"не окуп.",ROUND(I{rr}/J{rr},1))', bold=True, fmt="0.0")
    put(ws, first_data - 1, 9, "итого линия:", bold=True, fill=HDR_FILL, align="center")
    r += 1
    put(ws, r, 1, "Всё улучшить (4 линии × 3 уровня)", bold=True)
    put(ws, r, 2, f"=4*({pc['p1']}+{pc['p2']}+{pc['p3']})", bold=True, fmt="0")
    up_all = f"{U}!$B${r}"; r += 1
    put(ws, r, 1, "Пара улучшений (Бак ур.1 + Двигатель ур.1)")
    put(ws, r, 2, f"={pc['p1']}*2", fmt="0")
    up_pair = f"{U}!$B${r}"; r += 1
    put(ws, r, 1, "Улучшения: одна полная линия")
    put(ws, r, 2, f"={pc['p1']}+{pc['p2']}+{pc['p3']}", fmt="0")
    up_line = f"{U}!$B${r}"
    rule3(ws, f"H{first_data}:H{last_data}",
          f"AND(ISNUMBER($H{first_data}),$H{first_data}>=6,$H{first_data}<=10)",
          f'AND(ISNUMBER($H{first_data}),OR(AND($H{first_data}>=4,$H{first_data}<6),AND($H{first_data}>10,$H{first_data}<=14)))',
          f'OR($H{first_data}="не окуп.",AND(ISNUMBER($H{first_data}),OR($H{first_data}<4,$H{first_data}>14)))')
    widths(ws, {"A": 30, "B": 12, "C": 11, "D": 14, "E": 13, "F": 14, "G": 14, "H": 14,
                "I": 12, "J": 13, "K": 13})
    return dict(pair=up_pair, line=up_line, all=up_all, hp=pc["hp"], first=first_data)

# ============================ ЛИСТ КОРАБЛИ ==================================
def build_ships(ws, ec, up):
    title_row(ws, "КОРАБЛИ — сток из ассетов (жёлтое = вход) и расчёт (формулы)")
    note_row(ws, "Доход = карточка + чаевые − штраф − комиссия(аренда) − топливо − ремонт (PayoutService.Pay). Окупаемость — к лучшему кораблю предыдущего тира", 2)
    hdr = ["Id", "Имя", "Тир", "Цена, кр", "Аренда", "Thrust", "StrafeAcc", "MaxV", "FuelMax", "HpMax",
           "Retro", "VisualLength", "Разгон до MaxV, с", "Тормозной путь, юн", "Ср. скорость, юн/с",
           "Запас хода, юн", "Заказов на бак", "Цена карточки, кр", "Чаевые, кр", "Штраф, кр",
           "Комиссия, кр", "Топливо, кр", "Ремонт, кр", "Чистый доход/заказ, кр", "Выживаемость, заказов",
           "Урон на MaxV, HP", "Смерть на стоке", "Смерть с макс. корпусом", "Время заказа, с",
           "Δцена к пред. тиру", "Δдоход к пред. тиру", "Окупаемость перехода, заказов",
           "Бак-вердикт", "Торможение-вердикт", "Смерть-вердикт"]
    for i, h in enumerate(hdr, start=1):
        put(ws, 3, i, h, bold=True, fill=HDR_FILL, align="center", wrap=True)
    first = 4
    for idx, sh in enumerate(SHIPS):
        r = first + idx
        sid, name, tier, price, rental, thrust, strafe, maxv, fuelmax, hpmax, retro, vlen = sh
        for col, v in zip(range(1, 13), sh):
            put(ws, r, col, v, fill=IN_FILL,
                fmt=None if isinstance(v, str) else "0",
                align="left" if isinstance(v, str) else "center")
        f = {
            13: f"=H{r}/F{r}",
            14: f"=(H{r}^2-{ec['ls']}^2)/(2*K{r})",
            15: f"=H{r}*{ec['avgfrac']}",
            16: f"=I{r}/{ec['drain']}*O{r}",
            17: f"=P{r}/{ec['flight']}",
            18: f"=ROUND(({ec['base']}+MIN({ec['avgdist']},{ec['cap']})*{ec['rate']})*{ec['avg_sector']}"
                f"*INDEX({E}!$B${ec['pr_first']}:$B${ec['pr_last']},C{r})*{ec['r_exp']},0)",
            19: f"=ROUND(R{r}*{ec['tips_avg']},0)",
            20: f"=ROUND(MIN({ec['coll']}*{ec['pen']},{ec['pen_cap']})/100*R{r},0)",
            21: f"=IF(E{r}=1,ROUND((R{r}-T{r})*{ec['comm']}/100,0),0)",
            22: f"=ROUND({ec['flight']}/O{r}*{ec['drain']}*{ec['fuel_p']},0)",
            23: f"=ROUND({ec['dmg']}*{ec['rep_mul']},0)",
            24: f"=R{r}+S{r}-T{r}-U{r}-V{r}-W{r}",
            25: f"=J{r}/{ec['dmg']}",
            26: f"={ec['crashk']}*(H{r}-{ec['ls']})^2",
            27: f'=IF(Z{r}>=J{r},"да","НЕТ")',
            28: f'=IF(Z{r}>=J{r}*(1+{up["hp"]}),"да","НЕТ")',
            29: f"={ec['flight']}/O{r}+{ec['maneuver']}",
            30: f'=IF(C{r}=1,"",MINIFS($D$4:$D$12,$C$4:$C$12,C{r}-1,$E$4:$E$12,0))',
            31: f'=IF(C{r}=1,"",INDEX($X$4:$X$12,MATCH(AD{r},$D$4:$D$12,0)))',
            32: f'=IF(OR(C{r}=1,E{r}=1),"—",IF(X{r}-AE{r}<=0,"не окуп.",ROUND((D{r}-AD{r})/(X{r}-AE{r}),1)))',
            33: (f'=IF(C{r}<=2,IF(AND(Q{r}>={TARGET_TANK_WEAK[0]},Q{r}<={TARGET_TANK_WEAK[1]}),"норм",'
                 f'IF(AND(Q{r}>=1.2,Q{r}<=2.5),"граница","проблема")),'
                 f'IF(C{r}<=4,IF(AND(Q{r}>={TARGET_TANK_MID[0]},Q{r}<={TARGET_TANK_MID[1]}),"норм",'
                 f'IF(AND(Q{r}>=2,Q{r}<=7),"граница","проблема")),'
                 f'IF(Q{r}>={TARGET_TANK_BARIN},"норм",IF(Q{r}>=4,"граница","проблема"))))'),
            34: (f'=IF(C{r}=1,IF(N{r}<={TARGET_BRAKE},"норм",IF(N{r}<=800,"граница","проблема")),'
                 f'IF(N{r}<INDEX($N$4:$N$12,MATCH(AD{r},$D$4:$D$12,0)),"норм","проблема"))'),
            35: f'=IF(AB{r}="да","норм",IF(AA{r}="да","граница","проблема"))',
        }
        for col, formula in f.items():
            fmt = "0.00" if col in (13, 17, 25, 26, 29, 32) else ("0.0" if col in (14, 15, 16) else "0")
            put(ws, r, col, formula, fmt=fmt)
    last = first + len(SHIPS) - 1
    rule3(ws, f"AF{first}:AF{last}",
          f"AND(ISNUMBER($AF{first}),$AF{first}>={TARGET_PAYBACK[0]},$AF{first}<={TARGET_PAYBACK[1]})",
          f'AND(ISNUMBER($AF{first}),OR(AND($AF{first}>=4,$AF{first}<{TARGET_PAYBACK[0]}),AND($AF{first}>{TARGET_PAYBACK[1]},$AF{first}<=14)))',
          f'OR($AF{first}="не окуп.",AND(ISNUMBER($AF{first}),OR($AF{first}<4,$AF{first}>14)))')
    rule3(ws, f"Q{first}:Q{last}",
          f"OR(AND($C{first}<=2,$Q{first}>={TARGET_TANK_WEAK[0]},$Q{first}<={TARGET_TANK_WEAK[1]}),"
          f"AND($C{first}>=3,$C{first}<=4,$Q{first}>={TARGET_TANK_MID[0]},$Q{first}<={TARGET_TANK_MID[1]}),"
          f"AND($C{first}=5,$Q{first}>={TARGET_TANK_BARIN}))",
          f"OR(AND($C{first}<=2,$Q{first}>1,$Q{first}<1.5),AND($C{first}<=2,$Q{first}>2,$Q{first}<=2.5),"
          f"AND($C{first}>=3,$C{first}<=4,$Q{first}>=2,$Q{first}<3),AND($C{first}>=3,$C{first}<=4,$Q{first}>5,$Q{first}<=7),"
          f"AND($C{first}=5,$Q{first}>=4,$Q{first}<5))",
          "TRUE")
    for col in ("AG", "AH", "AI"):
        rule3(ws, f"{col}{first}:{col}{last}",
              f'${col}{first}="норм"', f'${col}{first}="граница"', f'${col}{first}="проблема"')
    rule3(ws, f"AA{first}:AA{last}", f'$AA{first}="да"', "FALSE", f'$AA{first}="НЕТ"')
    rule3(ws, f"AB{first}:AB{last}", f'$AB{first}="да"', "FALSE", f'$AB{first}="НЕТ"')
    widths(ws, {"A": 11, "B": 14})
    for i in range(3, len(hdr) + 1):
        ws.column_dimensions[get_column_letter(i)].width = 11
    ws.freeze_panes = "C4"
    ws.row_dimensions[3].height = 42
    return first, last

# ============================ ЛИСТ СЦЕНАРИИ =================================
UPG_PREFIX = "УЛУЧШ:"
def build_scenarios(ws, ec, up):
    title_row(ws, "СЦЕНАРИИ — 4 стратегии: время = заказы×время заказа + оверхед покупок")
    note_row(ws, "Жёлтое = вход (маршрут покупок). Цены/доходы/времена заказа — формулы от КОРАБЛИ; оверхед — ЭКОНОМИКА (оценки)", 2)
    r = 4
    blocks = {}
    def block(key, caption, steps, final_note):
        nonlocal r
        put(ws, r, 1, caption, bold=True, fill=SUB_FILL, border=False); r += 1
        hdr = ["Шаг", "Покупка (вход)", "Цена шага, кр", "Летим на (вход)", "Доход/заказ, кр",
               "Время заказа, с", "Заказов", "Время этапа, с"]
        for i, h in enumerate(hdr, start=1):
            put(ws, r, i, h, bold=True, fill=HDR_FILL, align="center", wrap=True)
        r += 1
        first = r
        for i, (buy, fly) in enumerate(steps, start=1):
            put(ws, r, 1, i, align="center")
            put(ws, r, 2, buy, fill=IN_FILL)
            if buy.startswith(UPG_PREFIX):
                ref = up["all"] if "4 линии" in buy else (up["pair"] if "пара" in buy else up["line"])
                put(ws, r, 3, f"={ref}", fmt="0")
            else:
                put(ws, r, 3, f"=INDEX({K}!$D$4:$D$12,MATCH($B${r},{K}!$A$4:$A$12,0))", fmt="0")
            put(ws, r, 4, fly, fill=IN_FILL)
            put(ws, r, 5, f"=INDEX({K}!$X$4:$X$12,MATCH(D{r},{K}!$A$4:$A$12,0))", fmt="0")
            put(ws, r, 6, f"=INDEX({K}!$AC$4:$AC$12,MATCH(D{r},{K}!$A$4:$A$12,0))", fmt="0.0")
            put(ws, r, 7, f"=IF(E{r}>0,ROUND(C{r}/E{r},1),0)", fmt="0.0")
            put(ws, r, 8, f"=ROUND(G{r}*F{r}+{ec['pur_over']},0)", fmt="0")
            r += 1
        last = r - 1
        put(ws, r, 1, "Итого", bold=True)
        put(ws, r, 7, f"=SUM(G{first}:G{last})", bold=True, fmt="0.0")
        put(ws, r, 8, f"=SUM(H{first}:H{last})", bold=True, fmt="0")
        tot = r; r += 1
        put(ws, r, 1, "Время, мин (реальные)")
        put(ws, r, 8, f"=ROUND(H{tot}/60,1)", bold=True, fmt="0.0")
        tmin = f"$H${r}"; r += 1
        put(ws, r, 1, "Игровые часы (борткомп: 3 с = 1 мин)")
        put(ws, r, 8, f"=H{tot}/{ec['clock']}/60", fmt="0.0")
        r += 1
        put(ws, r, 1, "Финальный корабль")
        put(ws, r, 4, f"=D{last}", bold=True)
        fin = f"$D${r}"; r += 1
        note_row(ws, final_note, r); r += 2
        blocks[key] = dict(tot=tot, tmin=tmin, fin=fin)
    block("a", "Сценарий (а) — «всё купить + всё улучшить»",
          [("skrepka", "egg_rental"), ("egg_own", "skrepka"), ("strela", "egg_own"), ("kaban", "strela"),
           ("delta", "kaban"), ("igla", "delta"), ("meridian", "igla"), ("barin", "meridian"),
           (UPG_PREFIX + "улучшения: все 4 линии ×3 ур.", "barin")],
          "Полная коллекция + максимальная прокачка всех линий.")
    block("b", "Сценарий (б) — «копить на Барина на аренде» (антипаттерн)",
          [("barin", "egg_rental")],
          "Весь путь на арендном Яйце: комиссия 25%, медленный корабль, без улучшений.")
    block("v", "Сценарий (в) — «все корабли без улучшений»",
          [("skrepka", "egg_rental"), ("egg_own", "skrepka"), ("strela", "egg_own"), ("kaban", "strela"),
           ("delta", "kaban"), ("igla", "delta"), ("meridian", "igla"), ("barin", "meridian")],
          "Коллекция без прокачки.")
    block("g", "Сценарий (г) — «4-5 кораблей + пара улучшений» (основная прогрессия)",
          [("skrepka", "egg_rental"), ("egg_own", "skrepka"), ("strela", "egg_own"), ("delta", "strela"),
           ("igla", "delta"), (UPG_PREFIX + "улучшения: пара (Бак ур.1 + Двигатель ур.1)", "igla")],
          "Основная ветка прогрессии: 5 кораблей + 2 дешёвых уровня улучшений.")
    rule3(ws, blocks["g"]["tmin"],
          f"AND({blocks['g']['tmin']}>={TARGET_PASS_MIN},{blocks['g']['tmin']}<={TARGET_PASS_MAX})",
          f"AND({blocks['g']['tmin']}>={TARGET_PASS_MIN}-5,{blocks['g']['tmin']}<={TARGET_PASS_MAX}+10)",
          "TRUE")
    widths(ws, {"A": 8, "B": 36, "C": 14, "D": 14, "E": 14, "F": 13, "G": 10, "H": 13})
    return blocks

# ============================ ЛИСТ СВОДКА ===================================
def build_summary(ws, ship_rows, blocks):
    skrepka, rental, egg_own, strela, kaban, delta, igla, meridian, barin = ship_rows
    title_row(ws, "СВОДКА — целевые ориентиры и вердикты инвариантов (значения — формулы от других листов)")
    r = 3
    put(ws, r, 1, "Цели владельца (жёлтое = вход)", bold=True, fill=SUB_FILL, border=False); r += 1
    tg = {}
    def target(label, value, key, fmt="0"):
        nonlocal r
        put(ws, r, 1, label)
        put(ws, r, 2, value, fill=IN_FILL, fmt=fmt)
        tg[key] = f"$B${r}"
        r += 1
    target("Время прохождения, мин — от", TARGET_PASS_MIN, "pmin")
    target("Время прохождения, мин — до", TARGET_PASS_MAX, "pmax")
    target("Переход тира окупается, заказов — от", TARGET_PAYBACK[0], "paymin")
    target("Переход тира окупается, заказов — до", TARGET_PAYBACK[1], "paymax")
    target("Бак слабого (тиры 1-2), заказов — от", TARGET_TANK_WEAK[0], "tw_min", fmt="0.0")
    target("Бак слабого — до", TARGET_TANK_WEAK[1], "tw_max", fmt="0.0")
    target("Бак среднего (тиры 3-4) — от", TARGET_TANK_MID[0], "tm_min", fmt="0.0")
    target("Бак среднего — до", TARGET_TANK_MID[1], "tm_max", fmt="0.0")
    target("Бак Барина, заказов — не менее", TARGET_TANK_BARIN, "tb", fmt="0.0")
    target("Тормозной путь, юн — не более", TARGET_BRAKE, "brake")
    r += 1

    put(ws, r, 1, "Переходы тиров (окупаемость, заказов) — из КОРАБЛИ", bold=True, fill=SUB_FILL, border=False); r += 1
    pay_first = r
    for label, row in [("Тир 1 → 2 (Яйцо)", egg_own), ("Тир 2 → 3 (Стрела)", strela),
                       ("Тир 3 → 4 (Игла)", igla), ("Тир 4 → 5 (Барин)", barin)]:
        put(ws, r, 1, label)
        put(ws, r, 2, f"={K}!AF{row}", fmt="0.0")
        r += 1
    pay_last = r - 1
    rng = f"$B${pay_first}:$B${pay_last}"
    put(ws, r, 1, "Вердикт: все переходы окупаются в целевом окне")
    put(ws, r, 2, f'=IF(COUNT({rng})<4,"КРАСНЫЙ",IF(AND(MIN({rng})>={tg["paymin"]},MAX({rng})<={tg["paymax"]}),"ЗЕЛЁНЫЙ",'
                  f'IF(AND(MIN({rng})>=4,MAX({rng})<=14),"ЖЁЛТЫЙ","КРАСНЫЙ")))', bold=True)
    inv_cells_late = [f"$B${r}"]
    r += 2

    put(ws, r, 1, "Сценарии (реальное время, мин / заказов / финал)", bold=True, fill=SUB_FILL, border=False); r += 1
    sc_first = r
    for label, key in [("а) всё купить + всё улучшить", "a"), ("б) копить на Барина на аренде", "b"),
                       ("в) все корабли без улучшений", "v"), ("г) 5 кораблей + пара улучшений", "g")]:
        put(ws, r, 1, label)
        put(ws, r, 2, f"={S}!{blocks[key]['tmin']}", fmt="0.0")
        put(ws, r, 3, f"={S}!$G${blocks[key]['tot']}", fmt="0.0")
        put(ws, r, 4, f"={S}!{blocks[key]['fin']}")
        r += 1
    sc = {key: f"$B${sc_first + i}" for i, key in enumerate(["a", "b", "v", "g"])}
    put(ws, r, 1, "Вердикт: (г) в целевом окне 30-35 мин")
    put(ws, r, 2, f'=IF(AND({sc["g"]}>={tg["pmin"]},{sc["g"]}<={tg["pmax"]}),"ЗЕЛЁНЫЙ",'
                  f'IF(AND({sc["g"]}>={tg["pmin"]}-5,{sc["g"]}<={tg["pmax"]}+10),"ЖЁЛТЫЙ","КРАСНЫЙ"))', bold=True)
    inv_cells_late.append(f"$B${r}"); r += 1
    put(ws, r, 1, "Вердикт: (б) хуже (г) — дольше И заказов заметно больше")
    put(ws, r, 2, f'=IF(AND({sc["b"]}>{sc["g"]},$C${sc_first+1}>$C${sc_first+3}*1.1),"ЗЕЛЁНЫЙ",'
                  f'IF({sc["b"]}>{sc["g"]},"ЖЁЛТЫЙ","КРАСНЫЙ"))', bold=True)
    inv_cells_late.append(f"$B${r}"); r += 2

    put(ws, r, 1, "Инварианты", bold=True, fill=SUB_FILL, border=False); r += 1
    inv_cells = list(inv_cells_late)
    def verdict_row(label, formula, value_formula=None, fmt="0.0"):
        nonlocal r
        put(ws, r, 1, label)
        if value_formula: put(ws, r, 2, value_formula, fmt=fmt)
        put(ws, r, 3, formula, bold=True)
        inv_cells.append(f"$C${r}")
        r += 1
    put(ws, r, 1, "Аренда-Яйцо: доход > 0 и худший в тире 2")
    put(ws, r, 2, f"={K}!X{rental}", fmt="0")
    put(ws, r, 3, f'=IF(AND({K}!X{rental}>0,{K}!X{rental}<{K}!X{egg_own}),"ЗЕЛЁНЫЙ",IF({K}!X{rental}>0,"ЖЁЛТЫЙ","КРАСНЫЙ"))', bold=True)
    inv_cells.append(f"$C${r}"); r += 1
    verdict_row("Бак: слабый 1.5-2, средний 3-5, Барин 5+",
                f'=IF(COUNTIF({K}!$AG$4:$AG$12,"проблема")>0,"КРАСНЫЙ",IF(COUNTIF({K}!$AG$4:$AG$12,"граница")>0,"ЖЁЛТЫЙ","ЗЕЛЁНЫЙ"))')
    verdict_row("Тормозной путь: сильный короче слабого, цель <= 450",
                f'=IF(COUNTIF({K}!$AH$4:$AH$12,"проблема")>0,"КРАСНЫЙ",IF(COUNTIF({K}!$AH$4:$AH$12,"граница")>0,"ЖЁЛТЫЙ","ЗЕЛЁНЫЙ"))')
    verdict_row("Смерть от врезания на MaxV гарантирована (вкл. макс. корпус)",
                f'=IF(COUNTIF({K}!$AI$4:$AI$12,"проблема")>0,"КРАСНЫЙ",IF(COUNTIF({K}!$AI$4:$AI$12,"граница")>0,"ЖЁЛТЫЙ","ЗЕЛЁНЫЙ"))')
    r += 1

    put(ws, r, 1, "Главные выводы (детали — отчёт ночного прогона)", bold=True, fill=SUB_FILL, border=False); r += 1
    for txt in [
        "Переходы тиров не окупаются: множителя премиальности тира в коде НЕТ — добавить TierPrestigeMul в PricingConfig.",
        "Тормозной путь растёт с тиром (MaxV растёт быстрее Retro) — нужен ретро-тюнинг Retro по кораблям.",
        "Кабан с макс. корпусом переживает полное врезание — либо CrashDamagePerSpeedSq, либо HullHpPct.",
        "Сценарий (б) конкурентоспособен: поднять комиссию аренды/цену Барина, чтобы антипаттерн был заведомо хуже.",
    ]:
        ws.cell(row=r, column=1, value=txt).font = Font(italic=True, size=10, color="7F3F00")
        r += 1

    for cell in inv_cells:
        ref = cell.replace("$", "")
        rule3(ws, ref, f'{cell}="ЗЕЛЁНЫЙ"', f'{cell}="ЖЁЛТЫЙ"', f'{cell}="КРАСНЫЙ"')
    rule3(ws, f"B{pay_first}:B{pay_last}",
          f"AND(ISNUMBER($B{pay_first}),$B{pay_first}>={tg['paymin']},$B{pay_first}<={tg['paymax']})",
          f"AND(ISNUMBER($B{pay_first}),OR(AND($B{pay_first}>=4,$B{pay_first}<{tg['paymin']}),AND($B{pay_first}>{tg['paymax']},$B{pay_first}<=14)))",
          "TRUE")
    widths(ws, {"A": 56, "B": 14, "C": 14, "D": 16})

# ============================ КАЛЬКУЛЯЦИЯ (проверка в python) ===============
def verify():
    """Та же модель в python: метрики + вердикты инвариантов при текущих входах."""
    prestige = EST["tier_prestige"]
    sector = sum(m * w for m, w in zip(PR["sector_muls"], PR["sector_weights"]))
    tips = (EST["tips_min"] + EST["tips_max"]) / 2
    dists = [math.dist((p[1], p[2]), (q[1], q[2])) for i, p in enumerate(PLANETS) for q in PLANETS[i+1:]]
    avgdist = sum(min(x, PR["dist_cap"]) for x in dists) / len(dists)
    flight = avgdist * (1 + EST["deadhead"])
    fuel_p = PR["fuel_price"][EST["fuel_tier_typ"] - 1]
    res = {}
    for sid, name, tier, price, rental, thrust, strafe, maxv, fuelmax, hpmax, retro, vlen in SHIPS:
        avgv = maxv * EST["avg_speed_frac"]
        brake = (maxv ** 2 - PH["land_speed"] ** 2) / (2 * retro)
        rng_ = fuelmax / PH["fuel_drain"] * avgv
        card = round((PR["base_price"] + min(avgdist, PR["dist_cap"]) * PR["price_per_unit"])
                     * sector * prestige[tier - 1] * EST["rating_expected"])
        tips_c = round(card * tips)
        pen = round(min(EST["collisions_per_order"] * PR["penalty_pct"], PR["penalty_cap"]) / 100 * card)
        comm = round((card - pen) * PR["rental_comm"] / 100) if rental else 0
        fuel_c = round(flight / avgv * PH["fuel_drain"] * fuel_p)
        repair = round(EST["avg_damage_per_order"])
        net = card + tips_c - pen - comm - fuel_c - repair
        res[sid] = dict(tier=tier, price=price, rental=rental, net=net, tank=rng_ / flight,
                        brake=brake, crash=PH["crash_k"] * (maxv - PH["land_speed"]) ** 2,
                        hp=hpmax, hp_max=hpmax * (1 + UP_PCT["hull_hp"] / 100),
                        t=flight / avgv + EST["maneuver_time"])
    pay = {}
    for sid in ("egg_own", "strela", "igla", "barin"):
        tier = res[sid]["tier"]
        prev = min((s for s in res.values() if s["tier"] == tier - 1 and not s["rental"]), key=lambda s: s["price"])
        dn = res[sid]["net"] - prev["net"]
        pay[sid] = "не окуп." if dn <= 0 else round((res[sid]["price"] - prev["price"]) / dn, 1)
    def scen(steps):
        orders = time_s = 0.0
        for buy, fly in steps:
            cost = 600 if (buy.startswith("UPG:") and "пара" in buy) else (10000 if buy.startswith("UPG:") else next(s[3] for s in SHIPS if s[0] == buy))
            n = cost / res[fly]["net"]
            orders += n
            time_s += n * res[fly]["t"] + EST["purchase_overhead"]
        return orders, time_s
    steps_full = [("skrepka", "egg_rental"), ("egg_own", "skrepka"), ("strela", "egg_own"), ("kaban", "strela"),
                  ("delta", "kaban"), ("igla", "delta"), ("meridian", "igla"), ("barin", "meridian")]
    sa = scen(steps_full + [("UPG:всё", "barin")])
    sb = scen([("barin", "egg_rental")])
    sv = scen(steps_full)
    sg = scen([("skrepka", "egg_rental"), ("egg_own", "skrepka"), ("strela", "egg_own"), ("delta", "strela"),
               ("igla", "delta"), ("UPG:пара", "igla")])
    def band(v, lo, hi, ylo, yhi):
        return "норм" if lo <= v <= hi else ("граница" if ylo <= v <= yhi else "проблема")
    tank_cls = []
    for sid in res:
        v = res[sid]
        lo, hi = TARGET_TANK_WEAK if v["tier"] <= 2 else TARGET_TANK_MID if v["tier"] <= 4 else (TARGET_TANK_BARIN, 999)
        tank_cls.append(band(v["tank"], lo, hi, lo - 0.3, hi + 2))
    ordered = sorted((s for s in SHIPS if not s[4]), key=lambda s: s[2])
    brake_cls = []
    for i, sh in enumerate(ordered):
        b = res[sh[0]]["brake"]
        if sh[2] == 1:
            brake_cls.append("норм" if b <= TARGET_BRAKE else ("граница" if b <= 800 else "проблема"))
        else:
            prev = min((res[s[0]]["brake"] for s in ordered if s[2] == sh[2] - 1))
            brake_cls.append("норм" if b < prev else "проблема")
    death_cls = ["норм" if res[s]["crash"] >= res[s]["hp_max"]
                 else ("граница" if res[s]["crash"] >= res[s]["hp"] else "проблема") for s in res]
    inv = {}
    nums = [v for v in pay.values() if isinstance(v, float)]
    inv["переходы 6-10 заказов"] = ("КРАСНЫЙ" if len(nums) < 4 else
        "ЗЕЛЁНЫЙ" if min(nums) >= 6 and max(nums) <= 10 else ("ЖЁЛТЫЙ" if min(nums) >= 4 and max(nums) <= 14 else "КРАСНЫЙ"))
    inv["аренда >0 и худшая в тире 2"] = ("ЗЕЛЁНЫЙ" if res["egg_rental"]["net"] > 0 and res["egg_rental"]["net"] < res["egg_own"]["net"]
                                          else ("ЖЁЛТЫЙ" if res["egg_rental"]["net"] > 0 else "КРАСНЫЙ"))
    inv["баки"] = "КРАСНЫЙ" if "проблема" in tank_cls else ("ЖЁЛТЫЙ" if "граница" in tank_cls else "ЗЕЛЁНЫЙ")
    inv["торможение монотонное/<=450"] = "КРАСНЫЙ" if "проблема" in brake_cls else ("ЖЁЛТЫЙ" if "граница" in brake_cls else "ЗЕЛЁНЫЙ")
    tgg = sg[1] / 60
    inv["время (г) 30-35 мин"] = "ЗЕЛЁНЫЙ" if TARGET_PASS_MIN <= tgg <= TARGET_PASS_MAX else ("ЖЁЛТЫЙ" if TARGET_PASS_MIN - 5 <= tgg <= TARGET_PASS_MAX + 10 else "КРАСНЫЙ")
    inv["(б) хуже (г)"] = ("ЗЕЛЁНЫЙ" if sb[0] > sg[0] * 1.1 and sb[1] > sg[1] else ("ЖЁЛТЫЙ" if sb[1] > sg[1] else "КРАСНЫЙ"))
    inv["смерть всем корпусам"] = "КРАСНЫЙ" if "проблема" in death_cls else ("ЖЁЛТЫЙ" if "граница" in death_cls else "ЗЕЛЁНЫЙ")
    # прикидка: престиж тира, дающий окупаемость за ~8 заказов
    margin = 1 + tips - PR["penalty_pct"] / 100 * EST["collisions_per_order"]
    base_card = (PR["base_price"] + min(avgdist, PR["dist_cap"]) * PR["price_per_unit"]) * sector * EST["rating_expected"]
    need = {}
    for sid in ("egg_own", "strela", "igla", "barin"):
        tier = res[sid]["tier"]
        prev = min((s for s in res.values() if s["tier"] == tier - 1 and not s["rental"]), key=lambda s: s["price"])
        maxv = next(s[7] for s in SHIPS if s[0] == sid)
        fuel_c = flight / (maxv * EST["avg_speed_frac"]) * PH["fuel_drain"] * fuel_p
        need_net = prev["net"] + (res[sid]["price"] - prev["price"]) / 8
        need[sid] = round((need_net + fuel_c + round(EST["avg_damage_per_order"])) / margin / base_card, 2)
    print("=== ПРОВЕРКА (python-калькуляция при текущих входах) ===")
    print(f"avgPaidDist={avgdist:.0f} юн; flight/order={flight:.0f} юн; avgSector={sector:.2f}; tips={tips:.3f}; fuelPrice={fuel_p} кр/ед")
    print(f"{'id':10}{'net':>6}{'tank':>7}{'brake':>8}{'crash':>8}{'t/ord':>7}")
    for sid, v in res.items():
        print(f"{sid:10}{v['net']:6.0f}{v['tank']:7.2f}{v['brake']:8.0f}{v['crash']:8.0f}{v['t']:7.1f}")
    print(f"окупаемость переходов (заказов): {pay}")
    print(f"сценарии, мин/заказов: а={sa[1]/60:.1f}/{sa[0]:.0f}  б={sb[1]/60:.1f}/{sb[0]:.0f}  в={sv[1]/60:.1f}/{sv[0]:.0f}  г={sg[1]/60:.1f}/{sg[0]:.0f}")
    for kk, vv in inv.items():
        print(f"ИНВАРИАНТ [{vv}] {kk}")
    print(f"TierPrestigeMul для окупаемости ~8 заказов (при текущих прочих входах): {need}")
    kab = res["kaban"]
    print(f"кабан: crash={kab['crash']:.0f} vs корпус+60%={kab['hp_max']:.0f} → crash_k >= {PH['crash_k']*kab['hp_max']/kab['crash']:.4f} или HullHpPct <= {100*(kab['crash']/kab['hp']-1):.0f}%")
    print("Retro для тормозного пути 450 юн:", {s[0]: round((s[7] ** 2 - 75 ** 2) / (2 * TARGET_BRAKE)) for s in SHIPS if not s[4]})

# ============================ MAIN ==========================================
def main():
    out = sys.argv[1] if len(sys.argv) > 1 else OUT_DEFAULT
    wb = Workbook()
    ws_sum = wb.active; ws_sum.title = "СВОДКА"
    ws_ships = wb.create_sheet("КОРАБЛИ")
    ws_up = wb.create_sheet("УЛУЧШЕНИЯ")
    ws_ec = wb.create_sheet("ЭКОНОМИКА")
    ws_sc = wb.create_sheet("СЦЕНАРИИ")
    ec = build_economy(ws_ec)
    up = build_upgrades(ws_up, ec)
    first, last = build_ships(ws_ships, ec, up)
    ship_rows = [next(i for i, s in enumerate(SHIPS) if s[0] == sid) + first
                 for sid in ["skrepka", "egg_rental", "egg_own", "strela", "kaban", "delta", "igla", "meridian", "barin"]]
    blocks = build_scenarios(ws_sc, ec, up)
    build_summary(ws_sum, ship_rows, blocks)
    wb.save(out)
    print(f"Сохранено: {out}")
    for ws in wb.worksheets:
        n = sum(1 for row in ws.iter_rows() for c in row if isinstance(c.value, str) and c.value.startswith("="))
        n_cf = sum(len(rules) for rng, rules in ws.conditional_formatting._cf_rules.items())
        n_in = sum(1 for row in ws.iter_rows() for c in row if c.fill == IN_FILL)
        print(f"  {ws.title}: формул={n}, правил УФ={n_cf}, входов(жёлтых)={n_in}")
    verify()

if __name__ == "__main__":
    main()
