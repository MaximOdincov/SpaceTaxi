using UnityEngine.UI;

namespace SpaceTaxi.Audio
{
    // Единый щелчок UI-кнопок. Хук вешается ДО пользовательского обработчика —
    // runtime-слушатели onClick срабатывают в порядке добавления, поэтому
    // звук идёт раньше действия. Кнопки сцен (пауза/меню/настройки/карта) уже
    // кликают AudioSource-ом из инспектора — им хук не нужен (будет двойной).
    public static class UiSfx
    {
        public static void Hook(Button button)
        {
            if (button == null)
                return;
            button.onClick.AddListener(() => Sound.Play(SoundId.UiClick));
        }
    }
}
