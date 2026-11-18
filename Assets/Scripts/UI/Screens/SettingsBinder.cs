using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using SpaceTaxi.Audio;

namespace SpaceTaxi.UI
{
    // Настройки: громкости + полноэкранный режим. Раскладка панели собирается
    // кодом в Awake (FixLayout): YAML префаба руками не правим, а оверрайды
    // GameScene обнуляют y-анкоры Fill/Handle слайдеров (нулевая высота —
    // «слипшиеся» полосы), тексты в префабе залиты в мини-прямоугольники, а
    // блок «Управление» наезжает сам на себя. Префаб неактивен до первого
    // открытия, поэтому всё чинится на первом OnEnable — владельцу ничего
    // нажимать в редакторе не нужно.
    public class SettingsBinder : MonoBehaviour
    {
        // video.fullscreen: 1 = FullScreenWindow, 0 = Windowed, ключа нет — режим не трогаем
        private const string FullscreenKey = "video.fullscreen";

        private static readonly string[] SliderNames = { "SliderMaster", "SliderMusic", "SliderSfx", "SliderUi" };

        private const float RowStep = 52f;   // шаг строк громкости
        private const float RowTop0 = -150f; // верх первой строки (ось панели, вниз)
        private const float LabelX = 100f;
        private const float SliderX = 360f;
        private const float ValueX = 730f;
        // префаб раскладывал элементы от верхнего левого края панели —
        // отрицательный y считается вниз от верха
        private static readonly Vector2 TopAnchor = new Vector2(0f, 1f);
        private static readonly Vector2 TopPivot = new Vector2(0f, 1f);

        private static readonly Color LabelGreen = new Color(0.357f, 0.788f, 0.561f, 1f);
        private static readonly Color TrackDark = new Color(0.059f, 0.2f, 0.125f, 0.82f);
        private static readonly Color Accent = new Color(0.659f, 1f, 0.796f, 1f);

        private sealed class VolumeRow
        {
            public Slider Slider;
            public TMP_Text Value;
            public System.Action<float> Apply;
            public System.Func<float> Current;
        }

        private readonly List<VolumeRow> volumeRows = new List<VolumeRow>();
        private Toggle fullscreenToggle;

        private void Awake()
        {
            FixLayout();
            HookButtons();
        }

        private void OnEnable()
        {
            HookSliders();
            if (fullscreenToggle != null)
                fullscreenToggle.SetIsOnWithoutNotify(IsFullscreen());
        }

        // --- Полноэкранный режим: применяем сразу, храним в PlayerPrefs ---

        private static bool IsFullscreen() => Screen.fullScreenMode != FullScreenMode.Windowed;

        // префаб SettingsUI неактивен до первого открытия экрана — сохранённый
        // режим окна восстанавливаем раньше, при старте сцены
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RestoreFullscreenAtStartup()
        {
            int saved = PlayerPrefs.GetInt(FullscreenKey, -1);
            if (saved == 1)
                Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
            else if (saved == 0)
                Screen.fullScreenMode = FullScreenMode.Windowed;
        }

        private static void ApplyFullscreen(bool on)
        {
            Screen.fullScreenMode = on ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            PlayerPrefs.SetInt(FullscreenKey, on ? 1 : 0);
            PlayerPrefs.Save();
            Sound.Play(SoundId.UiClick);
        }

        // --- Раскладка ---

        private void FixLayout()
        {
            Transform panel = transform.Find("Panel");
            if (panel == null)
                return;

            SetRect(panel as RectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(960f, 760f));

            Transform labelSource = panel.Find("SliderMasterLabel");
            TMP_Text fontSource = labelSource != null ? labelSource.GetComponent<TMP_Text>() : null;

            // шапка
            Point(panel.Find("Title"), 0f, -40f, 420f, 46f, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            Point(panel.Find("SoundTitle"), LabelX, -100f, 320f, 32f, TopAnchor, TopPivot);
            SetStyle(panel.Find("SoundTitle"), fontSource, 28f);

            // строки громкости: подпись слева, слайдер по центру, значение справа
            for (int i = 0; i < SliderNames.Length; i++)
                LayoutSliderRow(panel, SliderNames[i], RowTop0 - RowStep * i, fontSource);

            // строка полноэкранного режима — тем же шагом после громкости
            LayoutFullscreenRow(panel, RowTop0 - RowStep * SliderNames.Length, fontSource);

            // управление: две колонки, в префабе они перекрывались
            Point(panel.Find("Div1"), LabelX, -420f, 760f, 2f, TopAnchor, TopPivot);
            Point(panel.Find("ControlsTitle"), LabelX, -450f, 400f, 32f, TopAnchor, TopPivot);
            SetStyle(panel.Find("ControlsTitle"), fontSource, 28f);
            Point(panel.Find("Controls1"), LabelX, -490f, 370f, 150f, TopAnchor, TopPivot);
            Point(panel.Find("Controls2"), 530f, -490f, 380f, 112f, TopAnchor, TopPivot);

            // кнопки одним нижним рядом
            Point(panel.Find("Button_Back"), 200f, -688f, 260f, 56f, TopAnchor, TopPivot);
            Point(panel.Find("Button_Apply"), 500f, -688f, 260f, 56f, TopAnchor, TopPivot);
        }

        private void LayoutSliderRow(Transform panel, string sliderName, float rowTop, TMP_Text fontSource)
        {
            float centerY = rowTop - 16f;

            Point(panel.Find(sliderName + "Label"), LabelX, centerY, 230f, 32f, TopAnchor, new Vector2(0f, 0.5f));
            SetStyle(panel.Find(sliderName + "Label"), fontSource, 28f);

            RectTransform slider = panel.Find(sliderName) as RectTransform;
            Point(slider, SliderX, centerY, 360f, 26f, TopAnchor, new Vector2(0f, 0.5f));
            FixSliderInternals(slider);

            Point(panel.Find(sliderName + "Value"), ValueX, centerY, 100f, 32f, TopAnchor, new Vector2(0f, 0.5f));
            SetStyle(panel.Find(sliderName + "Value"), fontSource, 28f);
        }

        // Стандартная схема слайдера. Slider.UpdateVisuals двигает Fill/Handle
        // только по оси x, поэтому обнулённые сценой y-анкоры сами не
        // восстановятся — Fill/Handle остаются нулевой высоты.
        private static void FixSliderInternals(RectTransform slider)
        {
            if (slider == null)
                return;
            Slider comp = slider.GetComponent<Slider>();
            float v = comp != null ? comp.normalizedValue : 0.8f;

            SetRect(slider.Find("Background") as RectTransform, new Vector2(0f, 0.25f), new Vector2(1f, 0.75f),
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            SetRect(slider.Find("Fill Area") as RectTransform, new Vector2(0f, 0.25f), new Vector2(1f, 0.75f),
                new Vector2(0.5f, 0.5f), new Vector2(-5f, 0f), new Vector2(-20f, 0f));
            SetRect(slider.Find("Fill Area/Fill") as RectTransform, new Vector2(0f, 0f), new Vector2(v, 1f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10f, 0f));
            SetRect(slider.Find("Handle Slide Area") as RectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-20f, 0f));
            SetRect(slider.Find("Handle Slide Area/Handle") as RectTransform, new Vector2(v, 0f), new Vector2(v, 1f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10f, 0f));
        }

        private void LayoutFullscreenRow(Transform panel, float rowTop, TMP_Text fontSource)
        {
            float centerY = rowTop - 16f;

            TMP_Text label = OnboardComputerFactory.Text(panel.gameObject, "ПОЛНЫЙ ЭКРАН", 28f, LabelGreen, "LabelFullscreen");
            if (fontSource != null)
            {
                label.font = fontSource.font;
                label.fontSharedMaterial = fontSource.fontSharedMaterial;
            }
            SetRect(label.rectTransform, TopAnchor, TopAnchor, new Vector2(0f, 0.5f),
                new Vector2(LabelX, centerY), new Vector2(230f, 32f));

            // тумблер в колонке слайдеров: тёмная плашка + галочка, стилистика треков
            var toggleGo = new GameObject("ToggleFullscreen", typeof(RectTransform), typeof(Image), typeof(Toggle));
            toggleGo.transform.SetParent(panel, false);
            SetRect(toggleGo.GetComponent<RectTransform>(), TopAnchor, TopAnchor, new Vector2(0f, 0.5f),
                new Vector2(SliderX, centerY), new Vector2(34f, 26f));
            Image bg = toggleGo.GetComponent<Image>();
            bg.color = TrackDark;

            var checkGo = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
            checkGo.transform.SetParent(toggleGo.transform, false);
            SetRect(checkGo.GetComponent<RectTransform>(), Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-8f, -8f));
            Image check = checkGo.GetComponent<Image>();
            check.color = Accent;
            check.raycastTarget = false;

            fullscreenToggle = toggleGo.GetComponent<Toggle>();
            fullscreenToggle.targetGraphic = bg;
            fullscreenToggle.graphic = check;
            fullscreenToggle.isOn = IsFullscreen();
            fullscreenToggle.onValueChanged.AddListener(ApplyFullscreen);
        }

        // --- Громкости ---

        private void HookSliders()
        {
            if (Instance == null)
                return;
            Transform panel = transform.Find("Panel");
            if (panel == null)
                return;

            if (volumeRows.Count == 0)
            {
                volumeRows.Add(BindRow(panel, "SliderMaster", "SliderMasterValue",
                    v => Instance.SetMasterVolume(v), Instance.GetMasterVolume));
                volumeRows.Add(BindRow(panel, "SliderMusic", "SliderMusicValue",
                    v => Instance.SetGroupVolume(AudioGroup.Music, v), () => Instance.GetGroupVolume(AudioGroup.Music)));
                volumeRows.Add(BindRow(panel, "SliderSfx", "SliderSfxValue",
                    v => Instance.SetGroupVolume(AudioGroup.Sfx, v), () => Instance.GetGroupVolume(AudioGroup.Sfx)));
                volumeRows.Add(BindRow(panel, "SliderUi", "SliderUiValue",
                    v => Instance.SetGroupVolume(AudioGroup.Ui, v), () => Instance.GetGroupVolume(AudioGroup.Ui)));
            }

            // при каждом открытии подтягиваем актуальные значения из менеджера
            foreach (VolumeRow row in volumeRows)
                row.Slider.value = row.Current();
        }

        private VolumeRow BindRow(Transform panel, string sliderName, string valueName,
            System.Action<float> apply, System.Func<float> current)
        {
            var row = new VolumeRow
            {
                Slider = panel.Find(sliderName).GetComponent<Slider>(),
                Value = panel.Find(valueName).GetComponent<TMP_Text>(),
                Apply = apply,
                Current = current,
            };
            row.Slider.onValueChanged.AddListener(v =>
            {
                row.Apply(v);
                row.Value.text = Mathf.RoundToInt(v * 100f) + "%";
            });
            return row;
        }

        // --- Кнопки ---

        // Инспекторные AudioSource-клики не проходят через группу Ui (фиксированная
        // громкость) — глушим их и щёлкаем через Sound.Play, тогда слайдер
        // «ИНТЕРФЕЙС» влияет и на кнопки настроек.
        private void HookButtons()
        {
            Transform panel = transform.Find("Panel");
            if (panel == null)
                return;
            Hook(panel, "Button_Back");
            Hook(panel, "Button_Apply");
        }

        private static void Hook(Transform panel, string buttonName)
        {
            Transform holder = panel.Find(buttonName);
            Button button = holder != null ? holder.GetComponent<Button>() : null;
            if (button == null)
                return;
            int count = button.onClick.GetPersistentEventCount();
            for (int i = 0; i < count; i++)
                if (button.onClick.GetPersistentTarget(i) is AudioSource)
                    button.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
            UiSfx.Hook(button);
        }

        // --- Хелперы раскладки ---

        // точечный анкор: anchorMin == anchorMax
        private static void Point(Component holder, float x, float y, float w, float h, Vector2 anchor, Vector2 pivot)
            => SetRect(holder != null ? holder.transform as RectTransform : null,
                anchor, anchor, pivot, new Vector2(x, y), new Vector2(w, h));

        private static void SetRect(RectTransform t, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 pos, Vector2 size)
        {
            if (t == null)
                return;
            t.anchorMin = anchorMin;
            t.anchorMax = anchorMax;
            t.pivot = pivot;
            t.anchoredPosition = pos;
            t.sizeDelta = size;
            t.localScale = Vector3.one;
        }

        private static void SetStyle(Transform textHolder, TMP_Text fontSource, float size)
        {
            TMP_Text text = textHolder != null ? textHolder.GetComponent<TMP_Text>() : null;
            if (text == null)
                return;
            if (fontSource != null)
            {
                text.font = fontSource.font;
                text.fontSharedMaterial = fontSource.fontSharedMaterial;
            }
            text.fontSize = size;
            // в префабе у текстов отрицательные поля — подпись съезжает на слайдер
            text.margin = Vector4.zero;
        }

        private static AudioManager Instance => AudioManager.Instance;
    }
}
