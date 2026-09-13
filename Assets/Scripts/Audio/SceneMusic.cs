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
            if (ambient)
                Sound.StartLoop(SoundId.AmbientSpace);
            started = ambient;
        }

        // Смена сцены — эмбиент прошлой сцены гасим (менеджер переживает сцену).
        private void OnDestroy()
        {
            if (started)
                Sound.StopLoop(SoundId.AmbientSpace);
        }
    }
}
