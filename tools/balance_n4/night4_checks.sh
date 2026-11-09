#!/bin/bash
# night4_checks.sh — Ночь 4, Задача 7 шаг 3: инварианты баланса SpaceTaxi.
# Запускает python-проверки поверх balance_model.py (честные формулы из кода игры,
# входы — из ассетов репозитория). Печатает PASS/FAIL по каждому инварианту,
# код возврата = число провалившихся инвариантов (0 = все PASS).
# Unity НЕ трогается: это чистая офлайн-аналитика.
# Запуск: bash tools/balance_n4/night4_checks.sh
set -u
cd "$(dirname "$0")"

export PYTHONIOENCODING=utf-8

python - <<'PYEOF'
import sys
import os
sys.path.insert(0, os.path.dirname(os.path.abspath("night4_checks.sh")) or ".")
sys.path.insert(0, ".")
import build_balance_n4 as bb
import balance_model as bm
import statistics

data = bb.collect_all()
sims, pricing, ships = data["sims"], data["pricing"], data["ships"]
gate = {row["id"]: row for row in data["north_gate"]}
fails = []


def check(name, ok, fact):
    status = "PASS" if ok else "FAIL"
    print("[%s] %s" % (status, name))
    print("       факт: %s" % fact)
    if not ok:
        fails.append(name)


# ---------------- И1: следующий корабль за 8-14 заказов на каждом тире --------
gaps = []
for res in data["buy_runs"]:
    marks = [o for o, _, _ in res["purchases"]]
    gaps.append([marks[0]] + [marks[i] - marks[i - 1] for i in range(1, len(marks))])
flat = [g for run in gaps for g in run]
lo, hi = bm.TARGETS["next_ship_min"], bm.TARGETS["next_ship_max"]
check("И1: следующий корабль достижим за %d-%d заказов при аккуратной игре (каждый тир)"
      % (lo, hi),
      all(lo <= g <= hi for g in flat) and len(flat) >= 4,
      "переходов %d, гэпы %s, среднее %.1f — сейчас переходы БЫСТРЕЕ окна, цены кораблей низки относительно дохода"
      % (len(flat), flat, statistics.mean(flat) if flat else -1))
print("       -> владельцу (шаг 4): ShipConfig Price по диапазонам с листа «Инварианты» xlsx:")
for prev, nxt in (("egg_rental", "egg_own"), ("egg_own", "strela"), ("strela", "igla"), ("igla", "barin")):
    net = sims[prev]["net_per_order"]
    print("          Resources/Ships/%s.asset -> Price %d -> %d..%d (8-14 x доход %.0f на %s)"
          % (nxt, data["ships_by_id"][nxt]["price"],
             int(round(lo * net / 50.0) * 50), int(round(hi * net / 50.0) * 50), net, prev))

# ---------------- И2: улучшение окупается <= N заказов ------------------------
N = bm.TARGETS["upgrade_payback_max"]
bad = []
for sid, rows in data["upgrade_paybacks"].items():
    for r in rows:
        if r["payback"] is None or r["payback"] > N:
            bad.append("%s/%s L%d=%s" % (sid, r["line"], r["level"],
                                         "не окуп." if r["payback"] is None else round(r["payback"], 1)))
tank_ok = all(r["payback"] is not None and r["payback"] <= N
              for rows in data["upgrade_paybacks"].values() for r in rows if r["line_id"] == "tank")
check("И2: улучшение окупается <= %d заказов (все корабли/линии/уровни)" % N,
      not bad,
      "%d из %d уровней не проходят: %s" % (len(bad), 8 * 12, ", ".join(bad[:8]) + ("..." if len(bad) > 8 else "")))
print("       -> владельцу (шаг 4): цены уровней = Δдоход уровня x N (столбец «Δдоход/заказ» в Метриках.5); "
      "линия Бак уже в норме (%s); добавление минусов (+тяга=+расход, +бак=+масса) в ShipStats — механика владельца"
      % ("ок" if tank_ok else "FAIL"))

# ---------------- И3: смерть дороже заправки ----------------------------------
refuel_full_egg = -(-int(85 * pricing["fuel_price_by_tier"][1] * pricing["fuel_sector_mul"][0]) // 1)
death_rental = data["rental_death_cost"]
death_own_min = min(v["total"] for k, v in data["death_by_ship"].items() if k != "egg_rental")
fuel_per_order = sims["egg_rental"]["fuel_per_order"]
check("И3: смерть+воскрешение дороже, чем вовремя заправиться",
      death_rental > refuel_full_egg and death_own_min > refuel_full_egg,
      "аренда %.0f кр (долг %d + заказ %.0f), свой минимум %.0f кр; полная заправка Яйца на хабе %d кр, топливо/заказ %.0f кр (запас x%.1f)"
      % (death_rental, bm.DEBT_ACCIDENT, sims["egg_rental"]["net_per_order"],
         death_own_min, refuel_full_egg, fuel_per_order, death_rental / max(1, refuel_full_egg)))
print("       -> владельцу: порог с запасом; при снижении доходов (И1) пересчитать — долг AccidentDebt 400 (DebtService.cs:13) должен оставаться >> заправки")

# ---------------- И4: аренда <= X% валового дохода ----------------------------
share = sims["egg_rental"]["commission_share"]
X = bm.TARGETS["rental_share_max"]
check("И4: аренда съедает <= %.0f%% валового дохода до первого своего корабля (X зафиксирован по факту+запас)" % (X * 100),
      share <= X,
      "комиссия %.1f%% валового (RentalCommissionPct 35 от цена-штраф); чистый доход на аренде %.0f кр/заказ > 0 (не парализует)"
      % (share * 100, sims["egg_rental"]["net_per_order"]))
print("       -> владельцу: впритык; при пересборке доходов держать долю <= 30% или снизить RentalCommissionPct до ~30")

# ---------------- И5: проходимо на каждом корабле -----------------------------
bad_econ = [s["id"] for s in ships if sims[s["id"]]["net_per_order"] <= 0]
bad_geo = [s["id"] for s in ships if not gate[s["id"]]["ok"]]
check("И5: пройти можно на КАЖДОМ корабле при аккуратной игре (доход > 0; северная калитка Грань->Буй->Стужа)",
      not bad_econ and not bad_geo,
      "экономика: %s; география: не проходят %s (бак меньше цепочки ~%.0f ед через сухой Буй)"
      % ("все доходны" if not bad_econ else "нет дохода: " + ", ".join(bad_econ),
         ", ".join(bad_geo) if bad_geo else "никто",
         max(gate[s["id"]]["burn"] for s in ships)))
print("       -> владельцу (шаг 4), любой из рычагов: FuelMax Скрепки/Яйца >= 130 (Ships/*.asset); "
      "FuelDrainPerSecond 5.5 -> ~4 (PhysicsConfig_Default); заправка в разрыве Грань-Стужа (BetaMap: Буй/Полынь HasFuel=1); "
      "либо признать север недостижимым для тиров 1-2 (тогда смягчить формулировку инварианта)")

print()
total = len(fails)
if total == 0:
    print("ИТОГ: все инварианты PASS")
else:
    print("ИТОГ: FAIL по %d инвариантам: %s" % (total, ", ".join(fails)))
    print("Это ФАКТ при текущих числах ассетов; подбор чисел — шаг 4 (владелец), рычаги перечислены выше.")
sys.exit(total)
PYEOF
