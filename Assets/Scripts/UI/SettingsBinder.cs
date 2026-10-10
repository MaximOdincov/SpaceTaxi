using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceTaxi.Audio;

namespace SpaceTaxi.UI
{
    // Слайдеры громкости, применяются сразу; сохраняет сам AudioManager
    public class SettingsBinder : MonoBehaviour
    {
        private void OnEnable()
        {
            Bind("SliderMaster", "SliderMasterValue", v => Instance.SetMasterVolume(v), Instance.GetMasterVolume);
            Bind("SliderMusic", "SliderMusicValue", v => Instance.SetGroupVolume(AudioGroup.Music, v), () => Instance.GetGroupVolume(AudioGroup.Music));
            Bind("SliderSfx", "SliderSfxValue", v => Instance.SetGroupVolume(AudioGroup.Sfx, v), () => Instance.GetGroupVolume(AudioGroup.Sfx));
            Bind("SliderUi", "SliderUiValue", v => Instance.SetGroupVolume(AudioGroup.Ui, v), () => Instance.GetGroupVolume(AudioGroup.Ui));
        }

        private void Bind(string sliderName, string valueName, System.Action<float> apply, System.Func<float> get)
        {
            Transform panel = transform.Find("Panel");
            var slider = panel.Find(sliderName).GetComponent<Slider>();
            var value = panel.Find(valueName).GetComponent<TMP_Text>();

            // текущее значение из менеджера
            slider.value = get();
            value.text = Mathf.RoundToInt(slider.value * 100f) + "%";

            slider.onValueChanged.AddListener(v =>
            {
                apply(v);
                value.text = Mathf.RoundToInt(v * 100f) + "%";
            });
        }

        private static AudioManager Instance => AudioManager.Instance;
    }
}
