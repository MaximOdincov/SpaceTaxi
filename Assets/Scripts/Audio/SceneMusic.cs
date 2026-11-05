using UnityEngine;

namespace SpaceTaxi.Audio
{
    // Музыка сцены (меню — MusicMenu, игра — MusicFlight) и эмбиент кабины:
    // поле ambient сериализовано в сценах (GameScene 1, MainMenu 0) —
    // эмбиент включается только там, где он нужен
    public sealed class SceneMusic : MonoBehaviour
    {
        [SerializeField] private SoundId music = SoundId.MusicFlight;
        [SerializeField] private bool ambient = true;

        private void Start()
        {
            Sound.PlayMusic(music);
            if (ambient)
                Sound.StartLoop(SoundId.AmbientSpace);
        }

        private void OnDestroy()
        {
            // AudioManager переживает сцену — гасим эмбиент вместе с её SceneMusic
            if (ambient)
                Sound.StopLoop(SoundId.AmbientSpace);
        }
    }
}
