using UnityEngine;

namespace SpaceTaxi.Audio
{
    // Музыка сцены (меню — MusicMenu, игра — MusicFlight)
    public sealed class SceneMusic : MonoBehaviour
    {
        [SerializeField] private SoundId music = SoundId.MusicFlight;
        [SerializeField] private bool ambient = true;

        private void Start()
        {
            Sound.PlayMusic(music);
            // эмбиент пока отключён
        }
    }
}
