using UnityEngine;

namespace SpaceTaxi.Audio
{
    // Музыка и эмбиент сцены: вешается на объект в сцене (меню/игра).
    // Меню: music = MusicMenu, ambient = false. Игра: MusicFlight + эмбиент.
    public sealed class SceneMusic : MonoBehaviour
    {
        [SerializeField] private SoundId music = SoundId.MusicFlight;
        [SerializeField] private bool ambient = true;

        private bool started;

        private void Start()
        {
            Sound.PlayMusic(music);
            // ЗВУК ВЫКЛ (2026-09-20): эмбиент отключён, оставлена только музыка. Вернуть к аудио-итерации.
            //if (ambient)
            //    Sound.StartLoop(SoundId.AmbientSpace);
            //started = ambient;
        }

        // Смена сцены — эмбиент прошлой сцены гасим (менеджер переживает сцену).
        private void OnDestroy()
        {
            // ЗВУК ВЫКЛ: вместе с эмбиентом.
            //if (started)
            //    Sound.StopLoop(SoundId.AmbientSpace);
        }
    }
}
