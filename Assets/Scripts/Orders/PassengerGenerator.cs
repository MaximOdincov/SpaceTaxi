using UnityEngine;

namespace SpaceTaxi.Orders
{
    // Генератор пассажиров (П5.2): свежий профиль на каждый заказ из пулов П5.1.
    // Профили — runtime-инстансы (CreateInstance), живут со сценой, Destroy не нужен.
    public static class PassengerGenerator
    {
        // Спокойные тёплые/холодные тона — визуальное разнообразие портретов-заглушек до арта А5
        private static readonly Color32[] PortraitTints =
        {
            new Color32(0xE8, 0xB2, 0x7D, 0xFF), // песочный
            new Color32(0xD9, 0x8C, 0x6B, 0xFF), // терракота
            new Color32(0xC9, 0x6B, 0x7E, 0xFF), // пыльная роза
            new Color32(0xE6, 0xC8, 0x6E, 0xFF), // тёплый жёлтый
            new Color32(0x7D, 0xA9, 0xD9, 0xFF), // небесно-голубой
            new Color32(0x7D, 0xC9, 0xB4, 0xFF), // мятный
            new Color32(0xA0, 0x8C, 0xD9, 0xFF), // лавандовый
            new Color32(0x8F, 0xA3, 0xB8, 0xFF), // серо-голубой
        };

        // null-пулы → null (фолбэк на старый каталог — на стороне вызывающего)
        public static PassengerProfile Create(PassengerPools pools)
        {
            if (pools == null)
                return null;

            var profile = ScriptableObject.CreateInstance<PassengerProfile>();
            profile.Description = string.Empty;
            profile.PortraitTint = PortraitTints[UnityEngine.Random.Range(0, PortraitTints.Length)];

            string fullName = BuildName(pools);
            if (!string.IsNullOrEmpty(fullName))
                profile.DisplayName = fullName;

            var archetype = PickArchetype(pools);
            if (archetype == null)
                return profile; // останется дефолт «Турист» без реплик — UI терпит пустые массивы

            profile.Archetype = archetype.Archetype;
            profile.Greetings = Copy(archetype.Greetings);
            profile.Wishes = Copy(archetype.Wishes);
            profile.Thanks = Copy(archetype.Thanks);
            profile.Complaints = Copy(archetype.Complaints);
            return profile;
        }

        // «Имя Фамилия»; пустые списки — оставляем дефолтное DisplayName профиля
        private static string BuildName(PassengerPools pools)
        {
            string name = pools.PickName();
            string surname = pools.PickSurname();
            if (string.IsNullOrEmpty(name))
                return surname;
            if (string.IsNullOrEmpty(surname))
                return name;
            return name + " " + surname;
        }

        private static PassengerPools.ArchetypePool PickArchetype(PassengerPools pools)
        {
            var list = pools.Archetypes;
            if (list == null || list.Count == 0)
                return null;
            int start = UnityEngine.Random.Range(0, list.Count);
            // пропуск пустых слотов, если ассет заполнен криво
            for (int i = 0; i < list.Count; i++)
            {
                var pool = list[(start + i) % list.Count];
                if (pool != null)
                    return pool;
            }
            return null;
        }

        // полные копии массивов: реплика выбирается случайно при показе (PickPhrase)
        private static string[] Copy(string[] source)
        {
            return source != null ? (string[])source.Clone() : null;
        }
    }
}
