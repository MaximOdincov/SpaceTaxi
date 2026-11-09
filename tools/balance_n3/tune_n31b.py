#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
tune_n31b.py — калькуляция Н3.1-B «применение баланса».
Копия модели build_balance.py (формулы verify() не тронуты), входы обновлены
под решения оркестратора A-F:
  A. TierPrestigeMul = [1, 1.26, 1.52, 2.26, 4.28] — новая механика OrderPricing
  B. Retro по моделям: тормозной путь <= 450 юн и монотонно вниз с тиром
  C. crash_k 0.003 -> 0.0032 (LandingSystem.CrashDamagePerSpeedSq) — смерть всем корпусам
  D. комиссия аренды 25 -> 35 %
  E. доход BasePrice/PricePerDistanceUnit × SCALE — время (г) 30-35 мин
  F. бак Яйца FuelMax 100 -> 85 (1.5-2 заказа на бак)
Плюс перебор SCALE и цен кораблей (крайний рычаг) до полного зелёного.
Запуск: python3 tune_n31b.py
"""
import math

# ============================ ТЕКУЩИЕ ЗНАЧЕНИЯ АССЕТОВ (было) ================
# Assets/ScriptableObjects/Resources/Ships/*.asset
# (Id, Tier, Price, IsRental, Thrust, MaxV, FuelMax, HpMax, Retro)
SHIPS_BASE = [
    ("skrepka",    1,   900, 0,  88, 305,  90,  90, 62),
    ("egg_rental", 2,     0, 1,  95, 330, 100, 100, 60),
    ("egg_own",    2,  1500, 0,  95, 330, 100, 100, 60),
    ("strela",     3,  2600, 0, 118, 410, 120, 110, 64),
    ("kaban",      3,  3000, 0, 100, 350, 200, 150, 68),
    ("delta",      3,  3400, 0, 110, 385, 140, 130, 66),
    ("igla",       4,  5200, 0, 135, 470, 130,  90, 72),
    ("meridian",   4,  7800, 0, 118, 410, 170, 140, 70),
    ("barin",      5, 12000, 0, 140, 485, 240, 190, 75),
]

# Решение B: Retro per-модель (расчёт: путь = (MaxV^2-75^2)/(2*Retro) <= 450 и вниз с тиром;
# ровно 0.5*MaxV даёт 286->472 растущий путь, поэтому отношение ретро/MaxV растёт с тиром)
RETRO_NEW = {"skrepka": 98, "egg_rental": 120, "egg_own": 120, "strela": 198,
             "kaban": 143, "delta": 174, "igla": 276, "meridian": 208, "barin": 310}
# Решение F: бак Яйца (own+rental, одна модель)
FUEL_NEW = {"egg_rental": 85, "egg_own": 85}

# Assets/ScriptableObjects/Resources/UpgradeConfig_Default.asset (без изменений)
UP_PRICES = [300, 700, 1500]
UP_PCT = dict(hull_hp=60)

# PricingConfig_Default.asset: решения A/D/E
PRESTIGE = [1.0, 1.26, 1.52, 2.26, 4.28]   # решение A (индекс = тир-1)
RENTAL_COMM = 35                            # решение D (было 25)
PR_FIXED = dict(dist_cap=1500,
                sector_muls=[1.0, 1.0, 1.3, 1.6, 2.0],
                sector_weights=[0.0, 1.0, 0.0, 0.0, 0.0],
                penalty_pct=15, penalty_cap=45, debt_share=50,
                rating_th=4.0, rating_mul=1.1,
                fuel_price=[0.8, 1.0, 1.3, 1.8])

# Оценки-входы методики (как в build_balance.py)
EST = dict(rating_expected=1.1, tips_min=0.10, tips_max=0.25,
           collisions_per_order=1.0, avg_damage_per_order=40.0, fuel_tier_typ=2,
           deadhead=1.0, avg_speed_frac=0.60, maneuver_time=25.0, purchase_overhead=90.0)

# PhysicsConfig_Default.asset + LandingSystem
PH = dict(land_speed=75, fuel_drain=5.5, crash_k=0.0032)  # решение C (было 0.003)

PLANETS = [("Гавань", 0, 0), ("Мишень", 330, 40), ("Верстак", -350, 180),
           ("Маяк", 120, -520), ("Кромка", 760, -260), ("Печь", 250, 950)]

TARGET_PASS_MIN, TARGET_PASS_MAX = 30, 35
TARGET_PAYBACK = (6, 10)
TARGET_TANK_WEAK = (1.5, 2)
TARGET_TANK_MID = (3, 5)
TARGET_TANK_BARIN = 5
TARGET_BRAKE = 450

# ============================ МОДЕЛЬ (как verify() в build_balance.py) =======
def evaluate(scale, base_price, rate, prices):
    """scale — множитель дохода (применяется к BasePrice/ставке), prices — переопределение цен."""
    prestige = PRESTIGE
    sector = sum(m * w for m, w in zip(PR_FIXED["sector_muls"], PR_FIXED["sector_weights"]))
    tips = (EST["tips_min"] + EST["tips_max"]) / 2
    dists = [math.dist((p[1], p[2]), (q[1], q[2])) for i, p in enumerate(PLANETS) for q in PLANETS[i+1:]]
    avgdist = sum(min(x, PR_FIXED["dist_cap"]) for x in dists) / len(dists)
    flight = avgdist * (1 + EST["deadhead"])
    fuel_p = PR_FIXED["fuel_price"][EST["fuel_tier_typ"] - 1]
    res = {}
    for sid, tier, price, rental, thrust, maxv, fuelmax, hpmax, retro in SHIPS_BASE:
        price = prices.get(sid, price)
        fuelmax = FUEL_NEW.get(sid, fuelmax)
        retro = RETRO_NEW.get(sid, retro)
        avgv = maxv * EST["avg_speed_frac"]
        brake = (maxv ** 2 - PH["land_speed"] ** 2) / (2 * retro)
        rng_ = fuelmax / PH["fuel_drain"] * avgv
        card = round((base_price + min(avgdist, PR_FIXED["dist_cap"]) * rate)
                     * sector * prestige[tier - 1] * EST["rating_expected"])
        tips_c = round(card * tips)
        pen = round(min(EST["collisions_per_order"] * PR_FIXED["penalty_pct"], PR_FIXED["penalty_cap"]) / 100 * card)
        comm = round((card - pen) * RENTAL_COMM / 100) if rental else 0
        fuel_c = round(flight / avgv * PH["fuel_drain"] * fuel_p)
        repair = round(EST["avg_damage_per_order"])
        net = card + tips_c - pen - comm - fuel_c - repair
        res[sid] = dict(tier=tier, price=price, rental=rental, net=net, card=card, tank=rng_ / flight,
                        brake=brake, crash=PH["crash_k"] * (maxv - PH["land_speed"]) ** 2,
                        hp=hpmax, hp_max=hpmax * (1 + UP_PCT["hull_hp"] / 100),
                        t=flight / avgv + EST["maneuver_time"])
    pay = {}
    for sid in ("egg_own", "strela", "igla", "barin"):
        tier = res[sid]["tier"]
        prev = min((s for s in res.values() if s["tier"] == tier - 1 and not s["rental"]),
                   key=lambda s: s["price"])
        dn = res[sid]["net"] - prev["net"]
        pay[sid] = "не окуп." if dn <= 0 else round((res[sid]["price"] - prev["price"]) / dn, 2)
    def scen(steps):
        orders = time_s = 0.0
        for buy, fly in steps:
            cost = 600 if (buy == "UPG:пара") else (10000 if buy == "UPG:всё" else res[buy]["price"])
            n = cost / res[fly]["net"]
            orders += n
            time_s += n * res[fly]["t"] + EST["purchase_overhead"]
        return orders, time_s
    steps_full = [("skrepka", "egg_rental"), ("egg_own", "skrepka"), ("strela", "egg_own"),
                  ("kaban", "strela"), ("delta", "kaban"), ("igla", "delta"),
                  ("meridian", "igla"), ("barin", "meridian")]
    sa = scen(steps_full + [("UPG:всё", "barin")])
    sb = scen([("barin", "egg_rental")])
    sv = scen(steps_full)
    sg = scen([("skrepka", "egg_rental"), ("egg_own", "skrepka"), ("strela", "egg_own"),
               ("delta", "strela"), ("igla", "delta"), ("UPG:пара", "igla")])
    inv = {}
    nums = [v for v in pay.values() if isinstance(v, float)]
    inv["переходы"] = ("ЗЕЛЁНЫЙ" if len(nums) >= 4 and min(nums) >= 6 and max(nums) <= 10 else
                       "ЖЁЛТЫЙ" if len(nums) >= 4 and min(nums) >= 4 and max(nums) <= 14 else "КРАСНЫЙ")
    inv["аренда"] = ("ЗЕЛЁНЫЙ" if res["egg_rental"]["net"] > 0 and res["egg_rental"]["net"] < res["egg_own"]["net"]
                     else "КРАСНЫЙ")
    inv["время(г)"] = ("ЗЕЛЁНЫЙ" if TARGET_PASS_MIN <= sg[1] / 60 <= TARGET_PASS_MAX else
                       "ЖЁЛТЫЙ" if TARGET_PASS_MIN - 5 <= sg[1] / 60 <= TARGET_PASS_MAX + 10 else "КРАСНЫЙ")
    inv["(б) хуже (г)"] = ("ЗЕЛЁНЫЙ" if sb[0] > sg[0] * 1.1 and sb[1] > sg[1] else
                           "ЖЁЛТЫЙ" if sb[1] > sg[1] else "КРАСНЫЙ")
    inv["смерть"] = ("ЗЕЛЁНЫЙ" if all(res[s]["crash"] >= res[s]["hp_max"] for s in res) else "КРАСНЫЙ")
    tank_ok = True
    for sid, v in res.items():
        lo, hi = (TARGET_TANK_WEAK if v["tier"] <= 2 else
                  TARGET_TANK_MID if v["tier"] <= 4 else (TARGET_TANK_BARIN, 999))
        if not (lo <= v["tank"] <= hi):
            tank_ok = False
    inv["баки"] = "ЗЕЛЁНЫЙ" if tank_ok else "КРАСНЫЙ"
    ordered = sorted((s for s in SHIPS_BASE if not s[4]), key=lambda s: s[1])
    brake_ok = True
    for sid, tier, *_ in ordered:
        b = res[sid]["brake"]
        if tier == 1:
            brake_ok &= b <= TARGET_BRAKE
        else:
            prev_b = min(res[s[0]]["brake"] for s in ordered if s[1] == tier - 1)
            brake_ok &= b < prev_b
    inv["торможение"] = "ЗЕЛЁНЫЙ" if brake_ok else "КРАСНЫЙ"
    return dict(res=res, pay=pay, sa=sa, sb=sb, sv=sv, sg=sg, inv=inv,
                avgdist=avgdist, flight=flight, sector=sector, tips=tips, fuel_p=fuel_p)

# ============================ ПОИСК SCALE/ЦЕН ================================
def search():
    """Двухступенчатый перебор: (scale, skrepka, delta) грубо, цены переходов —
    по долям окна окупаемости 6-10 заказов."""
    best = []
    scales = [round(1.0 + 0.02 * i, 2) for i in range(0, 16)]        # 1.00..1.30
    skr = list(range(900, 1701, 100))
    delt = list(range(3400, 4401, 100))
    fracs = [0.30, 0.45, 0.60, 0.75, 0.88]  # доля окна [6Δ, 10Δ] с запасом от краёв
    for scale in scales:
        bp, rt = round(40 * scale), round(0.25 * scale, 4)
        for s_p in skr:
            for d_p in delt:
                probe = evaluate(scale, bp, rt,
                                 {"skrepka": s_p, "delta": d_p, "egg_own": 1500,
                                  "strela": 2200, "igla": 4300, "barin": 10000})
                nets = probe["res"]
                kaban_net = nets["kaban"]["net"]
                kaban_price = next(s[2] for s in SHIPS_BASE if s[0] == "kaban")
                dn_e = nets["egg_own"]["net"] - nets["skrepka"]["net"]
                dn_s = nets["strela"]["net"] - nets["egg_own"]["net"]
                for fe in fracs:
                    egg = round((s_p + (6 + 4 * fe) * dn_e) / 50) * 50
                    for fs in fracs:
                        st = round((egg + (6 + 4 * fs) * dn_s) / 100) * 100
                        t3 = [("strela", st, nets["strela"]["net"]), ("kaban", kaban_price, kaban_net),
                              ("delta", d_p, nets["delta"]["net"])]
                        _, cheap_t3, cheap_t3_net = min(t3, key=lambda x: x[1])
                        dn_i = nets["igla"]["net"] - cheap_t3_net
                        dn_b = nets["barin"]["net"] - nets["igla"]["net"]
                        for fi in (0.45, 0.60, 0.75, 0.88):
                            ig = round((cheap_t3 + (6 + 4 * fi) * dn_i) / 100) * 100
                            for fb in (0.45, 0.60, 0.75, 0.88):
                                br = round((ig + (6 + 4 * fb) * dn_b) / 100) * 100
                                prices = {"skrepka": s_p, "egg_own": egg, "strela": st,
                                          "delta": d_p, "igla": ig, "barin": br}
                                r = evaluate(scale, bp, rt, prices)
                                if any(v != "ЗЕЛЁНЫЙ" for v in r["inv"].values()):
                                    continue
                                pays = list(r["pay"].values())
                                g = r["sg"][1] / 60
                                bmargin = r["sb"][1] - r["sg"][1]
                                score = min(
                                    (min(g - TARGET_PASS_MIN, TARGET_PASS_MAX - g)) / 2.5,
                                    (TARGET_PAYBACK[1] - max(pays)) / 1.0,
                                    (min(pays) - TARGET_PAYBACK[0]) / 1.0,
                                    bmargin / 60.0 / 3.0,
                                    (r["sb"][0] / r["sg"][0] - 1.1) / 0.2,
                                )
                                best.append((score, scale, prices, pays, g, bmargin / 60,
                                             r["sb"][0] / r["sg"][0]))
    best.sort(key=lambda x: -x[0])
    return best[:15]

def report(scale, prices):
    bp, rt = round(40 * scale), round(0.25 * scale, 4)
    r = evaluate(scale, bp, rt, prices)
    res, pay, inv = r["res"], r["pay"], r["inv"]
    print(f"=== SCALE={scale}: BasePrice {40}->{bp}, rate {0.25}->{rt} ===")
    print(f"avgdist={r['avgdist']:.0f} flight={r['flight']:.0f} sector={r['sector']:.2f}")
    print(f"{'id':11}{'tier':>4}{'price':>7}{'card':>6}{'net':>6}{'tank':>6}{'brake':>7}{'crash':>7}{'t/ord':>7}")
    for sid, v in res.items():
        print(f"{sid:11}{v['tier']:>4}{v['price']:>7}{v['card']:>6}{v['net']:>6}{v['tank']:>6.2f}"
              f"{v['brake']:>7.0f}{v['crash']:>7.0f}{v['t']:>7.1f}")
    print(f"окупаемость: {pay}")
    for key, tag in [("sa", "а"), ("sb", "б"), ("sv", "в"), ("sg", "г")]:
        o, t = r[key]
        print(f"сценарий ({tag}): {t/60:.1f} мин / {o:.0f} заказов")
    print("инварианты:", inv)

if __name__ == "__main__":
    print("=== ПОИСК (SCALE × цены 4 кораблей, все инварианты зелёные) ===")
    top = search()
    for score, scale, prices, pays, g, bm, oratio in top:
        print(f"score={score:.2f} scale={scale} prices={prices} pay={pays} (г)={g:.1f}м "
              f"(б)-(г)={bm:+.1f}м заказы(б)/(г)={oratio:.2f}")
    if top:
        print()
        report(top[0][1], top[0][2])
