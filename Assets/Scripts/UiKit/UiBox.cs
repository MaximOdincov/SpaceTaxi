using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace UiKit
{
    // Fluent-строитель панели/узла (Н11): задать размер области, фон и дальше
    // декларативно добавлять содержимое. Каждый вызов возвращает тот же бокс
    // (конфигурация) или новый бокс ребёнка (вложенные панели), GameObject
    // доступен через Go — хвосты, которых нет в API, доделываются напрямую
    //
    //     var card = Ui.Box(parent, "Card").Size(440f, 200f)
    //         .Background(UiTheme.Panel)
    //         .Column(spacing: 10f)
    //         .Label("БАК", 21f, UiTheme.Ink).FixedHeight(28f)
    //         .Row(...)…
    public sealed class UiBox
    {
        public GameObject Go { get; }
        public RectTransform Rect { get; }

        private UiBox(GameObject go)
        {
            Go = go;
            Rect = (RectTransform)go.transform;
        }

        // новый узел в parent: именованный RectTransform, размер — в Size()
        public static UiBox Box(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return new UiBox(go);
        }

        // обернуть уже существующий узел (достроить чужой GameObject)
        public static UiBox Wrap(GameObject go) => new UiBox(go);

        // --- сам узел ---

        public UiBox Size(float width, float height)
        {
            var le = Go.GetComponent<LayoutElement>() ?? Go.AddComponent<LayoutElement>();
            le.minWidth = width;
            le.minHeight = height;
            le.preferredWidth = width;
            le.preferredHeight = height;
            le.flexibleWidth = 0f;
            le.flexibleHeight = 0f;
            return this;
        }

        public UiBox FixedHeight(float height)
        {
            var le = Go.GetComponent<LayoutElement>() ?? Go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
            le.flexibleHeight = 0f;
            return this;
        }

        public UiBox FixedWidth(float width)
        {
            var le = Go.GetComponent<LayoutElement>() ?? Go.AddComponent<LayoutElement>();
            le.minWidth = width;
            le.preferredWidth = width;
            le.flexibleWidth = 0f;
            return this;
        }

        public UiBox Flexible() // растягивать по ширине лейаутом
        {
            (Go.GetComponent<LayoutElement>() ?? Go.AddComponent<LayoutElement>()).flexibleWidth = 1f;
            return this;
        }

        // фон панели: по умолчанию raycast off (декор); под кнопки/клики — raycast: true
        public UiBox Background(Color color, Sprite sprite = null, bool raycast = false)
        {
            var img = Go.GetComponent<Image>() ?? Go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = raycast;
            return this;
        }

        // абсолютный размер свободного узла (вне лейаута): sizeDelta напрямую.
        // Size/FixedHeight работают только под LayoutGroup — у свободного узла
        // LayoutElement никто не читает
        public UiBox Absolute(float width, float height)
        {
            Rect.sizeDelta = new Vector2(width, height);
            return this;
        }

        public UiBox Frame(Color color, float thickness = 2f)
        {
            Ui.Frame(Go, color, thickness);
            return this;
        }

        public UiBox RectBorder(Color color, float thickness = 2f)
        {
            Ui.RectBorder(Go, color, thickness);
            return this;
        }

        // --- лейаут детей (один на узел — вызывать один из трёх) ---

        public UiBox Column(float spacing = 4f, TextAnchor alignment = TextAnchor.UpperLeft)
        {
            var layout = Go.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = alignment;
            return this;
        }

        public UiBox Row(float spacing = 8f, TextAnchor alignment = TextAnchor.MiddleLeft, bool expandHeight = true)
        {
            var layout = Go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = expandHeight;
            layout.childAlignment = alignment;
            return this;
        }

        public UiBox Padding(int left, int right, int top, int bottom)
        {
            var layout = Go.GetComponent<HorizontalOrVerticalLayoutGroup>()
                         ?? throw new System.InvalidOperationException("Padding: сначала Column/Row");
            layout.padding = new RectOffset(left, right, top, bottom);
            return this;
        }

        // --- дети: примитивы возвращают их GameObject (для ссылок на TMP/Button) ---

        public TMP_Text Label(string text, float size, Color color, TMP_FontAsset font = null, string name = "Text")
        {
            return Ui.Label(Rect, text, size, color, font, name);
        }

        public Button Button(string label, Color background, Color textColor, float size,
            UnityEngine.Events.UnityAction onClick = null, TMP_FontAsset font = null, string name = null)
        {
            var button = Ui.Button(Rect, label, background, textColor, size, font, name);
            if (onClick != null)
                button.onClick.AddListener(onClick);
            return button;
        }

        public Image Image(Sprite sprite, Color color, string name = "Image")
        {
            return Ui.Image(Rect, sprite, color, name);
        }

        // вложенная панель — бокс её ребёнка (чейны продолжаются на ребёнке)
        public UiBox Box(string name)
        {
            return UiBox.Box(Rect, name);
        }

        // усыновить узел, построенный вручную
        public UiBox Add(GameObject child)
        {
            child.transform.SetParent(Rect, false);
            return this;
        }
    }
}
