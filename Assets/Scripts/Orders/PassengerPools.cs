using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceTaxi.Orders
{
    // Пулы данных генератора пассажиров (П5.2): имена, фамилии, реплики по архетипам
    [CreateAssetMenu(fileName = "PassengerPools", menuName = "Space Taxi/Passenger Pools")]
    public sealed class PassengerPools : ScriptableObject
    {
        [Serializable]
        public sealed class ArchetypePool
        {
            [Tooltip("Архетип: Торопыга, Барыга, Турист, Тихоня, Чудик")]
            public string Archetype;

            [Header("Фразы (покажется случайная из списка)")]
            [TextArea(2, 4)]
            [Tooltip("Пассажир сел на борт")]
            public string[] Greetings;

            [TextArea(2, 4)]
            [Tooltip("«Пожелание» — текст задания, видно в карточке заказа до принятия")]
            public string[] Wishes;

            [TextArea(2, 4)]
            [Tooltip("Доставлен на место")]
            public string[] Thanks;

            [TextArea(2, 4)]
            [Tooltip("Заказ провален")]
            public string[] Complaints;
        }

        [Tooltip("Имена пассажиров")]
        public string[] Names;

        [Tooltip("Фамилии пассажиров")]
        public string[] Surnames;

        [Tooltip("Ровно 5 архетипов, порядок как в бета-плане §3.6")]
        public List<ArchetypePool> Archetypes = new List<ArchetypePool>();

        // Пул архетипа по имени; null, если такого нет
        public ArchetypePool Get(string archetype)
        {
            if (string.IsNullOrEmpty(archetype) || Archetypes == null)
                return null;
            for (int i = 0; i < Archetypes.Count; i++)
            {
                var pool = Archetypes[i];
                if (pool != null && pool.Archetype == archetype)
                    return pool;
            }
            return null;
        }

        public string PickName()
        {
            return Names != null && Names.Length > 0
                ? Names[UnityEngine.Random.Range(0, Names.Length)]
                : null;
        }

        public string PickSurname()
        {
            return Surnames != null && Surnames.Length > 0
                ? Surnames[UnityEngine.Random.Range(0, Surnames.Length)]
                : null;
        }
    }
}
