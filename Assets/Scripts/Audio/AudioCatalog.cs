using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceTaxi.Audio
{
    // Событие → клип(ы) и параметры микса
    [Serializable]
    public sealed class SoundEntry
    {
        public SoundId Id;

        [Tooltip("Вариации: у Collision — 3 по возрастанию силы, у остальных — рандом")]
        public AudioClip[] Clips = Array.Empty<AudioClip>();

        public AudioGroup Group = AudioGroup.Sfx;

        [Range(0f, 1f)] public float Volume = 1f;   // база микса события

        [Range(0.25f, 4f)] public float Pitch = 1f; // базовый тон (эмбиент занижен)

        [Range(0f, 0.25f)] public float PitchJitter = 0.04f; // разброс тона one-shot, «живость»

        public bool Loop;       // непрерывный звук (двигатель, жар, эмбиент)
        public bool Sequential; // вариации по порядку (сила удара), иначе случайные

        public bool IsValid => Clips != null && Clips.Length > 0;
    }

    // Каталог звуков: соответствие «событие → ассет»
    [CreateAssetMenu(fileName = "AudioCatalog", menuName = "Space Taxi/Audio Catalog")]
    public sealed class AudioCatalog : ScriptableObject
    {
        [SerializeField] private SoundEntry[] entries = Array.Empty<SoundEntry>();

        private Dictionary<SoundId, SoundEntry> map;

        public SoundEntry Get(SoundId id)
        {
            if (map == null)
            {
                map = new Dictionary<SoundId, SoundEntry>(entries.Length);
                foreach (SoundEntry e in entries)
                    if (e != null && !map.ContainsKey(e.Id))
                        map.Add(e.Id, e);
            }
            return map.TryGetValue(id, out SoundEntry entry) ? entry : null;
        }

        // обход записей группы в порядке каталога (плейлист плеера П9.3);
        // без дублей по Id
        public List<SoundId> IdsOfGroup(AudioGroup group)
        {
            var ids = new List<SoundId>();
            foreach (SoundEntry e in entries)
                if (e != null && e.Group == group && !ids.Contains(e.Id))
                    ids.Add(e.Id);
            return ids;
        }
    }
}
