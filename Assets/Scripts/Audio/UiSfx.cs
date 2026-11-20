using UnityEngine.UI;

namespace SpaceTaxi.Audio
{
    // Единый щелчок UI-кнопок. Хук вешается ПЕРВЫМ: слушатели onClick
    // срабатывают в порядке добавления, поэтому звук идёт раньше действия.
    // Кнопкам сцен (клик через AudioSource в инспекторе) хук не нужен.
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
