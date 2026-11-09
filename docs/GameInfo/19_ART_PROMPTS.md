# 19 — ПРОМПТЫ ГЕНЕРАЦИИ ТЕКСТУР БЕТЫ (для gpt-image / ChatGPT)

Дополнение к `18_ART_TASKS_NIGHT.md`. Промпты на EN (так генератор точнее),
пояснения на RU. Стиль — строго `13_ART_STYLE.md`: флэт-вектор, 2 тона
(база + полутон-полумесяц СПРАВА), без контуров/градиентов/падающих теней/текста.

**Как пользоваться:**
- Генератор: gpt-image, формат квадрат 1:1, transparent background.
- **Одиночные** — один ассет на кадр (корабли, хаб, диспетчер, фон меню, панели).
- **Коллажи** — один кадр, N объектов в ровной невидимой сетке; потом вырезаешь
  по ячейкам (без линий-направляющих — режешь с запасом от соседа).
- После вырезания, для КАЖДОГО спрайта: порог альфы ≥24 (снимает ореолы/пыль),
  трим + паддинг до целевого холста, снап палитры к HEX при дрейфе,
  даунскейл-тест (корабль/планета 64px, портрет 48px, иконка 24px).

**Хвост [СТИЛЬ] — дописывать в конец КАЖДОГО одиночного промпта:**
```
2D game asset, flat vector illustration, bold chunky shapes, simple clean
silhouette, limited palette, two-tone cel shading (flat base color plus a
darker crescent shade on the right side), no drop shadows, no cast shadows,
no outlines, no gradients, no texture noise, no text or numbers,
cartoon-friendly mood, isolated object on transparent background, crisp edges,
full object visible with small margin, centered, square image.
```

**Хвост [КОЛЛАЖ] — дописывать в конец КАЖДОГО коллажного промпта** (вместо
последней строки [СТИЛЬ]; линии не просим — чистый фон):
```
A neat collage of separate isolated objects arranged in an even grid of equal
invisible cells on a fully transparent background: each object fully inside
its own cell, generously spaced, objects never touch or overlap each other.
Same style for every object: 2D game asset, flat vector illustration, bold
chunky shapes, simple clean silhouettes, limited palette, two-tone cel shading
(darker crescent on the right side), no drop shadows, no outlines, no
gradients, no text, crisp edges, square image.
```

Целевые холсты и пути указаны у каждого пункта. Палитра — `13_ART_STYLE.md` §1:
жёлтый #FFD44D / тёмный #1A2033 / чекер-чёрный #0B0E1A / зелёный #3CFF8E /
белый #F2F5FF / danger #FF7A5C (в ассетах не используется).

---

## 1. КОРАБЛИ — 7 новых (Яйцо уже в проекте — не генерить)

**Правила деталей [2026-11-05, правка №2 — баланс]:**
- Первая версия промптов была перегружена (генератор выдавал парадные модели
  с чекерами), вторая — слишком пустая. ЗОЛОТАЯ СЕРЕДИНА: крупные формы +
  характерные детали (панели, швы, огни, окна) — но СПАРСЕ, не плотно.
- НЕТ: жёлтого (только «Яйцо»), чекер-лент, плотных заклёпок, глянца,
  декоративного мусора. Слово «space taxi» в промптах не использовать.
- Акцентные зоны — простые плоские пятна (перекрашиваешь сам).
- Т-3 намеренно нелепые: «из непонятных частей», луповатые пропорции.
  Т-4/Т-5 — опрятные, аккуратность формой, не декором.
- Т-1 «Скрепка» получилась — не трогаем.

Все: 512×512, вертикально, **нос строго вверх**, сопло БЕЗ огня (выхлоп —
частицы в Unity).

**Хвост [СТИЛЬ-КОРАБЛЬ] — вместо [СТИЛЬ] для всех кораблей этого раздела:**
```
2D game asset, flat vector illustration, strictly 2D flat side view, perfectly
straight and vertical, upright, no perspective, no tilt, no rotation, no 3D
look. Simple toy-like design built from a few large chunky shapes, bold clean
silhouette, limited palette of 3-4 flat colors, two-tone cel shading (flat
base color plus a darker crescent shade on the right side), sparse light
details only — a few panel seams and a couple of small lights are welcome,
but nothing dense or fussy, no checker pattern, no yellow, no rivets, no
dense greebles, no drop shadows, no cast shadows, no outlines, no gradients,
no texture noise, no text or numbers, cartoon-friendly mood, isolated object
on transparent background, crisp edges, full object visible with small
margin, centered, square image.
```

### 1.1 «Скрепка» Т-1 (самопал-джанк) → `Assets/Sprites/Ships/skrepka.png`
УЖЕ ПОЛУЧИЛАСЬ (Т-1 — эталон). Промпт оставлен как был:
```
A hilariously lopsided homemade junk spaceship assembled from mismatched
parts, vertical orientation, top pointing up. Strictly 2D flat side view,
perfectly upright, no perspective, no tilt, no rotation, no 3D look. A rusty
barrel fuel tank section in rust brown #8A5A33, a mustard yellow cargo box
section #C9A94A, a small crooked cockpit pod bolted on at a slight angle, two
mismatched thruster nozzles of clearly different sizes at the bottom without
flame, one crooked homemade tail fin, a wire antenna topped with a tiny tin
can, strips of dark duct tape across the seams. A crooked two-row
black-and-mustard checker sticker (#C9A94A and #0B0E1A) peeling at the edges.
Rusty patches, visible bolt dots, slightly dented silhouette. It is NOT a
classic rocket: no pointed nose cone, no sleek tail fins. Sad but lovable
junkyard charm.
```
`+ [СТИЛЬ]`

### 1.2 «Стрела» Т-3 → `Assets/Sprites/Ships/strela.png`
```
A goofy cartoon spaceship shaped like an elongated bullet with three long
thin fins, vertical orientation, nose pointing up. Flat muted orange-brown
body with a slightly darker accent nose panel, one small round window with a
simple frame, a few simple panel seams, a couple of tiny green light dots,
one simple nozzle at the bottom without flame. Looks assembled from unclear
simple parts, ugly-cute, slightly lumpy awkward proportions, nothing fancy.
```
`+ [СТИЛЬ-КОРАБЛЬ]`

### 1.3 «Кабан» Т-3 → `Assets/Sprites/Ships/kaban.png`
```
A goofy cartoon spaceship shaped like a squat heavy barge with a blunt flat
nose, vertical orientation, nose pointing up. Flat muted steel-grey body with
a few flat armor plates on top, TWO big simple cylindrical tanks bolted on
the sides, a narrow slit window, one wide nozzle at the bottom without flame,
a couple of small lights. Looks assembled from unclear simple parts,
ugly-cute, awkward squat proportions, nothing fancy.
```
`+ [СТИЛЬ-КОРАБЛЬ]`

### 1.4 «Дельта» Т-3 → `Assets/Sprites/Ships/delta.png`
```
A goofy cartoon spaceship shaped like a wide flat triangle wedge pointing up,
vertical orientation. Flat muted teal-grey body with a simple darker accent
panel, a small bubble window near the front tip, two engine pods at the two
rear corners without flame, a few thin panel seams, a couple of tiny lights.
Looks assembled from unclear simple parts, ugly-cute, slightly awkward
proportions, nothing fancy.
```
`+ [СТИЛЬ-КОРАБЛЬ]`

### 1.5 «Игла» Т-4 → `Assets/Sprites/Ships/igla.png`
```
A neat cartoon spaceship shaped like a very long thin needle with an ENORMOUS
ring-shaped engine at the bottom (the ring is wider than the hull), vertical
orientation, nose pointing up. Flat light grey body with a thin accent stripe
along it, one small round cockpit near the top, a few clean panel seams, a
couple of small lights, no flame. Simple and elegant, sporty but plain —
few details, placed sparingly.
```
`+ [СТИЛЬ-КОРАБЛЬ]`

### 1.6 «Меридиан» Т-4 → `Assets/Sprites/Ships/meridian.png`
```
A neat cartoon spaceship shaped like a plump smooth oval fuselage with two
arc-shaped wings and one merged engine block at the bottom, vertical
orientation, nose pointing up. Flat dark grey body with one thin light stripe
along it, a small row of two or three round windows, a few clean panel seams,
a couple of small lights, no flame. Simple and elegant, comfortable but
plain — few details, placed sparingly.
```
`+ [СТИЛЬ-КОРАБЛЬ]`

### 1.7 «Барин» Т-5 → `Assets/Sprites/Ships/barin.png`
```
A neat cartoon spaceship shaped like a large three-section liner: a rounded
nose section, a wide middle with a plain window band, a stern with three
nozzles in a row without flame, vertical orientation, nose pointing up. Flat
white-grey body with a few flat gold trim shapes on the edges, a couple of
small green running lights, a few clean panel seams, no flame. Rich only by
its shape and gold trim, not by decoration — few details, placed sparingly.
```
`+ [СТИЛЬ-КОРАБЛЬ]`

---

## 2. ТЕЛА СИСТЕМЫ

### 2.1 Хаб МТО (станция-сфера) → `Assets/Sprites/Stations/hab_mto.png`, 1024×1024
Готовый промпт **П2а** из `13_ART_STYLE.md` §10 (v6: строго круглый, ничего
не торчит, панельные швы, экваториальная траншея с док-нишами, 3 вент-диска,
2 люка, буквы «MTO» — тут текст разрешён; если генератор кривит буквы —
перепечатать кодом поверх). Генерить 2–3 варианта.

### 2.2 Планеты-архетипы ×9 — КОЛЛАЖ 3×3 → `Assets/Sprites/Planets/archetypes.png`
Дальше из этих 9 баз перекраской/вариациями собираются 24 планеты BetaMap
(тёплые оттенки по секторам: Near зелёно-олива, Mid1 песок-охра, Mid2
красно-ржавое, Far сине-сталь).
```
A neat collage of NINE separate cartoon planets arranged in an even 3x3 grid,
each planet a perfect circle fully inside its own invisible cell, generous
spacing, never touching. Row by row: 1) a pale grey cratered moon with a few
large flat craters, 2) a rocky brown planet with big flat continent patches,
3) a striped gas giant with wide horizontal flat bands, 4) an icy pale-blue
planet with flat white polar caps, 5) a volcanic planet with a dark surface
and a few flat glowing lava cracks, 6) a mushroom-bio planet with round flat
spore patches, 7) a rusty techno planet with large flat panel plates and thin
darker seam lines, 8) a deep-blue ocean planet with flat light island patches,
9) a sandy dune planet with flat wavy dune stripes. Only large details, two or
three per planet, perfectly round silhouettes with nothing sticking out beyond
the circles.
```
`+ [КОЛЛАЖ]`

### 2.3 Планеты-станции ×4 сети — КОЛЛАЖ 2×2 → `Assets/Sprites/Stations/networks.png`
```
A neat collage of FOUR separate cartoon planets arranged in an even 2x2 grid,
each a whole round planet with a small fuel station complex flush with the
round surface on its left lit side, nothing sticking out beyond the circle.
1) a rusty cheap network planet in rust orange #8A5A33: rusty patches, one
crooked old fuel pump, cracked spheres, paint drips; 2) a plain steel-grey
utility network planet: clean surface, two big white spherical storage tanks
with dark seams, one thick dark pipe, one large round pump disc with a big
simple fuel droplet emblem; 3) a neat turquoise network planet #3FD9C4: fresh
paint, even rows of glossy tanks, a glowing blank sign plate, softly lit pads;
4) a white-gold luxury network planet: white panel surface with golden trim
#F5C84C, a small crystal canopy, golden lights. Honest working gas-station
vibe in all four, different wealth levels.
```
`+ [КОЛЛАЖ]`

### 2.4 Солнце, чёрная дыра, комета, кольцо — КОЛЛАЖ 2×2 → `Assets/Sprites/Bodies/bodies.png`
```
A neat collage of FOUR separate objects arranged in an even 2x2 grid, each
fully inside its own invisible cell, never touching. 1) a cheerful cartoon
sun: a round yellow-orange disk #FFD44D with a flat darker orange crescent
shade on the right and a few short blunt triangular corona spikes around the
edge; 2) a black hole: a solid very dark circle #0B0E1A in the center wrapped
by a wide flat glowing ring disk of purple and magenta #B44DFF swirling
around it, the ring drawn as flat bold shapes; 3) a comet nucleus: a small
lumpy icy grey rock with a few flat pale-blue ice patches, no tail, no flames;
4) a planetary ring: a flat elliptical ring band, pale beige with two thin
darker groove lines, seen at a slight tilt like Saturn's rings.
```
`+ [КОЛЛАЖ]` (орёол зоны дыры и хвост кометы в игре — код/частицы, генерить не надо)

### 2.5 Астероиды ×9 — КОЛЛАЖ 3×3 → `Assets/Sprites/Bodies/asteroids.png`
Для пояса (в игре красятся кодом в серо-коричневые тона).
```
A neat collage of NINE separate small cartoon asteroids arranged in an even
3x3 grid, each fully inside its own invisible cell, never touching. Different
lumpy grey-brown rock silhouettes: rounded potato, split boulder, flat shard,
dented ball, long pebble, chunky cube-ish rock, a rock with one big flat
crater, a rock with a thin crack line, a small cluster of three pebbles.
Only large flat facets and simple craters, no small noise details.
```
`+ [КОЛЛАЖ]`

---

## 3. ПОРТРЕТЫ ПАССАЖИРОВ (модульная сборка, `13_ART_STYLE.md` §11)

Холст модулей 256×256. Все — строго ФРОНТАЛЬНО, флэт, 2 тона. Сборка кодом:
силуэт + глаза + нос + рот + аксессуар; тонирование `SpriteRenderer.color`.

### 3.1 Силуэты ×5 — КОЛЛАЖ (ряд 5 в ширину) → `Assets/Sprites/Portraits/bodies.png`
Лицо ПУСТОЕ (никаких черт!), тело светло-нейтральное (под tint).
```
A neat collage of FIVE separate cartoon alien creature busts arranged in an
even horizontal row of five equal invisible cells, each fully inside its own
cell, never touching, all facing the viewer frontally, all with COMPLETELY
EMPTY BLANK faces — no eyes, no nose, no mouth, no markings. 1) a tall
slender humanoid bust with a long oval head and narrow sloped shoulders;
2) a short fat round pot-bellied bust with a wide body and stubby shoulders;
3) a slug-like bust: a low wide soft body with a smooth rounded head merging
into it, no neck; 4) a crab-like bust: a wide flat head shell with two small
claw-like shoulder pincers and stalk-less round eye sockets area left blank;
5) a droplet blob bust: a soft teardrop-shaped body with a round head flowing
into rounded shoulders. Light neutral pale beige-green body color for all
five, so they can be tinted later.
```
`+ [КОЛЛАЖ]`

### 3.2 Черты лица ×9 — КОЛЛАЖ 3×3 → `Assets/Sprites/Portraits/features.png`
Строго изолированные (решение владельца): без головы, чистые формы на
прозрачном. При вырезании — центрировать каждую в 256×256.
```
A neat collage of NINE simple flat cartoon facial feature icons arranged in an
even 3x3 grid, each feature fully inside its own invisible cell, isolated,
never touching, no head, no face shape, just the bare feature on transparent
background. Row 1 — three EYES sets in dark graphite #1A2033 with clean white
eyeball zones #F2F5FF: 1) a pair of big round eyes with large dark pupils,
2) a pair of narrow sleepy almond eyes half-closed, 3) one single big cyclops
eye in the center. Row 2 — three NOSES in dark graphite #1A2033: 4) two tiny
nostril dots, 5) one large soft oval nose, 6) a small triangular beak nose.
Row 3 — three MOUTHS in dark graphite #1A2033: 7) a friendly smile arc line,
8) a straight calm line mouth, 9) an open surprised O-shaped mouth.
Bold chunky flat shapes, thick forms readable at 48 pixels.
```
`+ [КОЛЛАЖ]` (зрачки потом перекрашиваешь; позиции при сборке кодом: глаза ~35% высоты лица, нос ~50%, рот ~65%)

### 3.3 Аксессуары ×5 — КОЛЛАЖ (ряд 5) → `Assets/Sprites/Portraits/accessories.png`
```
A neat collage of FIVE separate cartoon head accessories arranged in an even
horizontal row of five equal invisible cells, each fully inside its own cell,
isolated, never touching, each designed to sit on top of a round alien head,
frontal view. 1) a dispatcher cap in dark graphite #1A2033 with a yellow
band #FFD44D and a small round blank badge (no text); 2) a soft bucket hat
panama in olive; 3) chunky over-ear headphones with a thin band; 4) a warm
wrapped scarf in warm red-orange; 5) a small flower with round petals on a
short stem. Bold chunky flat shapes, readable at 48 pixels.
```
`+ [КОЛЛАЖ]`

### 3.4 Диспетчер МТО (фиксированный портрет) → `Assets/Sprites/Portraits/dispatcher.png`, 256×256
Цельное лицо корпорации (не модуль): гуманоид + кепка + гарнитура.
```
2D game character portrait, flat vector illustration, bold chunky shapes,
two-tone cel shading (flat base color plus a darker crescent on the right
side), no outlines, no gradients, no drop shadows, no background. A corporate
space taxi dispatcher: a round-headed humanoid alien in a dark graphite
dispatcher cap #1A2033 with a yellow band #FFD44D and a small round blank
badge, wearing a thin headset with a small microphone, one big cyclops eye
looking at the viewer, a small calm professional smile, medium graphite body
tone. Frontal bust close-up, head fills most of the frame, perfectly centered.
Cartoon-friendly, corporate but warm. Isolated on transparent background,
crisp edges, square image.
```

---

## 4. UI

### 4.1 Иконки экономики/улучшений ×9 — КОЛЛАЖ 3×3 → `Assets/Sprites/UI/icons_econ.png`
Все — ОДНОЦВЕТНЫЕ белые #F2F5FF (тонирование кодом, как иконка станции альфы),
толстые формы, читаются на 24px.
```
A neat collage of NINE minimal monochrome white UI icons arranged in an even
3x3 grid, each fully inside its own invisible cell, never touching. Solid
flat white #F2F5FF shapes only, single color, generous negative space, thick
simple forms readable at 24 pixels, game UI icon style. Row 1: 1) a round
coin with a simple checker band stripe across the middle, 2) a fuel droplet,
3) a wrench; row 2: 4) a rocket engine nozzle with a small motion arrow,
5) a fuel canister tank, 6) a shield; row 3: 7) two horizontal arrows
pointing left and right (maneuvering), 8) a chain link (debt), 9) a five-
pointed star (rating). No outlines, no gradients, no text, crisp edges,
square image.
```

### 4.2 Иконки карты/предупреждения ×6 — КОЛЛАЖ 2×3 → `Assets/Sprites/UI/icons_map.png`
```
A neat collage of SIX minimal monochrome white UI icons arranged in an even
grid of two rows and three columns, each fully inside its own invisible cell,
never touching. Solid flat white #F2F5FF shapes only, single color, thick
simple forms readable at 24 pixels. 1) a planet: circle with one flat crater
dot; 2) a star: circle with short blunt rays; 3) a black hole: dark ring with
a small center dot; 4) an asteroid belt: three small rocks in a diagonal arc
with a thin dotted arc line; 5) a comet: small circle head with three short
motion lines behind; 6) a warning triangle with an exclamation mark. No
outlines, no gradients, no text, crisp edges, square image.
```

### 4.3 Панели UI 9-slice — 4 одиночных генерации (края важны для нарезки)
Общий каркас (подставлять тему):
```
A single square retro sci-fi UI panel frame filling most of the canvas, flat
vector illustration, [ТЕМА]. A rounded rectangle panel with a clean empty
inner area and a distinct decorated border suitable for 9-slice slicing:
straight equal-width edges, simple corners, small [ДЕТАЛЬ] accents on the
border. Dark background #08140C inner, border [ЦВЕТ]. Flat colors, no
gradients, no text, no icons inside, the inner area completely empty, centered,
square image.
```
- Станция: [ТЕМА] «corporate taxi station terminal, yellow corporate identity», [ДЕТАЛЬ] «two-row checker», [ЦВЕТ] #FFD44D → `panel_station.png`
- Завершение заказа: [ТЕМА] «mission-complete stamp terminal, warm success mood», [ДЕТАЛЬ] «corner brackets», [ЦВЕТ] #3CFF8E → `panel_complete.png`
- Увольнение: [ТЕМА] «stern dismissal notice terminal, cold corporate mood», [ДЕТАЛЬ] «stamped corner mark», [ЦВЕТ] #FF7A5C → `panel_dismissal.png`
- Карточка тела на карте: [ТЕМА] «compact cartography card», [ДЕТАЛЬ] «thin double border», [ЦВЕТ] #5BC98F → `panel_card.png`

### 4.4 Арт-зона главного меню → `Assets/Sprites/UI/menu_bg.png`, 1024×1024
Единственный ассет С ФОНОМ (звёзды разрешены).
```
2D game main menu background, flat vector illustration, bold chunky shapes.
A wide dark space vista: deep blue-black starfield with a few tiny flat white
stars, the giant yellow corporate sphere station #FFD44D looming large on the
right side (panel seams, dark equatorial trench with docking bays, big bold
dark letters "MTO"), a small cute yellow space taxi rocket flying toward it
leaving a flat dotted route line, a distant flat comet. Two-tone cel shading,
no outlines, no gradients (flat color blocks only), cartoon-friendly mood,
square image.
```

---

## 5. ЧТО ГЕНЕРИТЬ НЕ НУЖНО (уже есть / делается кодом)

- **Яйцо** — спрайт уже в проекте (промпт П1 в `13_ART_STYLE.md` §10, если захочешь перегенерить).
- **Шашка аренды** — рисована кодом (PIL), `Assets/Sprites/ship_shashka.png`.
- **TargetGlow, посадочное кольцо, ореол жаровни, зона дыры, кольцо станции В1** — кодовые спрайты/частицы.
- **След кометы, выхлопы, взрыв** — частицы в Unity.
- **Кнопки плеера |< ▶ ⏭** — глифы шрифта.
- **Перекраска кораблей/планет** — вручную по зонам (`14_SHIP_LINEUP` §4), генерация не нужна.

## 6. Порядок генерации (рекомендация)

1. Корабли 7 (пакет 1: Скрепка/Кабан/Дельта, пакет 2: Стрела/Игла/Меридиан/Барин) → линейный тест «тир читается на 64px».
2. Коллажи тел: планеты ×9, станции ×4, тела 2×2, астероиды ×9.
3. Портреты: силуэты, черты, аксессуары, диспетчер → тест сборки кодом.
4. UI: иконки ×2, панели ×4, фон меню.
5. Вставка: корабли в ShipConfig (инспектор, поле ShipSprite), планеты — по путям `Assets/Sprites/Planets/` + раздача в BetaMap01, остальное по путям выше.
```
