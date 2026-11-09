# 13 — АРТ-СТИЛЬ И ГЕНЕРАЦИЯ АССЕТОВ (альфа)

Рабочий док сессий художника. Скоуп — `12_ALPHA_PLAN.md` §7 (корабль, пассажиры,
иконка станции). Стиль-рамка плана конкретизирована здесь до воспроизводимого
состояния; док живой — ход отбора пишется в §8.

---

## 0. Решения [2026-09-27]

| # | Решение |
|---|---|
| 1 | Ветка стиля: **чистый флэт** — 2 тона на материал, БЕЗ контуров |
| 2 | Портреты: альфа — **1 универсальный цельный** бюст на прозрачном фоне; рамка-«удостоверение» (плашка #08140C + рамка #3CFF8E) рисуется UI [пересмотрено 2026-09-27: было 10 шт запечёнными] |
| 3 | Корабли проектируются как **семейство по конструктору** (§4): базовая модель сейчас, покупаемые позже — те же слоты с другими значениями |
| 4 | **Шашка = маркер аренды** корпорации: ОТДЕЛЬНЫЙ спрайт-оверлей (на арендованных есть, на своих нет). Чекер-лента — фирменный код на всех кораблях корпорации |
| 5 | Генератор: тестовый батч в **Recraft и gpt-image (ChatGPT)**, выбор после сравнения (§7) |
| 6 | Выхлоп/след двигателей — ТОЛЬКО частицами в Unity, в спрайт не печь |
| 7 | Генератор: **gpt-image (ChatGPT)** [2026-09-27, тест; Recraft не запустился] |
| 8 | Ракета — НЕ базовая модель (решение Макса): чищеные Т1/Т2 — задел под покупаемые |
| 9 | Перекраска вариантов (акцент-полоса/корпус) — вручную, генерация не нужна |
| 10 | Иконка станции ОТМЕНЯЕТСЯ: станции видны через облик самой планеты — вариант выбирается (§9) |
| 11 | Базовая модель: **капсула-«яйцо»**, ширина ≈ высоте [УТВЕРЖДЕНО 2026-09-27] |
| 12 | Станции: **В2** — планеты со станцией получают собственные полнокрасочные текстуры (шашечная лента по экватору); рендер готовым `BodyVisuals.Sprite` БЕЗ тонирования — код уже поддерживает [УТВЕРЖДЕНО 2026-09-27] |
| 13 | Свет/тон: **2 тона — база + полутон-полумесяц СПРАВА** (как в Т1/Т2); ЗАПРЕЩЕНЫ только падающие/отбрасываемые тени [пересмотрено Максом 2026-09-27; Стрелы НЕ выравниваются] |
| 14 | Индивидуализация «Яйца»: **номерная табличка-бланк** на корпусе (пустая, без символов; цвет пластины — зона перекраски, номер накладывается в редакторе) [2026-09-27] |
| 15 | **Хаб = гигантская искусственная станция-сфера** (вайб «Звезды смерти», жёлтая, дружелюбная): панельные швы, экваториальная траншея с ОДИНАКОВЫМИ док-нишами равномерно по кругу (посадка в любое место — НИКАКОЙ выделенной площадки) [пересмотрено Максом 2026-09-27 ×3] |
| 15а | **Название корпорации: МТО / MTO** — выбрано Максом 2026-09-27 (вариант из `02_STORY.md` §4; там пометить решение). На хабе — КРУПНЫЕ тёмные буквы «MTO» по центральной панели (бренд на борту); если генератор кривит буквы — перепечатать кодом поверх |
| 16 | **Заправки = 4 компании-тира:** ржавая (дешёвая) / простая / аккуратная / люкс; своя айдентика и цвет сети. Альфа = Т-2 «Простая», ОДНА текстура на все станции уровня (одна сеть) [решение Макса 2026-09-27]; чекер-лента как маркер станции ОТМЕНЕНА |
| 16а | **Правило мира: из планет/станций НИЧЕГО не торчит** — посадка возможна с любой стороны, силуэт тел строго круглый; вся техника — формы НА поверхности, заподлицо с корпусом [Макс, 2026-09-27] |
| 17 | **Портреты (бета):** модульная код-сборка — 5 силуэтов × 3 глаза × 3 носа × 3 рта × 5 аксессуаров (спека §11); цвета глаз добавляет Макс. **Альфа: 1 универсальный цельный портрет** [решение Макса 2026-09-27] |

## 1. Палитра

| Токен | HEX | Роль в ассетах |
|---|---|---|
| accent/taxi | #FFD44D | корпус, чекеры, шашка, полосы |
| accent/taxi-shade | #D9A82C | полутон жёлтого (правая сторона) |
| hull/dark | #1A2033 | корпус-база, плавники, детали |
| hull/darker | #10141F | тёмный тон корпуса (2-й тон), чекер-клетки |
| checker/black | #0B0E1A | чёрные клетки чекер-ленты |
| fill (терминал) | #3CFF8E | огни, мелкие акценты — ДОЗИРОВАННО (код UI) |
| light/white | #F2F5FF | белые огни; иконка станции целиком |
| danger | #FF7A5C | НЕ используется в базовых ассетах (код опасности) |

Существа-пассажиры: цвета тел вне палитры UI (см. §5Б), тон — тёплый мультяшный.

## 2. Стиль-якорь (входит в КАЖДЫЙ промпт)

RU: плоская векторная иллюстрация, жирные пухлые формы, простой чистый силуэт,
≤5 цветов на ассет, 2 тона (база + полутон-полумесяц справа), мультяшное
настроение, без контуров, без градиентов/шумов/текста, БЕЗ падающих теней,
изолированный объект, прозрачный фон (кроме портретов-«удостоверений»).

EN (в промпт):
```
2D game asset, flat vector illustration, bold chunky shapes, simple clean
silhouette, limited palette, two-tone cel shading (flat base color plus a
darker crescent shade on the right side), no drop shadows, no cast shadows,
no outlines, no gradients, no texture noise, no text or numbers,
cartoon-friendly mood, isolated object on transparent background, crisp edges
```

Запрещено: фотореализм, 3D-рендер, пиксель-арт, тонкие линии, текст,
вотермарки, ПАДАЮЩИЕ/ОТБРАСЫВАЕМЫЕ тени. Полутон справа — норм (2 тона).

## 3. Технические форматы

| Ассет | Холст | Экспорт | В Unity |
|---|---|---|---|
| Корабль (корпус) | 512×512, корабль вертикально ~448px, запас полей | RGBA PNG | PPU 32 → 14 юнитов |
| Шашка-оверлей | 128×128 | RGBA PNG | чайлдом к корпусу |
| Портреты ×10 | 512×512 | RGBA PNG | использование 96/48px |
| Иконка станции | 128×128 | RGBA PNG | как `ui_icon_*`, тонирование |

Пост-обработка после генерации (скриптом): проверка альфы, трим + паддинг до
холста, снап палитры к HEX при расползании, даунскейл-тест читаемости.

## 4. Конструктор кораблей («заложить семейство»)

Каждый корабль игры = стиль-якорь + заполненные слоты. Базовая модель (аренда,
альфа) — эталон семьи.

### Слои (рисуются в одном спрайте, кроме шашки)

| Слой | Что это | База (альфа) |
|---|---|---|
| 1. Корпус | силуэт класса, тёмный графит + тень | ретро-ракета |
| 2. Акцент-полоса | цветная панель — ГЛАВНЫЙ меняющийся цвет | жёлтый #FFD44D |
| 3. Чекер-лента | 2 ряда клеток жёлтый/чёрный — код корпорации | есть всегда |
| 4. Шашка | светящийся значок — МАРКЕР АРЕНДЫ | отдельный спрайт |
| 5. Огни | зелёные точки/белые — дозированно | пара точек |

### Слоты будущих моделей (та же формула промпта)

| Слот | Значения |
|---|---|
| СИЛУЭТ | **капсула-«яйцо» (база)** / ракета (покупаемые, Т1/Т2 в резерве) / спидер / грузовой / гоночный |
| ЦВЕТ АКЦЕНТА | жёлтый (база) / зелёный / красный / голубой / фиолетовый |
| ПЛАВНИКИ | классические / широкие-хвостовые / отсутствуют |
| НОС | пуля / конус / тарелка-сканер |
| ШАШКА | есть (аренда) / нет (свой корабль) |
| ДОП | антенна, грузовой отсек, ранцы, второй ряд сопел |

Перекраска в рантайме не планируется; если понадобится — акцент-полоса
выносится в отдельный спрайт-слой (решение беты+).

> **Полная линейка 8 кораблей** (роли, силуэты, тиры «видимой цены», зоны
> перекраски, порядок генерации) — `14_SHIP_LINEUP.md`.

## 5. Реестр ассетов альфы

**А. Корабль-такси (базовая модель, аренда)** — капсула-«яйцо» (§0 п.11).
`Assets/Sprites/ship_taxi.png` + `ship_shashka.png`. Нос строго вверх.
Читаемость при ~30px на чёрном.

**Б. Портрет пассажира — 1 универсальный** [2026-09-27: было 5×2, упрощено]
— `Assets/Sprites/Passengers/pass_generic.png`. Бюст на ПРОЗРАЧНОМ фоне,
рамку-«удостоверение» рисует UI (плашка #08140C + рамка #3CFF8E, слоты 96/48 —
задача UI-дизайнеру). Тело — светлое нейтральное: программист может давать
разнообразие тонированием `SpriteRenderer.color` (как заглушки планет).
Архетип подаётся текстом (имя + реплика). Модульная система беты — §11.

**В. Станции на планетах** [УТВЕРЖДЕНО 2026-09-27: В2] — планеты со станцией
получают собственные полнокрасочные текстуры: шашечная лента по экватору
+ купол-док (код корпорации, как у хаб-планеты из `02_STORY.md` §9). Мастер +
варианты под цвет конкретных планет (станций в альфе 2–3). Рендер:
`BodyVisuals.Sprite` в `WorldBuilder.ApplyVisuals` — код уже поддерживает,
tint НЕ включать. Детали — §9.

## 6. Чек-лист приёмки ассета

- [ ] Альфа-канал есть, фон прозрачный (нет белого/чёрного бокса)
- [ ] Ориентация: нос вверх (корабль) / фронтально (портреты) / центр (иконки)
- [ ] Даунскейл до 32px (корабль) / 48px (портрет) / 24px (иконка) — читается
- [ ] Палитра в пределах §1 (снап при дрейфе)
- [ ] Без текста, контуров, градиентов, шума
- [ ] Трим + паддинг до холста §3, размер файла вменяемый

## 7. Тестовый батч генераторов [в работе]

Цель: сравнить Recraft и gpt-image на самом сложном ассете (корабль) + иконке,
выбрать генератор. Промпты — §7.1; результаты и вердикт пишем сюда же.

### 7.1 Промпты теста

**Т1. Корабль — ретро-ракета (базовая модель):**
```
A cute cartoon space taxi rocket, vertical orientation, nose pointing straight
up. Bullet-shaped hull in warm yellow #FFD44D with darker yellow #D9A82C shade
on the right side, two small dark graphite fins #1A2033, one round porthole
window, a horizontal two-row black-and-yellow checker band (#FFD44D and
#0B0E1A) around the midsection, tiny green light dots #3CFF8E on the fins,
dark engine nozzle at the bottom without flame. No roof sign.
2D game asset, flat vector illustration, bold chunky shapes, simple clean
silhouette, limited palette of 5 colors, two-tone cel shading, no outlines,
no gradients, no text, isolated on transparent background, crisp edges, full
rocket visible with small margin, centered, square image.
```

**Т2. Корабль — капсула-челнок (альтернатива для сравнения силуэта):**
```
A cute cartoon space taxi shuttle, vertical orientation, nose pointing straight
up. Stout capsule cabin in warm yellow #FFD44D with darker shade #D9A82C on the
right, wide cockpit window, two chunky side booster pods in dark graphite
#1A2033, a two-row black-and-yellow checker band (#FFD44D and #0B0E1A) across
the cabin, single dark engine nozzle at the bottom without flame. No roof sign.
2D game asset, flat vector illustration, bold chunky shapes, simple clean
silhouette, limited palette of 5 colors, two-tone cel shading, no outlines,
no gradients, no text, isolated on transparent background, crisp edges, full
shuttle visible with small margin, centered, square image.
```

**Т3. Иконка станции:**
```
Minimal monochrome white UI icon of an orbital refueling station: one bold
ring with a fuel droplet shape in the center and a short docking pylon below
the ring. Solid flat white #FFFFFF shapes only, single color, no outlines,
no gradients, generous negative space, thick simple forms readable at 24
pixels, game UI icon style, isolated on transparent background, centered,
square image.
```

Настройки: gpt-image — квадрат 1:1, transparent background; Recraft —
стиль flat vector / illustration, экспорт PNG с прозрачностью.

### 7.2 Лог сравнения

| Генератор | Т1 | Т2 | Т3 | Заметки |
|---|---|---|---|---|
| Recraft | — | — | — | не запустился |
| gpt-image | ✅ | ⚠ дубль | ❌ | стиль/HEX/нос/фон ок; Т2 стянут в ракету; Т3 читается как «датчик дыма» + белая пыль |

Вердикт: [УТВЕРЖДЁН 2026-09-27] **gpt-image**. Обязательная пост-обработка:
порог альфы ≥24 (снимает ореолы/пыль, сглаживание краёв сохраняет), трим,
паддинг до холста. Проверено: силуэты ракет читаются на 32px, чекеры
смазываются — допустимо. Известный перекос генератора: «космическое такси»
вертикально → тянет к классической ракете; промпты не-ракетных корпусов
строить ОТ ФОРМЫ (яйцо/клин/фургон) + явный запрет ракетных признаков
(no pointed cone nose, no tail fins, no classic rocket).

## 8. Лог отбора ассетов

| Ассет | Вариант | Источник | Вердикт |
|---|---|---|---|
| Ракета | Т1 `01a0e217` | gpt-image | резерв (покупаемые корабли), почищен → `analysis/t1_clean.png` |
| Ракета | Т2 `45c83748` | gpt-image | резерв (покупаемые корабли), почищен → `analysis/t2_clean.png` |
| Иконка станции | Т3 `01a0e219` | gpt-image | ОТБРАКОВАН: концепт нечитаем («датчик дыма») + белая пыль фона |
| Шашка аренды | код (PIL) | — | ✅ ГОТОВО → `Assets/Sprites/ship_shashka.png` |
| «Стрела» Т1/Т2 | `01a0e217`, `45c83748` | gpt-image | почищены, трим+паддинг 512 (контент 448px) → `_candidates/reserve/strela_t1/t2.png`; реальные контуры стройные — прежняя ширина была ореолом |

## 9. Планеты-локации: хаб и заправочные сети [ПЕРЕСМОТР 2026-09-27]

Прежняя схема «маркер станции = чекер-лента по экватору» ОТМЕНЕНА. Новая:

### 9.1. Хаб МТО [v6 2026-09-27]

Гигантская искусственная станция-сфера со СТРОГО круглым силуэтом — из тела
НИЧЕГО не торчит (посадка с любой стороны, §0 п.16а): жёлтый корпус #FFD44D
с панельными швами; тёмная траншея с док-нишами; 3 крупных вент-диска +
2 больших плоских люка (все заподлицо); КРУПНЫЕ буквы «MTO» по центру.
Техничность — только поверхностными формами. Использование: меню / загрузка /
брифинг + тело уровня (`BodyVisuals.Sprite`).

### 9.2. Заправочные сети — 4 компании-тира (§0 п.16)

Цена топлива читается вайбом планеты; цвет сети зарезервировать за заправками
(левел-дизайн: обычным планетам эти оттенки не давать).

| Тир | Сеть (вайб) | Цвет сети | Читаемые признаки |
|---|---|---|---|
| 1 | «Ржавая»: дешёвый бензин, почти пусто | ржаво-рыжий | ржавые пятна, кривая старая колонка, треснувшие сферы, потёки |
| 2 | **«Простая»: без шика — АЛЬФА** | серо-стальной | чистая утилитарная планета, 2 больших белых бака, толстая труба, крупный диск-колонка с большой каплей-брендом; всё заподлицо, только крупные формы |
| 3 | «Аккуратная»: чуть выше среднего | свежий бирюзовый | свежая краска, ровные ряды глянцевых баков, светящаяся табличка, подсветка площадок |
| 4 | «Люкс»: дорого, всем рады | бело-золотой | белый корпус с золотой отделкой, хрустальный навес над VIP-площадками, золотые огни |

Маркер «здесь заправка» = силуэт топливного комплекса (сферы-баки + колонка)
на ближней стороне + цвет сети; на миникарте — цвет сети. Альфа: одна
текстура Т-2 на все станции уровня (одна сеть — лорно и практично).

## 10. Промпты тиража (gpt-image, после отбора генератора)

Анти-дрейф для не-ракетных корпусов: промпт строится ОТ ФОРМЫ + явные запреты
ракетных признаков (§7.2).

### Ш. Шашка аренды [СДЕЛАНО 2026-09-27 — рисована кодом, НЕ генерация]

Геометрический ассет → PIL с точными HEX: `Assets/Sprites/ship_shashka.png`,
128×128, плита 72×40 (4×2 клетки по 16px, #FFD44D/#0B0E1A), скругление 8px,
светлый рим #FFE98A. В Unity: PPU 32 → плита 2.25×1.25 юнита, крепится
чайлдом к верхушке корпуса арендованного корабля.

### П1. Базовый корабль — капсула-«яйцо» [v3 ФИНАЛ 2026-09-27: полутон
возвращён, запрет только падающих теней; табличка-бланк] (генерить 4 варианта)

```
A cute cartoon space taxi pod, vertical orientation, top pointing straight up.
Egg-shaped capsule hull, nearly as wide as it is tall, with a blunt rounded top.
Hull in warm yellow #FFD44D with a darker yellow #D9A82C crescent shade on the
right side. A wide dark cockpit window band across the upper body. A horizontal
two-row black-and-yellow checker band (#FFD44D and #0B0E1A) around the widest
part of the hull. Below the checker band: one small rounded blank license plate
in off-white #F2F5FF with a dark border, completely empty, no characters on it.
Two small dark graphite #1A2033 side thruster pods attached left and right at
the middle. One short dark engine nozzle at the very bottom, no flame. It is
NOT a rocket: no pointed nose cone, no tail fins, no classic rocket silhouette.
No roof sign, no text. No drop shadows, no cast shadows, no gradients.
2D game asset, flat vector illustration, bold chunky shapes, simple clean
silhouette, limited palette, two-tone cel shading, no outlines, isolated on
transparent background, crisp edges, full pod visible with margin, centered,
square image.
```

### П2а. Хаб МТО, станция-сфера [v6 2026-09-27: ничего не торчит] (генерить 2–3, экспорт 1024)

```
2D game asset, a huge artificial corporate space station shaped like a giant
sphere, flat vector illustration, bold chunky shapes, only large details,
perfectly round silhouette with nothing sticking out beyond the circle. The
sphere is warm yellow #FFD44D, its surface divided into big flat panels by
thin darker curved seam lines. A dark graphite #1A2033 horizontal equatorial
trench band wraps around the middle with several identical rectangular
docking bays along it. Three large dark circular vents with thick rims and
two big flat rectangular hatches sit on different big panels, all flush with
the surface. Large bold dark letters "MTO" run across the front-center panel
like a corporate brand. Heavy industrial technical look, everything chunky
and readable at small size. Two-tone cel shading (darker crescent on the
right), no outlines, no gradients, no drop shadows, no other text, no tiny
windows, no ships, no antennas, no towers, no moons, no stars, isolated on
transparent background, the sphere fills most of the square canvas, centered,
square image.
```

### П2б. Планета-заправка Т-2 «Простая» [v3 2026-09-27: только крупные
формы, ничего не торчит, без площадок] (АЛЬФА; генерить 2–3, экспорт 512)

```
2D game asset, a whole planet seen from space, flat vector illustration, bold
chunky shapes, only large details, perfectly round silhouette with nothing
sticking out beyond the circle. A plain utility planet in muted steel
grey-blue with a simple smooth surface and a few large faint darker patches.
On the front side, flush with the round surface: a simple fuel station
complex — two big white spherical storage tanks with dark seams side by side,
one thick dark pipe connecting them, and one large round pump station disc
with a big simple fuel droplet emblem on it. Honest working gas-station vibe:
clean, plain, practical, no luxuries. Two-tone cel shading (darker crescent
on the right), no outlines, no gradients, no drop shadows, no text, no
landing pads, no tiny windows, no towers, no antennas, no moons, no rings,
no stars, isolated on transparent background, the planet fills most of the
square canvas, centered, square image.
```

Требования к результату П2а/П2б: диаметр планеты ≈ 90% холста, комплекс
деталей — на ближней (левой светлой) стороне, чтобы не прятался в полутоне.

### П3. Универсальный пассажир [единственный в альфу; 2026-09-27 заменил
П3–П7 архетипные] (генерить 2–3, экспорт 256)

```
2D game character portrait, flat vector illustration, bold chunky shapes,
two-tone cel shading (flat base color plus a darker crescent on the right
side), no outlines, no gradients, no drop shadows, no text, no background.
A friendly generic alien passenger: a round-headed creature with a simple
smooth pale mint-green body, two big white eyes with large dark pupils
looking at the viewer, a small friendly closed-mouth smile, two short chunky
antennae on top of the head, and small rounded shoulders. Frontal bust
close-up, head fills most of the square frame, perfectly centered, facing
the viewer. Cartoon-friendly and neutral. Isolated on transparent background,
crisp edges, square image.
```

Требования: анфас строго; глаза — чистые белые зоны с тёмными зрачками
(перекраска/тонировка); тело светлое (под tint); без аксессуаров.

## 11. Модульная система портретов (БЕТА — спека-черновик)

Альфа-портрет (П3) — той же школы: фронтально, крупные черты, флэт, 2 тона.

### Набор деталей (19 PNG)

| Слой | Кол-во | Варианты |
|---|---|---|
| Силуэт-бюст, лицо ПУСТОЕ, светлый нейтральный тон | 5 | высокий гуманоид / пузатик / слизень / крабоид / капля-блоб |
| Глаза (зрачки тёмные — Макс перекрашивает) | 3 | пара больших круглых / прищуренные миндалины / циклоп |
| Нос | 3 | точки-ноздри / крупный овал / клюв-треугольник |
| Рот | 3 | улыбка-дуга / прямая линия / открытый «о» |
| Аксессуар | 5 | кепка МТО / панама / наушники / тёплый шарф / цветок |

### Правила

- Все PNG прозрачные, холст 256×256, один стиль-якорь, полутон справа
- Единая сетка гнёзд: глаза ~35% высоты лица, нос ~50%, рот ~65%
- Сборка кодом: силуэт + глаза + нос + рот + аксессуар (можно весами)
- Внешность НЕ кодирует архетип (архетип — текст: имя/реплика), рандом не
  спорит с характером
- Диспетчер МТО = ФИКСИРОВАННАЯ сборка (лицо корпорации в монологах
  бортового компьютера, бета): тело-гуманоид + кепка МТО + циклоп? —
  зафиксировать при сборке
- Тонирование `SpriteRenderer.color` поверх сборки — как в альфе

Иконка станции отменена (§0 п.10): станции видны через облик самой планеты.

| # | Вариант | Плюсы | Минусы |
|---|---|---|---|
| В1 | Орбитальное кольцо со станцией — спрайт-оверлей поверх любой планеты | одна текстура на все станции; цвета планет сохраняются; на планете/карте/миникарте видно автоматически (планеты рисуются собой) | нужен чайлд-спрайт у планет со станцией (задача программисту) |
| В2 | Отдельная текстура «планета со станцией» (индустриальная, купола) | буквально «другая текстурка», замена спрайта | станции одинаковы между собой / теряют цвет планеты (или плодим текстуру под каждый цвет); детали не читаются на миникарте |
| В3 | Кольцо кодом (обводка, как жёлтое кольцо цели) | ноль арта | выглядит бедно; путается с кодом цели заказа |

Рекомендация художника: **В1** — кольцо несёт маленький шашечный паттерн
(фирменный код корпорации), читается на любом масштабе, живёт во всех
отображениях планеты без отдельных иконок.
