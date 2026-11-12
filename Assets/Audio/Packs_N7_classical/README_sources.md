# N7 A7.3a — ВОЛНА 3 «классическая станция» (MusicStation3): стагинг треков

Спокойная классика, public domain / CC0 записи, формат `.ogg` (Vorbis) как есть,
без конвертации. Суммарно 12 файлов, ~36.3 МБ, ~56 минут.

**Итоговое место** (переносит оркестратор после приёмки M): `Assets/Audio/Packs_N7_classical/`.
Там же в `Assets/ScriptableObjects/AudioCatalog.asset` создаётся запись `MusicStation3`
(Group = Music (3), Loop = 0, Volume ~0.5 — по образцу записи станции 2), клипы — по GUID
перенесённых файлов в порядке нумерации ниже.

Проверка каждого файла: магические байты `OggS`, идент-заголовок vorbis, последняя
Ogg-страница с флагом EOS, размер байт-в-байт совпадает с метаданными archive.org.
Запускать/конвертировать ничего не запускалось (ffmpeg в системе отсутствует).

## Треки

| # | Файл | Произведение | Композитор | Длит. | Размер | Источник (item) | Лицензия |
|---|------|--------------|------------|-------|--------|-----------------|----------|
| 01 | `01_chopin_nocturne_op9_no2.ogg` | Ноктюрн Op. 9 № 2 (E-flat major) | Ф. Шопен | 4:37 | 2.7 МБ | https://archive.org/details/musopen-chopin | CC0 1.0 |
| 02 | `02_chopin_nocturne_op32_no1.ogg` | Ноктюрн Op. 32 № 1 (B major) | Ф. Шопен | 5:01 | 3.5 МБ | https://archive.org/details/musopen-chopin | CC0 1.0 |
| 03 | `03_chopin_nocturne_op55_no1.ogg` | Ноктюрн Op. 55 № 1 (F minor) | Ф. Шопен | 5:17 | 2.7 МБ | https://archive.org/details/musopen-chopin | CC0 1.0 |
| 04 | `04_chopin_nocturne_b49_lento.ogg` | Ноктюрн B. 49 (C-sharp minor, «Lento con gran espressione») | Ф. Шопен | 3:59 | 2.2 МБ | https://archive.org/details/musopen-chopin | CC0 1.0 |
| 05 | `05_satie_gymnopedie1.ogg` | Гимнопедия № 1 | Э. Сати | 8:52 | 5.4 МБ | https://archive.org/details/clasiciininterpretarimoderne | CC0 1.0 |
| 06 | `06_ravel_pavane_pour_une_infante_defunte.ogg` | Павана на смерть инфанты | М. Равель | 5:36 | 3.0 МБ | https://archive.org/details/clasiciininterpretarimoderne | CC0 1.0 |
| 07 | `07_bach_goldberg_aria.ogg` | Ария (Гольдберг-вариации BWV 988) | И. С. Бах | 4:54 | 3.0 МБ | https://archive.org/details/The_Open_Goldberg_Variations-11823 | CC0 1.0 |
| 08 | `08_bach_goldberg_variatio25.ogg` | Вариация 25, a 2 Clav. (Гольдберг-вариации BWV 988) | И. С. Бах | 9:13 | 5.5 МБ | https://archive.org/details/The_Open_Goldberg_Variations-11823 | CC0 1.0 |
| 09 | `09_bach_goldberg_aria_da_capo.ogg` | Ария da Capo (Гольдберг-вариации BWV 988) | И. С. Бах | 2:45 | 1.7 МБ | https://archive.org/details/The_Open_Goldberg_Variations-11823 | CC0 1.0 |
| 10 | `10_bach_wtc1_prelude_c_major.ogg` | Прелюдия № 1 до мажор (ХТК I, BWV 846) | И. С. Бах | 2:21 | 1.4 МБ | https://archive.org/details/BachWellTemperedKlavierBookI-PreludeInCMajor | CC Public Domain 1.0 |
| 11 | `11_bach_jesu_joy_of_mans_desiring.ogg` | Иисус, радость моя (BWV 147, хорал) | И. С. Бах | 2:53 | 1.8 МБ | https://archive.org/details/JesuJoyOfMansDesiring | CC Public Domain 1.0 |
| 12 | `12_vivaldi_mandolin_concerto_rv425.ogg` | Концерт для мандолины до мажор RV 425 (все части) | А. Вивальди | 7:43 | 5.2 МБ | https://archive.org/details/091729VivaldiMandolineConcertoInCRV425 | Public Domain Mark 1.0 |

## Исполнители и основания лицензий

- **musopen-chopin** (01–04): проект Musopen (метаданные item-а: creator «Aaron Dunn»,
  uploader aaron@musopen.org). Основание: item явно помечен
  `http://creativecommons.org/publicdomain/zero/1.0/`; записи записаны и выпущены
  Musopen как public domain (проект записи и освобождения произведений).
  Конкретный пианист в метаданных item-а по трекам не указан.
- **clasiciininterpretarimoderne** (05, 06): исполнитель в метаданных не указан
  (creator пустой). Основание: item явно помечен CC0 1.0
  (`http://creativecommons.org/publicdomain/zero/1.0/`). Это сборник
  «классика в современных трактовках»: трек 05 заметно медленнее «академических»
  исполнений (8:52 против обычных ~3:30) — по характеру подходит станции.
- **The_Open_Goldberg_Variations-11823** (07–09): Kimiko Ishizaka, фортепиано —
  проект «Open Goldberg Variations» (opengoldbergvariations.org), записан специально
  для выпуска в public domain. Основание: item помечен CC0 1.0, creator
  «Kimiko Ishizaka».
- **BachWellTemperedKlavierBookI-PreludeInCMajor** (10): Sam Tennison (creator).
  Основание: item помечен `http://creativecommons.org/licenses/publicdomain/`
  (CC Public Domain certification).
- **JesuJoyOfMansDesiring** (11): исполнитель в метаданных не указан
  (creator = композитор). Основание: `http://creativecommons.org/licenses/publicdomain/`.
- **091729VivaldiMandolineConcertoInCRV425** (12): исполнитель в метаданных не указан.
  Основание: Public Domain Mark 1.0
  (`http://creativecommons.org/publicdomain/mark/1.0/`). Трек содержит и быстрые
  части концерта — в целом светлый барочный характер, без перкуссии.

Сами сочинения — public domain (композиторы умерли более 70 лет назад); свобода
записи подтверждена лицензией item-а на archive.org (CC0 / CC PD / PD Mark).

## Чего не нашлось (для докачки владельцем)

- **Дебюсси** (Clair de lune / Reverie / Arabesques) и **Моцарт** (спокойные
  Andante/Adagio): чистых записей с проверяемой PD/CC0-лицензией в ogg на archive.org
  не найдено. Коммерческие исполнения (Клиберн, Брендель, Грюмьо и т.п.) — записи
  под копирайтом, отклонены. Кандидаты из коллекции 78rpm (georgeblood) датированы
  1928–1938 — по Music Modernization Act войдут в PD США только в 2028–2038,
  отклонены. Рабочий путь для докачки: аккаунт musopen.org (там эти произведения
  есть в PD-исполнениях), либо новые релизы Musopen/Open Goldberg.
- «Орган» в миксе представлен хоралом BWV 147 (11) — полноценной спокойной органной
  записи с чистой лицензией не нашлось (Toccata/Fugue — не спокойные, Prelude&Fugue
  BWV 545 — 14:22, вне лимита 12 мин).
