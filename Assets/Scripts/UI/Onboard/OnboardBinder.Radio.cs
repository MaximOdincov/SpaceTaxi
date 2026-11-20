using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceTaxi.Audio;

namespace SpaceTaxi.UI
{
    // Плеер «ЭФИР»: треки, волны, мьют
    public partial class OnboardBinder
    {
        private Button playerPrev, playerNext;
        private Button radioMute;
        private Button radioWave;         // «ВОЛНА 1/2/3»: плейлист → эмбиент → классика
        private SoundId wave1Track;       // трек волны 1, запомненный при уходе в эфир
        private TMP_Text trackTitle;
        private RectTransform progressFill;
        private AudioManager audioManager;
        private bool musicBound;

        // узлы построены фабрикой (звуковой хук уже внутри Button);
        // менеджер живёт между сценами — разрешается на клике
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

        // «ВОЛНА 1/2/3»: копия мьют-кнопки; 1 — плейлист, 2 — эмбиент, 3 — классика
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
                // волна 3 → волна 1: трек, запомненный при уходе (нет — дорожная)
                manager.PlayMusic(Wave1Track());
            }
            else if (manager.CurrentTrack == SoundId.MusicStation2)
            {
                // волна 2 → волна 3; пустая станция пропускается к следующей непустой
                SoundId station = manager.NextValidStation(SoundId.MusicStation3, 1);
                if (station == SoundId.None)
                    return;
                manager.PlayMusic(station);
            }
            else
            {
                // волна 1 → волна 2: запоминаем трек, куда вернуться
                wave1Track = manager.CurrentTrack;
                SoundId station = manager.NextValidStation(SoundId.MusicStation2, 1);
                if (station == SoundId.None)
                    return;
                manager.PlayMusic(station);
            }
            RefreshMusicPlayer(); // событие придёт и так — дублируем для мгновенной метки
        }

        // трек волны 1: запомненный при уходе в эфир; памяти нет — дорожная волна
        private SoundId Wave1Track()
        {
            return wave1Track == SoundId.None
                || wave1Track == SoundId.MusicStation2
                || wave1Track == SoundId.MusicStation3
                ? SoundId.MusicFlight
                : wave1Track;
        }

        // имя станции и метка мьюта по состоянию менеджера
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
                SetButtonLabel(radioWave, WaveLabel()); // синхрон и при смене трека мимо волны
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
                case SoundId.MusicStation3: return "Классика";
                case SoundId.MusicStation4: return "Ночная частота";
                default: return "—";
            }
        }
    }
}
