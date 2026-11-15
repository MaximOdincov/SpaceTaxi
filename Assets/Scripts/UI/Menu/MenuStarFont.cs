using System.Collections.Generic;

namespace SpaceTaxi.UI
{
    // Пиксельные маски 5x7 для звёздного текста (бриф E3): каждая буква —
    // 7 строк по 5 символов, 'X' — ячейка, в которой ставится звезда.
    // Набор — только то, что реально встречается в меню: латиница названия
    // (SPACE TAXI) и кириллица актуальных кнопок (НОВАЯ ИГРА / ПРОДОЛЖИТЬ /
    // НАСТРОЙКИ / ВЫХОД). Одинаковые по форме буквы латиницы и кириллицы
    // (A/А, B/В, C/С, E, H/Н, K/К, O/О, P/Р, T/Т, X/Х) ссылаются на одну маску.
    public static class MenuStarFont
    {
        private const string A = ".XXX.|X...X|X...X|XXXXX|X...X|X...X|X...X";
        private const string B = "XXXX.|X...X|X...X|XXXX.|X...X|X...X|XXXX.";
        private const string C = ".XXXX|X...X|X....|X....|X....|X...X|.XXXX";
        private const string E = "XXXXX|X....|X....|XXXX.|X....|X....|XXXXX";
        private const string I = "XXXXX|..X..|..X..|..X..|..X..|..X..|XXXXX";
        private const string P = "XXXX.|X...X|X...X|XXXX.|X....|X....|X....";
        private const string S = ".XXXX|X....|X....|.XXX.|....X|....X|XXXX.";
        private const string T = "XXXXX|..X..|..X..|..X..|..X..|..X..|..X..";
        private const string X = "X...X|X...X|.X.X.|..X..|.X.X.|X...X|X...X";
        private const string Gcyr = "XXXXX|X....|X....|X....|X....|X....|X....";     // Г
        private const string Dcyr = ".XXXX|.X..X|.X..X|.X..X|.X..X|XXXXX|X...X"; // Д
        private const string Icyr = "X...X|X...X|X..XX|X.X.X|XX..X|X...X|X...X"; // И
        private const string Jcyr = ".X.X.|X...X|X..XX|X.X.X|XX..X|X...X|X...X"; // Й
        private const string Kcyr = "X...X|X..X.|X.X..|XX...|X.X..|X..X.|X...X"; // К
        private const string Lcyr = ".XXXX|X..XX|X...X|X...X|X...X|X...X|X...X"; // Л
        private const string Hcyr = "X...X|X...X|X...X|XXXXX|X...X|X...X|X...X"; // Н
        private const string Ocyr = ".XXX.|X...X|X...X|X...X|X...X|X...X|.XXX."; // О
        private const string Pcyr = "XXXXX|X...X|X...X|X...X|X...X|X...X|X...X"; // П
        // Ы: обе вертикали во всю высоту + ДВУХРЯДНАЯ нижняя перекладина
        // (2026-11-15: однорядная перекладина из мерцающих точек терялась —
        // «ВЫХОД» читалось «ВИХОД»; двойной бар читается однозначно)
        private const string Ycyr = "X...X|X...X|X...X|X...X|X...X|XXXXX|XXXXX"; // Ы
        private const string SoftSign = "X....|X....|X....|XXXX.|X...X|X...X|XXXX."; // Ь
        private const string Yacyr = ".XXXX|X...X|X...X|.XXXX|..X.X|.X..X|X...X"; // Я
        private const string Zhcyr = "X.X.X|X.X.X|X.X.X|XXXXX|X.X.X|X.X.X|X.X.X"; // Ж

        private static readonly Dictionary<char, string[]> Glyphs = new()
        {
            ['A'] = Rows(A), ['А'] = Rows(A),
            ['B'] = Rows(B), ['В'] = Rows(B),
            ['C'] = Rows(C), ['С'] = Rows(C),
            ['E'] = Rows(E),
            ['H'] = Rows(Hcyr), ['Н'] = Rows(Hcyr),
            ['I'] = Rows(I),
            ['K'] = Rows(Kcyr), ['К'] = Rows(Kcyr),
            ['O'] = Rows(Ocyr), ['О'] = Rows(Ocyr),
            ['P'] = Rows(P), ['Р'] = Rows(P),
            ['S'] = Rows(S),
            ['T'] = Rows(T), ['Т'] = Rows(T),
            ['X'] = Rows(X), ['Х'] = Rows(X),
            ['Г'] = Rows(Gcyr),
            ['Д'] = Rows(Dcyr),
            ['Ж'] = Rows(Zhcyr),
            ['И'] = Rows(Icyr),
            ['Й'] = Rows(Jcyr),
            ['Л'] = Rows(Lcyr),
            ['П'] = Rows(Pcyr),
            ['Ы'] = Rows(Ycyr),
            ['Ь'] = Rows(SoftSign),
            ['Я'] = Rows(Yacyr),
        };

        // Маска буквы без учёта регистра; буквы, которой нет в наборе,
        // соответствует false (вызывающий код отводит под неё пустую клетку)
        public static bool TryGet(char character, out string[] rows) =>
            Glyphs.TryGetValue(char.ToUpperInvariant(character), out rows);

        private static string[] Rows(string compact) => compact.Split('|');
    }
}
