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
        LowHull,          // пульс «корпус повреждён» (последние 25% HP)
        BlackHoleHum,     // гул притяжения дыры, громче к горизонту
        BlackHoleSwallow, // гибель на горизонте — «провал»
        BeltRumble,       // стуки пояса астероидов
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
