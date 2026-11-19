using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceTaxi.Audio;

namespace SpaceTaxi.UI
{
    // --- Плеер «ЭФИР» ---
    // partial-секция OnboardBinder.
    public partial class OnboardBinder
    {
        // плеер «ЭФИР»
        private Button playerPrev, playerNext;
        private Button radioMute;
        private Button radioWave;         // «ВОЛНА 1/2/3»: плейлист → эмбиент-станция → классика
        private SoundId wave1Track;       // трек волны 1, запомненный при уходе в эфир (волны 2/3)
        private TMP_Text trackTitle;
        private RectTransform progressFill;
        private AudioManager audioManager;
        private bool musicBound;

        // узлы построены фабрикой (звуковой хук уже внутри Button); менеджер
        // живёт между сценами — разрешается на клике
        private void SetupMusicPlayer()
        {
            if (playerPrev != null)
                playerPrev.onClick.AddListener(() => AudioManager.Instance?.PlayPlaylistNext(-1));
            if (playerNext != null)
                playerNext.onClick.AddListener(() => AudioManager.Instance?.PlayPlaylistNext(1));
            if (radioMute != null)
                radioMute.onClick.AddListener(() => AudioManager.Instance?.ToggleMusicMuted());
            BuildWaveToggle();
        }

        // «ВОЛНА 1/2/3» (Н6/Z, Н7 A7.3): кнопка в стиле секции — копия мьют-кнопки под
        // ней. Волна 1 — как играло (плейлист не тронут), волна 2 — станция эмбиента
        // (MusicStation2: три эмбиента + menu_out_in_space по кругу), волна 3 —
        // классическая станция (MusicStation3, PD-микс Packs_N7_classical)
        private void BuildWaveToggle()
        {
            if (radioWave != null || radioMute == null)
                return;
            radioWave = Instantiate(radioMute.gameObject, radioMute.transform.parent).GetComponent<Button>();
            radioWave.gameObject.name = "RadioWave";
            SetButtonLabel(radioWave, WaveLabel());
            UiSfx.Hook(radioWave);
            radioWave.onClick.AddListener(ToggleRadioWave);
        }

        private static void SetButtonLabel(Button button, string text)
        {
            TMP_Text label = button.GetComponentInChildren<TMP_Text>();
            if (label != null)
                label.text = text;
        }

        private string WaveLabel()
        {
            SoundId track = audioManager != null ? audioManager.CurrentTrack : SoundId.None;
            if (track == SoundId.MusicStation2)
                return "ВОЛНА: 2";
            if (track == SoundId.MusicStation3)
                return "ВОЛНА: 3";
            return "ВОЛНА: 1";
        }

        private void ToggleRadioWave()
        {
            AudioManager manager = audioManager != null ? audioManager : AudioManager.Instance;
            if (manager == null)
                return;
            audioManager = manager; // метки ниже и событие читают поле, а не локальную ссылку
            if (manager.CurrentTrack == SoundId.MusicStation3)
            {
                // волна 3 → волна 1: тот же трек, что играл до эфира;
                // памяти нет — дорожная волна (MusicFlight)
                manager.PlayMusic(Wave1Track());
            }
            else if (manager.CurrentTrack == SoundId.MusicStation2)
            {
                // волна 2 → волна 3 (классика). Н10.7: пустая станция пропускается
                // МОЛЧА к следующей НЕ пустой по кругу; пусты все — остаёмся на
                // текущей (владелец: ВОЛНА никогда не оставляет тишину)
                SoundId station = manager.NextValidStation(SoundId.MusicStation3, 1);
                if (station == SoundId.None)
                    return;
                manager.PlayMusic(station);
            }
            else
            {
                // волна 1 → волна 2: запоминаем, куда вернуться по кругу на волну 1.
                // Н10.7: пустая станция пропускается молча к следующей НЕ пустой
                wave1Track = manager.CurrentTrack;
                SoundId station = manager.NextValidStation(SoundId.MusicStation2, 1);
                if (station == SoundId.None)
                    return;
                manager.PlayMusic(station);
            }
            RefreshMusicPlayer(); // событие MusicStateChanged придёт и так — дублируем для мгновенной метки
        }

        // трек волны 1: запомненный при уходе в эфир; памяти нет (или в памяти
        // сама волна эфира) — дорожная волна (MusicFlight)
        private SoundId Wave1Track()
        {
            return wave1Track == SoundId.None
                || wave1Track == SoundId.MusicStation2
                || wave1Track == SoundId.MusicStation3
                ? SoundId.MusicFlight
                : wave1Track;
        }

        // имя станции и метка мьюта по состоянию менеджера; событие MusicStateChanged
        private void RefreshMusicPlayer()
        {
            if (trackTitle == null)
                return; // биндер разрушен, менеджер живёт между сценами
            if (radioMute != null)
            {
                TMP_Text muteLabel = radioMute.GetComponentInChildren<TMP_Text>();
                if (muteLabel != null)
                    muteLabel.text = audioManager != null && audioManager.MusicMuted ? "ЗВУК: ВЫКЛ" : "ЗВУК: ВКЛ";
            }
            if (radioWave != null)
                SetButtonLabel(radioWave, WaveLabel()); // синхрон и при prev/next мимо волн 2/3
            if (audioManager == null || audioManager.MusicPlaylist.Count == 0)
            {
                trackTitle.text = "ЭФИР · треки не найдены";
            }
            else
            {
                trackTitle.text = "СТАНЦИЯ · " + StationName(audioManager.CurrentTrack);
            }
        }

        // названия радиостанций (SoundId станции -> имя в эфире);
        // у SoundEntry DisplayName нет — карта известных треков, незнакомые — имя enum
        private static string StationName(SoundId id)
        {
            switch (id)
            {
                case SoundId.MusicFlight: return "Дорожная волна";
                case SoundId.MusicStation1: return "Космо-классика";
                case SoundId.MusicStation2: return "Ретро-волна";
                case SoundId.MusicStation3: return "Классика"; // Н7 A7.3: волна 3 — PD-микс классики (была заглушка «Механика драйв»)
                case SoundId.MusicStation4: return "Ночная частота";
                default: return "—";
            }
        }
    }
}
