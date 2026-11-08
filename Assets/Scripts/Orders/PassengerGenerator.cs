using UnityEngine;

namespace SpaceTaxi.Orders
{
    // Генератор пассажиров (П5.2): свежий профиль на каждый заказ из пулов П5.1.
    // Профили — runtime-инстансы (CreateInstance), живут со сценой, Destroy не нужен.
    // Портрет и имя — полный рандом: готовый NPC или сборка из слоёв (NpcPortrait,
    // детерминирован по PortraitSeed — сейв восстанавливает портрет), имя —
    // «прилагательное + существительное» с согласованием родов.
    public static class PassengerGenerator
    {
        // формы прилагательного: мужской | женский | средний
        private static readonly string[] Adjectives =
        {
            "весенний|весенняя|весеннее",
            "крутой|крутая|крутое",
            "ржавый|ржавая|ржавое",
            "звёздный|звёздная|звёздное",
            "медовый|медовая|медовое",
            "жестяной|жестяная|жестяное",
            "пугливый|пугливая|пугливое",
            "упрямый|упрямая|упрямое",
            "хрустящий|хрустящая|хрустящее",
            "торжественный|торжественная|торжественное",
            "сонный|сонная|сонное",
            "электрический|электрическая|электрическое",
            "картофельный|картофельная|картофельное",
            "туманный|туманная|туманное",
            "поющий|поющая|поющее",
            "бодрый|бодрая|бодрое",
            "штурманский|штурманская|штурманское",
            "лунный|лунная|лунное",
            "бархатный|бархатная|бархатное",
            "честный|честная|честное",
            "мыльный|мыльная|мыльное",
            "героический|героическая|героическое",
            "тихий|тихая|тихое",
            "космический|космическая|космическое",
            "дрессированный|дрессированная|дрессированное",
            "бортовой|бортовая|бортовое",
            "рассеянный|рассеянная|рассеянное",
            "важный|важная|важное",
        };

        // существительное | род: m/f/n
        private static readonly string[] Nouns =
        {
            "утка|f",
            "волшебник|m",
            "капитан|m",
            "трактор|m",
            "огурец|m",
            "балерина|f",
            "пингвин|m",
            "телескоп|m",
            "бухгалтер|m",
            "самовар|m",
            "комета|f",
            "морж|m",
            "гармонист|m",
            "чемодан|m",
            "сосиска|f",
            "инженер|m",
            "медуза|f",
            "градусник|m",
            "пианистка|f",
            "карп|m",
            "садовник|m",
            "гвоздь|m",
            "директриса|f",
            "турбина|f",
            "навигатор|m",
            "прокурор|m",
            "кассирша|f",
            "спутник|m",
        };

        // null-пулы → null (фолбэк на старый каталог — на стороне вызывающего)
        public static PassengerProfile Create(PassengerPools pools)
        {
            if (pools == null)
                return null;

            var profile = ScriptableObject.CreateInstance<PassengerProfile>();
            profile.RuntimeInstance = true; // оффер протухнет — профиль отдаст OrderService

            int seed = Random.Range(1, int.MaxValue);
            profile.PortraitSeed = seed;
            profile.Portrait = NpcPortrait.Generate(seed);
            profile.PortraitTint = Color.white; // цвета запечены в портрет
            profile.DisplayName = BuildFunnyName();

            // общие пулы фраз (Н-14): полные копии массивов — реплика выбирается
            // случайно при показе (PickPhrase), офферы не делят состояние
            profile.Greetings = Copy(pools.Greetings);
            profile.Wishes = Copy(pools.Wishes);
            profile.Thanks = Copy(pools.Thanks);
            profile.Complaints = Copy(pools.Complaints);
            return profile;
        }

        // «Весенняя утка», «Крутой волшебник»: прилагательное согласуется с родом
        private static string BuildFunnyName()
        {
            string adjective = Adjectives[Random.Range(0, Adjectives.Length)];
            string noun = Nouns[Random.Range(0, Nouns.Length)];

            string[] nounParts = noun.Split('|');
            string word = nounParts[0];
            int gender = nounParts.Length > 1 && nounParts[1] == "f" ? 1 : 0; // n — средний род

            string[] forms = adjective.Split('|');
            string form = forms[Mathf.Clamp(gender, 0, forms.Length - 1)];
            return char.ToUpper(form[0]) + form.Substring(1) + " " + word;
        }

        // полные копии массивов: реплика выбирается случайно при показе (PickPhrase)
        private static string[] Copy(string[] source)
        {
            return source != null ? (string[])source.Clone() : null;
        }
    }
}
