using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceTaxi.Audio
{
    [DisallowMultipleComponent]
    public sealed class AudioManager : MonoBehaviour
    {
        [SerializeField] private AudioCatalog catalog;

        public static AudioManager Instance { get; private set; }

        private const float LoopVolumeMin = 0.4f;   // на минимуме гул не пропадает полностью
        private const float LoopPitchMin = 0.9f;
        private const float LoopPitchRange = 0.22f;
        private const float LoopFadeTime = 0.15f;   // нарастание/затухание лупа, без щелчков
        private const float MusicFadeTime = 1.5f;   // кроссфейд треков
        private const float DuckFactor = 0.2f;      // приглушение групп при взрыве
        private const float DuckTime = 1.2f;
        private const int MaxOneShotVoices = 12;

        private readonly Dictionary<SoundId, LoopVoice> loops = new Dictionary<SoundId, LoopVoice>();
        private readonly List<AudioSource> oneShotPool = new List<AudioSource>();
        private readonly Dictionary<AudioGroup, float> groupVolumes = new Dictionary<AudioGroup, float>();
        private readonly Dictionary<AudioGroup, float> groupDuck = new Dictionary<AudioGroup, float>();
        private readonly HashSet<SoundId> warnedSilent = new HashSet<SoundId>(); // уже предупредили о пустых клипах
        private AudioSource musicA, musicB; // чередуем для кроссфейда
        private SoundId currentMusic = SoundId.None;
        private int musicFlip;
        private Coroutine duckRoutine;

        private sealed class LoopVoice
        {
            public AudioSource Source;
            public SoundEntry Entry;
            public float Intensity = 1f;
            public float Pan;
            public bool FadingOut;
            public Coroutine Fade;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject); 
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            foreach (AudioGroup g in System.Enum.GetValues(typeof(AudioGroup)))
            {
                groupVolumes[g] = LoadVolume(g);
                groupDuck[g] = 1f;
            }
            // та же квадратичная кривая, что в SetMasterVolume — иначе на старте
            // слайдер показывает одно (sqrt), а слышится другое (линейное)
            float master = LoadVolume("Master");
            AudioListener.volume = master * master;

            musicA = CreateVoice("MusicA");
            musicB = CreateVoice("MusicB");
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        // --- Одноразовые звуки ---

        public void PlayOneShot(SoundId id, float intensity = 1f)
        {
            SoundEntry entry = GetEntry(id);
            if (entry == null || entry.Loop)
                return;

            AudioSource src = GetFreeOneShotVoice();
            src.clip = ChooseClip(entry, intensity);
            src.pitch = entry.Pitch * (1f + Random.Range(-entry.PitchJitter, entry.PitchJitter));
            src.volume = entry.Volume * GroupFactor(entry.Group) * Mathf.Clamp01(intensity);
            src.panStereo = 0f;
            src.loop = false;
            src.Play();

            if (id == SoundId.Explosion)
                DuckAll(); // взрыв «срезает» остальное
        }

        // --- Непрерывные звуки ---

        public void SetLoopActive(SoundId id, bool active)
        {
            if (active)
                StartLoop(id);
            else
                StopLoop(id);
        }

        public void StartLoop(SoundId id)
        {
            SoundEntry entry = GetEntry(id);
            if (entry == null)
                return;

            if (loops.TryGetValue(id, out LoopVoice voice))
            {
                if (voice.FadingOut) // возвращаем из затухания
                {
                    if (voice.Fade != null) StopCoroutine(voice.Fade);
                    voice.FadingOut = false;
                    voice.Fade = StartCoroutine(FadeLoop(voice, 1f));
                }
                return;
            }

            voice = new LoopVoice { Entry = entry, Source = CreateVoice("Loop_" + id) };
            voice.Source.clip = ChooseClip(entry, 1f);
            voice.Source.loop = true;
            voice.Source.volume = 0f;
            voice.Source.pitch = entry.Pitch;
            voice.Source.Play();
            loops.Add(id, voice);
            voice.Fade = StartCoroutine(FadeLoop(voice, 1f));
        }

        public void StopLoop(SoundId id)
        {
            if (!loops.TryGetValue(id, out LoopVoice voice))
                return;
            if (voice.FadingOut)
                return; // затухание уже идёт: пер-кадровые повторы не роняют корутину
            if (voice.Fade != null) StopCoroutine(voice.Fade);
            voice.FadingOut = true;
            voice.Fade = StartCoroutine(FadeLoop(voice, 0f, removeWhenDone: true));
        }

        public void SetLoopIntensity(SoundId id, float intensity)
        {
            if (!loops.TryGetValue(id, out LoopVoice voice) || voice.FadingOut)
                return;
            voice.Intensity = Mathf.Clamp01(intensity);
            ApplyLoopParams(voice);
        }

        public void SetLoopPan(SoundId id, float pan)
        {
            if (!loops.TryGetValue(id, out LoopVoice voice))
                return;
            voice.Pan = Mathf.Clamp(pan, -1f, 1f);
            voice.Source.panStereo = voice.Pan;
        }

        private void ApplyLoopParams(LoopVoice voice)
        {
            float i = voice.Intensity;
            float vol = LoopVolumeMin + (1f - LoopVolumeMin) * i;
            voice.Source.volume = voice.Entry.Volume * GroupFactor(voice.Entry.Group) * vol;
            voice.Source.pitch = voice.Entry.Pitch * (LoopPitchMin + LoopPitchRange * i);
        }

        private IEnumerator FadeLoop(LoopVoice voice, float dir, bool removeWhenDone = false)
        {
            // dir: 1 — до рабочих параметров, 0 — до тишины
            float from = voice.Source.volume;
            float target = dir > 0f ? FullLoopVolume(voice) : 0f;
            for (float t = 0f; t < LoopFadeTime; t += Time.unscaledDeltaTime)
            {
                // цель пересчитывается каждый кадр: SetLoopIntensity мог
                // сменить Intensity в том же кадре — иначе слышимый памп
                if (dir > 0f)
                    target = FullLoopVolume(voice);
                voice.Source.volume = Mathf.Lerp(from, target, t / LoopFadeTime);
                yield return null;
            }
            voice.Source.volume = target;
            voice.Fade = null;
            if (removeWhenDone)
            {
                voice.Source.Stop();
                Destroy(voice.Source.gameObject);
                loops.Remove(voice.Entry.Id);
            }
        }

        private float FullLoopVolume(LoopVoice voice)
        {
            float i = voice.Intensity;
            float vol = LoopVolumeMin + (1f - LoopVolumeMin) * i;
            return voice.Entry.Volume * GroupFactor(voice.Entry.Group) * vol;
        }

        // --- Музыка ---

        // живые корутины кроссфейда; новый кроссфейд гасит предыдущие,
        // иначе две корутины воюют за один канал и могут застопать свежую музыку
        private readonly List<Coroutine> musicFades = new List<Coroutine>();

        // число ещё не завершённых кроссфейдов (счётчик, а не musicFades.Count:
        // хэндлы из списка не удаляются до StopAllMusicFades)
        private int activeMusicFades;

        private void StopAllMusicFades()
        {
            foreach (Coroutine fade in musicFades)
                if (fade != null) StopCoroutine(fade);
            musicFades.Clear();
        }

        public void PlayMusic(SoundId id)
        {
            if (id == currentMusic)
                return;
            SoundEntry entry = GetEntry(id);
            if (entry == null)
                return;

            currentMusic = id;
            AudioSource fadeIn = musicFlip % 2 == 0 ? musicA : musicB;
            AudioSource fadeOut = musicFlip % 2 == 0 ? musicB : musicA;
            musicFlip++;
            musicActive = fadeIn;

            fadeIn.clip = ChooseClip(entry, 1f);
            fadeIn.loop = true;
            fadeIn.pitch = entry.Pitch;
            fadeIn.mute = musicPaused; // пауза плеера переживает смену трека
            fadeIn.Play();

            StopAllMusicFades();
            musicFades.Add(StartCoroutine(Crossfade(fadeIn, fadeOut, entry.Volume * GroupFactor(AudioGroup.Music))));
            MusicStateChanged?.Invoke(); // подписчики узнают о смене трека (меню↔игра)
        }

        public void StopMusic()
        {
            currentMusic = SoundId.None;
            musicActive = null;
            StopAllMusicFades();
            if (musicA.isPlaying) musicFades.Add(StartCoroutine(Crossfade(null, musicA, 0f)));
            if (musicB.isPlaying) musicFades.Add(StartCoroutine(Crossfade(null, musicB, 0f)));
            MusicStateChanged?.Invoke();
        }

        // --- Плейлист музыки (П9.3) ---

        private SoundId[] playlist;       // треки каталога для плеера борткомпа
        private AudioSource musicActive;  // канал текущего трека (для прогресса)
        private bool musicPaused;         // пауза = mute канала, НЕ Time.timeScale

        // смена трека/паузы — имя и состояние для UI плеера
        public event System.Action MusicStateChanged;

        public SoundId CurrentTrack => currentMusic;

        public bool MusicPaused => musicPaused;

        // треки для плеера: группа Music, но эмбиент — подложка SceneMusic,
        // не трек; записи без клипов в плейлист не попадают
        public IReadOnlyList<SoundId> MusicPlaylist
        {
            get
            {
                if (playlist == null)
                {
                    var tracks = new List<SoundId>();
                    if (catalog != null)
                        foreach (SoundId id in catalog.IdsOfGroup(AudioGroup.Music))
                        {
                            SoundEntry entry = catalog.Get(id);
                            if (id != SoundId.AmbientSpace && entry != null && entry.IsValid)
                                tracks.Add(id);
                        }
                    playlist = tracks.ToArray();
                }
                return playlist;
            }
        }

        // dir: +1 — следующий трек, −1 — предыдущий, по кругу; ничего не играет —
        // первый запуск плеером продолжает то, что играет (MusicFlight в игре),
        // при тишине плейлист стартует с начала
        public void PlayPlaylistNext(int dir)
        {
            IReadOnlyList<SoundId> tracks = MusicPlaylist;
            if (tracks.Count == 0)
                return;
            int index = -1;
            for (int i = 0; i < tracks.Count; i++)
                if (tracks[i] == currentMusic)
                {
                    index = i;
                    break;
                }
            if (index < 0)
                index = dir >= 0 ? -1 : tracks.Count; // вне плейлиста — с краю по направлению
            index = (index + dir + tracks.Count) % tracks.Count;
            PlayMusic(tracks[index]);
            MusicStateChanged?.Invoke();
        }

        // ▶/⏸ плеера: mute музыкальных каналов — кроссфейд и timeScale не трогаем;
        // при тишине запуск плейлиста вместо бессмысленного переключения mute
        public void ToggleMusicPause()
        {
            if (currentMusic == SoundId.None)
            {
                PlayPlaylistNext(1);
                return;
            }
            musicPaused = !musicPaused;
            musicA.mute = musicPaused;
            musicB.mute = musicPaused;
            MusicStateChanged?.Invoke();
        }

        // доля проигранного трека для прогресс-бара (0..1)
        public float MusicProgress
        {
            get
            {
                if (musicActive == null || musicActive.clip == null)
                    return 0f;
                float length = musicActive.clip.length;
                return length > 0f ? Mathf.Clamp01(musicActive.time / length) : 0f;
            }
        }

        private IEnumerator Crossfade(AudioSource fadeIn, AudioSource fadeOut, float targetVolume)
        {
            activeMusicFades++;
            try
            {
                yield return RunCrossfade(fadeIn, fadeOut, targetVolume);
            }
            finally
            {
                // finally отработает и при остановке корутины — иначе счётчик
                // залипает и ApplyMusicVolume навсегда игнорирует слайдер
                activeMusicFades--;
            }
        }

        private IEnumerator RunCrossfade(AudioSource fadeIn, AudioSource fadeOut, float targetVolume)
        {
            float t = 0f;
            while (t < MusicFadeTime)
            {
                t += Time.unscaledDeltaTime;
                float k = t / MusicFadeTime;
                if (fadeIn != null)
                {
                    // цель пересчитывается каждый кадр: слайдер музыки мог
                    // поехать посреди кроссфейда — иначе застынет старое значение
                    if (fadeIn == musicActive)
                        targetVolume = CurrentMusicVolume();
                    fadeIn.volume = Mathf.Lerp(0f, targetVolume, k);
                }
                if (fadeOut != null) fadeOut.volume = Mathf.Lerp(fadeOut.volume, 0f, k);
                yield return null;
            }
            if (fadeOut != null)
            {
                fadeOut.Stop();
                fadeOut.clip = null;
            }
        }

        // --- Громкости групп и настройки ---

        public void SetGroupVolume(AudioGroup group, float linear01)
        {
            groupVolumes[group] = Mathf.Clamp01(linear01);
            SaveVolume(group.ToString(), groupVolumes[group]);
            if (group == AudioGroup.Music)
                ApplyMusicVolume(); // слайдер должен менять и уже играющий трек
            RefreshLoops();
        }

        // громкость музыки применяется к живым каналам сразу; во время
        // кроссфейда цель пересчитывает сама корутина (см. Crossfade)
        private void ApplyMusicVolume()
        {
            if (activeMusicFades > 0)
                return;
            float target = CurrentMusicVolume();
            if (musicA != null && musicA.isPlaying)
                musicA.volume = target;
            if (musicB != null && musicB.isPlaying)
                musicB.volume = target;
        }

        private float CurrentMusicVolume()
        {
            if (currentMusic == SoundId.None)
                return 0f;
            SoundEntry entry = GetEntry(currentMusic);
            return (entry != null ? entry.Volume : 1f) * GroupFactor(AudioGroup.Music);
        }

        public float GetGroupVolume(AudioGroup group) => groupVolumes.TryGetValue(group, out float v) ? v : 1f;

        // Общая громкость; квадратичная кривая — субъективно ровная
        public void SetMasterVolume(float linear01)
        {
            float v = Mathf.Clamp01(linear01);
            AudioListener.volume = v * v;
            SaveVolume("Master", v);
        }

        public float GetMasterVolume() => Mathf.Sqrt(AudioListener.volume);

        private void RefreshLoops()
        {
            foreach (LoopVoice voice in loops.Values)
                if (!voice.FadingOut)
                    ApplyLoopParams(voice);
        }

        private void DuckAll()
        {
            if (duckRoutine != null) StopCoroutine(duckRoutine);
            duckRoutine = StartCoroutine(DuckRoutine());
        }

        private IEnumerator DuckRoutine()
        {
            foreach (AudioGroup g in System.Enum.GetValues(typeof(AudioGroup)))
                if (g != AudioGroup.Music && g != AudioGroup.Ui)
                    groupDuck[g] = DuckFactor;
            RefreshLoops();
            float t = 0f;
            while (t < DuckTime)
            {
                t += Time.unscaledDeltaTime;
                float recover = Mathf.Clamp01(t / DuckTime);
                foreach (AudioGroup g in System.Enum.GetValues(typeof(AudioGroup)))
                    if (g != AudioGroup.Music && g != AudioGroup.Ui)
                        groupDuck[g] = Mathf.Lerp(DuckFactor, 1f, recover);
                RefreshLoops();
                yield return null;
            }
        }

        // --- Внутреннее ---

        private float GroupFactor(AudioGroup group)
        {
            float vol = groupVolumes.TryGetValue(group, out float v) ? v : 1f;
            float duck = groupDuck.TryGetValue(group, out float d) ? d : 1f;
            return vol * duck;
        }

        private SoundEntry GetEntry(SoundId id)
        {
            if (catalog == null)
                return null;
            SoundEntry entry = catalog.Get(id);
            if (entry == null || !entry.IsValid)
            {
                // слоты без клипов (ждут файлы): лупы зовут каждый кадр —
                // предупреждение один раз на id, не спамим
                if (warnedSilent.Add(id))
                    Debug.LogWarning($"[Audio] нет клипов для {id}");
                return null;
            }
            return entry;
        }

        // выбор вариации: по силе или случайно
        private AudioClip ChooseClip(SoundEntry entry, float intensity)
        {
            AudioClip[] clips = entry.Clips;
            if (clips.Length == 1)
                return clips[0];
            int index = entry.Sequential
                ? Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(intensity) * clips.Length), 0, clips.Length - 1)
                : Random.Range(0, clips.Length);
            return clips[index];
        }

        private AudioSource CreateVoice(string name)
        {
            var go = new GameObject("Audio_" + name);
            go.transform.SetParent(transform, false);
            return go.AddComponent<AudioSource>();
        }

        private AudioSource GetFreeOneShotVoice()
        {
            foreach (AudioSource src in oneShotPool)
                if (!src.isPlaying)
                    return src;
            if (oneShotPool.Count >= MaxOneShotVoices)
                return oneShotPool[0]; // переполнение — перебиваем старейший занятый
            AudioSource voice = CreateVoice("OneShot");
            oneShotPool.Add(voice);
            return voice;
        }

        private static float LoadVolume(AudioGroup g) => LoadVolume(g.ToString());

        private static float LoadVolume(string key)
        {
            return PlayerPrefs.GetFloat("audio.vol." + key, 1f);
        }

        private static void SaveVolume(string key, float value)
        {
            PlayerPrefs.SetFloat("audio.vol." + key, value);
            PlayerPrefs.Save();
        }
    }

    // Фасад: Sound.Play(SoundId.Takeoff) из любого кода; без менеджера — тихо
    public static class Sound
    {
        public static void Play(SoundId id, float intensity = 1f) =>
            AudioManager.Instance?.PlayOneShot(id, intensity);

        public static void SetLoop(SoundId id, bool active) =>
            AudioManager.Instance?.SetLoopActive(id, active);

        public static void StartLoop(SoundId id) =>
            AudioManager.Instance?.StartLoop(id);

        public static void StopLoop(SoundId id) =>
            AudioManager.Instance?.StopLoop(id);

        public static void SetLoopIntensity(SoundId id, float intensity) =>
            AudioManager.Instance?.SetLoopIntensity(id, intensity);

        public static void SetLoopPan(SoundId id, float pan) =>
            AudioManager.Instance?.SetLoopPan(id, pan);

        public static void PlayMusic(SoundId id) =>
            AudioManager.Instance?.PlayMusic(id);

        public static void StopMusic() =>
            AudioManager.Instance?.StopMusic();
    }
}
