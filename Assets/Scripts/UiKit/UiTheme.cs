using UnityEngine;

namespace UiKit
{
    // Палитра UI: роли цветов (текст/акцент/фон), а не названия экранов.
    // Значения подобраны под зелёно-янтарный стиль HUD игры
    public static class UiTheme
    {
        public static readonly Color Ink = new Color(0.357f, 0.788f, 0.561f);     // приглушённый текст/лейблы
        public static readonly Color Bright = new Color(0.659f, 1f, 0.796f);      // яркий текст/значения
        public static readonly Color Accent = new Color(1f, 0.831f, 0.302f);      // акцент (главное действие)
        public static readonly Color Positive = new Color(0.357f, 0.9f, 0.56f);   // успех
        public static readonly Color Danger = new Color(1f, 0.32f, 0.24f);        // провал/тревога
        public static readonly Color DangerDark = new Color(0.05f, 0.02f, 0.03f); // фон тревожной панели
        public static readonly Color Calm = new Color(0.86f, 0.46f, 0.42f);       // «спокойный» тревожный (долг по графику)
        public static readonly Color Panel = new Color(0.031f, 0.078f, 0.047f, 0.92f);   // фон панели
        public static readonly Color Button = new Color(0.06f, 0.2f, 0.13f, 0.9f);       // фон кнопки
        public static readonly Color OnAccent = Color.black;                             // текст на акцентной кнопке
        public static readonly Color Frame = new Color(0.27f, 0.54f, 0.39f, 0.9f);       // рамка карточки/аватара

        // слабый светлый контур кнопки — силуэт читается, с текстом не спорит
        public static Color ButtonOutline => new Color(0.55f, 0.85f, 0.7f, 0.55f);

        // разделитель секций (Ink с прозрачностью)
        public static Color Divider => new Color(Ink.r, Ink.g, Ink.b, 0.4f);
    }
}
