using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SpaceTaxi.Audio;
using UiKit;

namespace SpaceTaxi.UI
{
    // Настройки: панель собирается через UiKit (слайдеры громкости + схема
    // управления); из префаба берётся только шрифт заголовка
    public class SettingsBinder : MonoBehaviour
    {
        private sealed class VolumeRow
        {
            public Slider Slider;
            public TMP_Text Value;
            public System.Action<float> Apply;
            public System.Func<float> Current;
        }

        private readonly List<VolumeRow> volumeRows = new List<VolumeRow>();
        private TMP_FontAsset font;
        private bool built; // панель строится один раз (Awake может вызываться повторно)

        private void Awake()
        {
            if (built)
                return;
            built = true;
            CaptureFont();
            BuildPanel();
        }

        private void OnEnable()
        {
            // при каждом открытии подтягиваем актуальные значения из менеджера
            foreach (VolumeRow row in volumeRows)
                row.Slider.value = row.Current();
        }

        private void CaptureFont()
        {
            var title = transform.Find("Panel/Title");
            if (title != null)
                font = title.GetComponent<TMP_Text>() != null ? title.GetComponent<TMP_Text>().font : null;
        }

        private void BuildPanel()
        {
            // сносим префабный контент сразу (deferred Destroy оставил бы
            // старую панель живой на кадр)
            for (int i = transform.childCount - 1; i >= 0; i--)
                DestroyImmediate(transform.GetChild(i).gameObject);

            // Absolute: панель — свободный узел, LayoutElement никто не читает
            var panel = UiBox.Box(transform, "Panel").Absolute(960f, 760f)
                .Background(UiTheme.Panel)
                .Column(10f)
                .Padding(40, 40, 24, 24);

            panel.LabelBox("НАСТРОЙКИ", 42f, UiTheme.Bright, font, "Title").FixedHeight(56f);
            panel.LabelBox("// ЗВУК", 28f, UiTheme.Ink, font, "SoundTitle").FixedHeight(36f);

            // строки громкости: подпись | слайдер | значение %
            AddVolumeRow(panel, "ГРОМКОСТЬ",
                v => AudioManager.Instance?.SetMasterVolume(v),
                () => AudioManager.Instance != null ? AudioManager.Instance.GetMasterVolume() : 0.8f);
            AddVolumeRow(panel, "МУЗЫКА",
                v => AudioManager.Instance?.SetGroupVolume(AudioGroup.Music, v),
                () => AudioManager.Instance != null ? AudioManager.Instance.GetGroupVolume(AudioGroup.Music) : 0.8f);
            AddVolumeRow(panel, "ЭФФЕКТЫ",
                v => AudioManager.Instance?.SetGroupVolume(AudioGroup.Sfx, v),
                () => AudioManager.Instance != null ? AudioManager.Instance.GetGroupVolume(AudioGroup.Sfx) : 0.8f);
            AddVolumeRow(panel, "ИНТЕРФЕЙС",
                v => AudioManager.Instance?.SetGroupVolume(AudioGroup.Ui, v),
                () => AudioManager.Instance != null ? AudioManager.Instance.GetGroupVolume(AudioGroup.Ui) : 0.8f);

            panel.LabelBox("// УПРАВЛЕНИЕ", 28f, UiTheme.Ink, font, "ControlsTitle").FixedHeight(36f);
            var columns = UiBox.Box(panel.Rect, "Controls").Row(20f); // дочерний ряд
            columns.LabelBox("Тяг W\nТормоз S\nМанёвры A/D\nВзлёт ПРОБЕЛ", 20f, UiTheme.Ink, font, "Controls1").FixedHeight(150f);
            columns.LabelBox("Карта M\nБорт.комп TAB\nПауза ESC", 20f, UiTheme.Ink, font, "Controls2").FixedHeight(150f);

            var buttons = UiBox.Box(panel.Rect, "Buttons").Row(20f, TextAnchor.MiddleCenter); // дочерний ряд
            var apply = buttons.Button("ПРИМЕНИТЬ", UiTheme.Button, UiTheme.Bright, 24f, font: font, name: "Button_Apply");
            Ui.FixedSize(apply.gameObject, 260f, 56f);
            var back = buttons.Button("ЗАКРЫТЬ", UiTheme.Button, UiTheme.Bright, 24f, Close, font, "Button_Back");
            Ui.FixedSize(back.gameObject, 260f, 56f);
        }

        // строка громкости: подпись | слайдер | значение; bind сразу
        private void AddVolumeRow(UiBox panel, string label,
            System.Action<float> apply, System.Func<float> current)
        {
            var row = UiBox.Box(panel.Rect, "Row_" + label).Row(12f).FixedHeight(36f); // дочерний ряд
            row.LabelBox(label, 24f, UiTheme.Ink, font).FixedWidth(230f);
            var slider = row.SliderBox("Slider_" + label, UiTheme.Button, UiTheme.Accent, UiTheme.Bright);
            Ui.Flexible(slider.gameObject);
            var value = row.LabelBox("80%", 24f, UiTheme.Bright, font).FixedWidth(100f).Text;

            var rowRef = new VolumeRow { Slider = slider, Value = value, Apply = apply, Current = current };
            volumeRows.Add(rowRef);
            slider.value = current(); // триггерит слушатель — значение применяется сразу
            slider.onValueChanged.AddListener(v =>
            {
                rowRef.Apply(v);
                rowRef.Value.text = Mathf.RoundToInt(v * 100f) + "%";
            });
        }

        // ЗАКРЫТЬ — контроллер на том же объекте (префаб SettingsUI)
        private void Close()
        {
            GetComponent<SettingsScreenController>()?.Close();
        }
    }
}
