namespace SpaceTaxi.Audio
{
    // Идентификаторы звуковых событий
    public enum SoundId
    {
        None = 0,

        // непрерывные (loop)
        EngineMain,     // основной гул
        EngineRetro,    // ретро-сопла
        EngineStrafe,   // маневровые, панорама L/R
        HeatZone,       // жаровая зона звезды

        // разовые (one-shot)
        Collision,      // 3 вариации по силе
        LandingTouch,
        Takeoff,
        Explosion,      // с приглушением остальных
        Respawn,

        // интерфейс
        UiClick,
        ComputerPing,

        // фон
        AmbientSpace,   // тихий слой под музыкой

        // музыка
        MusicMenu,
        MusicFlight,

        // состояния и опасности (бета) — добавляются В КОНЕЦ enum: вставка до
        // музыки сдвинула бы числовые значения сериализованных полей
        // (SceneMusic.music в сценах, SoundEntry.Id в каталоге) и сломала бы их
        LowFuel,          // сигнал «топливо кончается» (пороги 25%/10%)
        Alarm,            // луп тревоги HUD (жар/пустой бак)
        MusicStation1,    // радиостанции: треки раскладываются файлами
        MusicStation2,    // music_station1_*.ogg ... music_station4_*.ogg
        MusicStation3,
        MusicStation4,
        LowHull,          // пульс «корпус повреждён» (последние 25% HP)
        BlackHoleHum,     // гул притяжения дыры, громче к горизонту
        BlackHoleSwallow, // затянуло за горизонт (Н-10) — «провал», гибели на горизонте больше нет
        BeltRumble,       // стуки пояса астероидов

        // заказы и экономика (П9.3); «новый заказ» — реюз ComputerPing,
        // отдельный OrderPing не заводился
        BoardBip,       // пассажир сел в такси
        DeliveryChime,  // доставка выполнена — аккорд
        TaskSuccess,    // задание пассажира выполнено
        TaskFail,       // задание провалено — жалоба
        PurchaseCash,   // покупка корабля/улучшения — касса
        DebtTone,       // долг: авария (рост) и досрочное погашение
        DismissalTone,  // увольнение — три аварии
    }

    // группы громкости для настроек
    public enum AudioGroup
    {
        Engine, // двигатели
        Sfx,    // эффекты
        Ui,     // интерфейс
        Music,  // музыка и эмбиент
    }
}
