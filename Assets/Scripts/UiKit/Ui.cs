using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace UiKit
{
    // Примитивы построения uGUI: создание RectTransform/Image/TMP/лейаутов
    // живёт здесь, экраны зовут короткие методы или fluent UiBox.Box(...).
    // Тулкит не зависит от игрового кода: шрифт и цвета передаются параметрами
    public static class Ui
    {
        // --- контейнеры ---

        // голый RectTransform-ребёнок (якоря/позицию ставит вызывающий)
        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        // растянуть на весь родитель
        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // тот же цвет с другой альфой — для приглушённых фонов и треков
        public static Color WithAlpha(Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, alpha);
        }

        // горизонтальная строка-лейаут (ширина детей управляется лейаутом)
        public static GameObject Row(Transform parent, string name, float spacing = 8f,
            TextAnchor alignment = TextAnchor.MiddleLeft, bool expandHeight = true)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = expandHeight;
            layout.childAlignment = alignment;
            return go;
        }

        // вертикальная колонка-лейаут (высота детей — по содержимому)
        public static GameObject Column(Transform parent, string name, float spacing = 4f,
            TextAnchor alignment = TextAnchor.UpperLeft)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = alignment;
            return go;
        }

        // --- содержимое ---

        // однолинейный текст с обрезкой «…». ВАЖНО: Ellipsis режет и по вертикали —
        // высота узла должна быть ≥ высоты строки (36pt ≈ 47.5, 21pt ≈ 27.7),
        // иначе текст «есть, но невидим»
        public static TMP_Text Label(Transform parent, string text, float size, Color color,
            TMP_FontAsset font = null, string name = "Text")
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.font = font != null ? font : TMP_Settings.defaultFontAsset;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.text = text;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false;
            return tmp;
        }

        // кнопка: фон + светлый контур + подпись по центру. Звуковой хук навешивает
        // вызывающий (UiSfx.Hook) — щелчок должен звучать раньше его обработчиков
        public static Button Button(Transform parent, string label, Color background, Color textColor,
            float size, TMP_FontAsset font = null, string name = null, bool framed = true)
        {
            var go = new GameObject(name ?? label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = background;
            if (framed)
            {
                var outline = go.AddComponent<Outline>();
                outline.effectColor = UiTheme.ButtonOutline;
                outline.effectDistance = new Vector2(2f, -2f);
            }
            var text = Label(go.transform, label, size, textColor, font);
            Stretch(text.rectTransform);
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = true;
            return go.GetComponent<Button>();
        }

        // картинка (raycast off — чистая графика, клики не ест)
        public static Image Image(Transform parent, Sprite sprite, Color color, string name = "Image")
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        // слайдер: трек + Fill + Handle; внутренние rect'ы экраны не подгоняют
        public static Slider Slider(Transform parent, string name, Color track, Color fill, Color handleColor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Slider));
            go.transform.SetParent(parent, false);
            var slider = go.GetComponent<UnityEngine.UI.Slider>();
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;

            var trackImg = Child(go.transform, "Background", track);
            var trackRect = (RectTransform)trackImg.transform;
            trackRect.anchorMin = new Vector2(0f, 0.25f);
            trackRect.anchorMax = new Vector2(1f, 0.75f);

            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(go.transform, false);
            var fillRect = (RectTransform)fillArea.transform;
            fillRect.anchorMin = new Vector2(0f, 0.25f);
            fillRect.anchorMax = new Vector2(1f, 0.75f);
            fillRect.pivot = new Vector2(0.5f, 0.5f);
            fillRect.offsetMin = new Vector2(-5f, 0f);
            fillRect.offsetMax = new Vector2(-20f, 0f);
            var fillImg = Child(fillArea.transform, "Fill", fill);
            var fillInner = (RectTransform)fillImg.transform;
            fillInner.anchorMin = Vector2.zero;
            fillInner.anchorMax = new Vector2(0.8f, 1f);

            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(go.transform, false);
            var handleAreaRect = (RectTransform)handleArea.transform;
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.offsetMin = new Vector2(10f, 0f);
            handleAreaRect.offsetMax = new Vector2(-10f, 0f);
            var handleImg = Child(handleArea.transform, "Handle", handleColor);
            var handleRect = (RectTransform)handleImg.transform;
            handleRect.anchorMin = new Vector2(0.5f, 0f);
            handleRect.anchorMax = new Vector2(0.5f, 1f);
            handleRect.sizeDelta = new Vector2(10f, 0f);

            slider.fillRect = fillImg.rectTransform;
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImg;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            return slider;
        }

        // одноразовый ребёнок-картинка (внутри слайдера)
        private static Image Child(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            return img;
        }

        // --- размеры в лейаутах (один LayoutElement на узел) ---

        private static LayoutElement Le(GameObject go)
        {
            var le = go.GetComponent<LayoutElement>();
            return le != null ? le : go.AddComponent<LayoutElement>();
        }

        public static void FixedSize(GameObject go, float width, float height)
        {
            var le = Le(go);
            le.minWidth = width;
            le.minHeight = height;
            le.preferredWidth = width;
            le.preferredHeight = height;
            le.flexibleWidth = 0f;
            le.flexibleHeight = 0f;
        }

        public static void FixedHeight(GameObject go, float height)
        {
            var le = Le(go);
            le.minHeight = height;
            le.preferredHeight = height;
            le.flexibleHeight = 0f;
        }

        public static void FixedWidth(GameObject go, float width)
        {
            var le = Le(go);
            le.minWidth = width;
            le.preferredWidth = width;
            le.flexibleWidth = 0f;
        }

        public static void Flexible(GameObject go)
        {
            Le(go).flexibleWidth = 1f;
        }

        public static void IgnoreLayout(GameObject go)
        {
            Le(go).ignoreLayout = true;
        }

        // --- рамки ---

        // контур узла (UI.Outline)
        public static void Frame(GameObject go, Color color, float thickness = 2f)
        {
            var outline = go.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = new Vector2(thickness, -thickness);
        }

        // рамка из 4 полосок — для прозрачных картинок: Outline продублировал
        // бы спрайт 4 раза со смещением
        public static void RectBorder(GameObject go, Color color, float thickness = 2f)
        {
            var parent = (RectTransform)go.transform;
            void Bar(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
            {
                var bar = new GameObject(name, typeof(RectTransform), typeof(Image));
                bar.transform.SetParent(parent, false);
                var img = bar.GetComponent<Image>();
                img.color = color;
                img.raycastTarget = false;
                var rt = (RectTransform)bar.transform;
                rt.anchorMin = anchorMin;
                rt.anchorMax = anchorMax;
                rt.offsetMin = offsetMin;
                rt.offsetMax = offsetMax;
            }
            Bar("BorderTop", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -thickness), new Vector2(0f, 0f));
            Bar("BorderBottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, thickness));
            Bar("BorderLeft", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(thickness, 0f));
            Bar("BorderRight", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-thickness, 0f), new Vector2(0f, 0f));
        }
    }
}
