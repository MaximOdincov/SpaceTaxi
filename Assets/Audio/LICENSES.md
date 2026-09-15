# Лицензии и источники аудио

Всё аудио в этой папке — **CC0 (public domain)**, ограничений на использование нет.

## Kenney (kenney.nl) — паки CC0

| Пак | Файлы в проекте |
|---|---|
| Sci-Fi Sounds | sfx_engine_strafe_loop (spaceEngineSmall), sfx_collision_1..2 (impactMetal), sfx_collision_3 (lowFrequency_explosion), ambient_space_loop (spaceEngineLow) |
| Impact Sounds | sfx_landing (impactSoft_heavy) |
| Digital Audio | ui_computer_ping (lowThreeTone) |
| Interface Sounds | ui_click (click_004) |

Источник: https://kenney.nl/assets (Sci-Fi Sounds, Impact Sounds)

## OpenGameArt — rubberduck, паки CC0

| Пак | Файлы в проекте |
|---|---|
| 50 CC0 Sci-Fi SFX (opengameart.org/content/50-cc0-sci-fi-sfx) | sfx_explosion (explosion_01), sfx_takeoff (rocket_01), sfx_respawn (teleport_02) |
| 30 CC0 SFX Loops (opengameart.org/content/30-cc0-sfx-loops) | sfx_heat_loop (alarm_01 — корабельная тревога) |

## Авторский синтез (сгенерировано для проекта, без ограничений)

| Файл | Что это |
|---|---|
| sfx_engine_main_loop.wav | глубокий гул основного двигателя: суббас 55/110 Гц + коричневый шум, дыхание 0.5 Гц, бесшовный луп 4 с |
| sfx_engine_retro_loop.wav | тормозное шипение ретро-сопел: полосной шум с «выдохами», подложка 90 Гц, бесшовный луп 3 с |

## OpenGameArt — треки CC0

| Файл | Оригинал | Автор |
|---|---|---|
| music_menu.ogg | Out There (242 с) | yd — https://opengameart.org/content/space-music-out-there |

## Плейлист-кандидаты (Music/Candidates/, всё CC0) — задел под внутриигровой плеер

theme_01 — основная тема полёта (MusicFlight); theme_11 — бывшая основная («дополнительные»).

| Файл | Оригинал | Автор / страница |
|---|---|---|
| theme_01_magic_space.mp3 | Magic Space (55 с) — светлая sci-fi-тема | CodeManu — opengameart.org/content/magic-space |
| theme_02_happy_adventure.mp3 | Happy Adventure Loop (47 с) | TinyWorlds — opengameart.org/content/happy-adventure-loop |
| theme_03_bossa_8bit.mp3 | 8bit Bossa (60 с) | Joth — opengameart.org/content/bossa-nova |
| theme_04_elevator_lounge.wav | Elevator Music (96 с) | Pro Sensory (Alex McCulloch) — opengameart.org/content/elevator-music (атрибуция желательна) |
| theme_05_retro_soundtrack.ogg | Retro Soundtrack (157 с), сб. Retro Wave | Holizna — opengameart.org/content/retro-wave-collection |
| theme_06_space_ace.mp3 | Space Ace / Logisector Intro (72 с) | Sudocolon — opengameart.org/content/space-ace |
| theme_07_short_theme_metal.wav | Short Theme [Rock/Metal] (56 с) | nene — opengameart.org/content/short-theme-rockmetal |
| theme_08_bach_jig_8bit.ogg | Bach — Fugue BWV 577 «The Jig», 8-bit (170 с) | TheOuterLinux — opengameart.org/content/bach-fugue-bwv-577-the-jig |
| theme_09_cat_in_space.mp3 | Cat in Space (64 с) | mutantleg — opengameart.org/content/cat-in-space |
| theme_10_vape_in_space.ogg | Vape In Space (40 с) | zq — opengameart.org/content/vape-in-space |
| theme_11_dead_ship.ogg | My Very Own Dead Ship (226 с) | yd — opengameart.org/content/background-space-track |

## Примечания

- ambient_space_loop — производный слой: spaceEngineLow с pitch ~0.45 и низкой громкостью
  (настройка в AudioCatalog, не в файле). Можно заменить отдельным CC0-эмбиентом без правок кода.
- Характер звуков подобран по описаниям/именам источников; финальная сверка — на слух в Play Mode,
  замена любого звука = подмена файла или записи в AudioCatalog.
