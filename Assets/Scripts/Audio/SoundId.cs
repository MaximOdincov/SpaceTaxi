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
