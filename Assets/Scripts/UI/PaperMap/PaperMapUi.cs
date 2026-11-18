using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceTaxi.Core;

namespace SpaceTaxi.UI.PaperMap
{
    // — подсистема хелперов бумажной карты (Н11): общий слой «чернил и ректов» —
    // Статические утилиты, которыми пользовались все разделы построения карты:
    // хром листа, подписи, кольца, аннотации. Тела перенесены из MapScreenBinder
    // дословно (Н11); MakeLabel требует шрифт карты — передаётся явно
    // (ResolveFont живёт в хосте).
    internal static class PaperMapUi
    {
        // — лист и бортовой компьютер (Н5.21) — геометрия окна, общая для хрома
        // (BuildChrome/LayoutSheet) и вида (SheetClampView) — Н11
        internal const float SheetMargin = 0f;   // поля листа от краёв доступной области
        internal const float ContentMargin = 0f; // окно вида внутри листа (маска клипует всё внутри)

        // альфа-фейд подписей на смене тира (M7.5) — общий темп для подписей,
        // линейки и районов — Н11
        internal const float LabelFadeSeconds = 0.12f;

        // отрезок чернил: капсула StubSprites.Line между двумя точками
        internal static RectTransform MakeSegment(RectTransform parent, Vector2 a, Vector2 b, Color color, float thickness)
        {
            var go = new GameObject("Ink", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Image img = go.GetComponent<Image>();
            img.sprite = StubSprites.Line();
            img.color = color;
            img.raycastTarget = false; // штрихи рамок не едят клики (иначе рамка района блокирует «клик мимо»)
            RectTransform rect = (RectTransform)go.transform;
            Vector2 mid = (a + b) * 0.5f;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2((b - a).magnitude + thickness * 0.9f, thickness);
            rect.anchoredPosition = mid - (Vector2)parent.rect.center;
            rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
            return rect;
        }

        // Н12.4: один FNV-1a на проект — Economy.DeterministicHash
        internal static int StableHash(string text) => SpaceTaxi.Economy.DeterministicHash.StableHash(text);

        internal static Rect CenteredRect(Vector2 center, Vector2 size)
        {
            return new Rect(center - size * 0.5f, size);
        }

        internal static void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
                Object.Destroy(t.GetChild(i).gameObject); // static-класс: не MonoBehaviour (Н11)
        }

        internal static RectTransform MakeRect(string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return rect;
        }

        internal static RectTransform MakeImage(string name, Transform parent, Sprite sprite, Color color, Vector2 size, bool raycast)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Image image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = raycast;
            RectTransform rect = (RectTransform)go.transform;
            rect.sizeDelta = size;
            return rect;
        }

        // все подписи карты одним рукописным шрифтом (Н5.21-3 — только Excalifont);
        // высоты узлов ≥ fontSize*1.32 (урок ночи 4)
        internal static TMP_Text MakeLabel(TMP_FontAsset font, string name, Transform parent, string text, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
            if (font != null)
                label.font = font;
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = align;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            return label;
        }

        internal static Color WithAlpha(Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, alpha);
        }

        // (Н11-step1-papermapui-applied)
    }
}
