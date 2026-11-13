using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

namespace SpaceTaxi.UI
{
    // Точка входа ревампа главного меню (бриф E, «лицо игры»). ВСЁ строится кодом
    // при загрузке сцены MainMenu — .unity не правится: спавн через
    // RuntimeInitializeOnLoadMethod + подписка на sceneLoaded (обе точки
    // покрывают и запуск сразу в меню, и возврат из игры).
    //
    // Монтируется первым ребёнком канваса UI_Root, поэтому рисуется ПОД сценическим
    // UI (TerminalPanel/SettingsUI поверх — их биндинги и переходы не тронуты):
    //
    //   MenuBackdrop (контроллер, ловит клики по пустому месту)
    //   ├── ClickCatcher      — невидимый Image на весь экран (IPointerClickHandler)
    //   ├── Nebula            — туманность (медленный дрейф)
    //   ├── StarsFar          — дальние звёзды (тусклые, мерцают медленно)
    //   ├── StarsMid          — средние
    //   ├── StarsNear         — ближние яркие (мерцают быстро)
    //   └── EggShip           — яйцо-такси (клик = точная цель)
    //
    // Слои живут ТОЛЬКО автономным дрейфом (E8.3: мышиный параллакс убран —
    // владельца «подташнивает»): sway + медленное плавание в противофазе (E4);
    // запас площади слоя ±400px скрывает края. Клик по экрану — ловушка
    // кликов передаёт точку яйцу (MenuEggShip); кнопки перехватывают свои
    // клики через ClickTarget в StarButtonDecorator (Н6). Название —
    // MenuStarTitle, кнопки — StarButtonDecorator (E2/E3 + Н6: без плашек,
    // шрифта и рамки — только звёздный текст; E8.2: под наведением жёлтеют
    // САМИ звёзды, ореолов-подложек нет).
    // Зелёные заливки прошлой итерации
    // (Frame/Background панели) и оранжевый мокап-квадрат «Image» гасятся:
    // фон меню — только живой звёздный слой; это чистый визуал без скриптов,
    // биндинги не затронуты.
    public sealed class MenuBackdropController : MonoBehaviour, IPointerClickHandler
    {
        private const string SceneName = "MainMenu";

        private static MenuBackdropController active;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            TrySpawn(SceneManager.GetActiveScene()); // запуск сразу в меню
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => TrySpawn(scene);

        private static void TrySpawn(Scene scene)
        {
            if (scene.name != SceneName || active != null)
                return;

            GameObject canvasGo = GameObject.Find("UI_Root");
            if (canvasGo == null || !canvasGo.TryGetComponent(out Canvas canvas) || !canvas.isRootCanvas)
            {
                Debug.LogWarning("[MenuBackdrop] канвас UI_Root не найден — фон меню пропущен");
                return;
            }

            var go = new GameObject("MenuBackdrop", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            go.transform.SetSiblingIndex(0); // позади MainMenuUI и SettingsUI
            active = go.AddComponent<MenuBackdropController>();
            HidePlaceholder(canvasGo.transform);
            HidePanelFills(canvasGo.transform);
        }

        // бриф E1: зелёная заливка прошлой итерации — TerminalPanel несёт Frame
        // (мятный) и Background (тёмно-зелёный), растянутые с запасом за пределы
        // панели (sizeDelta ~1324x276) поверх звёзд. Обе плоские заливки
        // (Image без спрайта, у объектов нет скриптов) гасим — фон меню только
        // звёздный параллакс; контент панели (логотип, кнопки, футер) не тронут
        private static void HidePanelFills(Transform canvas)
        {
            Transform panel = canvas.Find("MainMenuUI/TerminalPanel");
            if (panel == null)
                return;
            for (int i = 0; i < panel.childCount; i++)
            {
                Transform child = panel.GetChild(i);
                if (child.name != "Frame" && child.name != "Background")
                    continue;
                if (child.TryGetComponent(out Image image) && image.sprite == null)
                {
                    child.gameObject.SetActive(false);
                    Debug.Log($"[MenuBackdrop] '{child.name}' панели терминала скрыт (зелёная заливка прошлой итерации)");
                }
            }
        }

        private static void HidePlaceholder(Transform canvas)
        {
            Transform placeholder = canvas.Find("Image");
            if (placeholder == null)
                return;
            // страховка: гасим только чистый мокап — ровно 3 компонента
            // (RectTransform, CanvasRenderer, Image), никаких скриптов
            Component[] components = placeholder.GetComponents<Component>();
            if (components.Length != 3 || placeholder.GetComponent<Image>() == null)
                return;
            placeholder.gameObject.SetActive(false);
            Debug.Log("[MenuBackdrop] мокап-квадрат 'Image' скрыт (заменён живым фоном)");
        }

        private readonly List<MenuStarLayer> layers = new();
        private RectTransform rect;
        private Canvas canvas;
        private MenuEggShip egg;
        private bool contentBuilt;

        // палитры: тёмный космос + яркие акценты борткомпа (мята/такси-жёлтый)
        private static readonly Color[] NebulaPalette =
        {
            new(0.16f, 0.20f, 0.45f), new(0.28f, 0.14f, 0.42f),
            new(0.10f, 0.28f, 0.36f), new(0.20f, 0.12f, 0.34f)
        };
        private static readonly Color[] StarPalette =
        {
            new(1f, 1f, 1f), new(0.72f, 0.82f, 1f),
            new(0.659f, 1f, 0.796f), new(1f, 0.94f, 0.80f)
        };
        private static readonly Color[] BrightPalette =
        {
            new(1f, 1f, 1f), new(0.659f, 1f, 0.796f),
            new(1f, 0.831f, 0.302f), new(0.85f, 0.92f, 1f)
        };

        private void Awake()
        {
            canvas = GetComponentInParent<Canvas>();
            rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            // ловушка кликов: прозрачный Image; кнопки поверх перехватывают свои
            // клики (raycast берёт верхний таргет), пустое место — сюда
            var catcher = new GameObject("ClickCatcher", typeof(RectTransform), typeof(Image));
            catcher.transform.SetParent(transform, false);
            var catcherRect = (RectTransform)catcher.transform;
            catcherRect.anchorMin = Vector2.zero;
            catcherRect.anchorMax = Vector2.one;
            catcherRect.offsetMin = Vector2.zero;
            catcherRect.offsetMax = Vector2.zero;
            Image catcherImage = catcher.GetComponent<Image>();
            catcherImage.color = new Color(0f, 0f, 0f, 0f);
            catcherImage.raycastTarget = true;

            BuildLayers();
        }

        private void BuildLayers()
        {
            // глубина как в игре (E4): дальние — тусклее, мельче и мерцают
            // медленно; ближние — ярче, крупнее, мерцают быстро и искрят.
            // PhaseShift разводит слои по фазе (far 0.75π vs near 1.75π —
            // противофаза), медленное плавание Drift* — у слоёв встречные
            // направления: глубина читается без мыши (E8.3)
            layers.Add(MakeLayer("Nebula", new MenuStarLayer.Config
            {
                Count = 5, SizeMin = 700f, SizeMax = 1400f,
                AlphaMin = 0.09f, AlphaMax = 0.16f, TwinkleAmp = 0.25f,
                SpeedMin = 0.10f, SpeedMax = 0.30f, SparkleChance = 0f,
                SoftCore = 0f,
                SwayAmplitude = 10f, SwaySpeed = 0.05f,
                PhaseShift = 0f,
                DriftAmplitude = 30f, DriftSpeed = 0.045f, DriftAngleDeg = 15f,
                Palette = NebulaPalette
            }));
            layers.Add(MakeLayer("StarsFar", new MenuStarLayer.Config
            {
                Count = 80, SizeMin = 5f, SizeMax = 13f,
                AlphaMin = 0.22f, AlphaMax = 0.55f, TwinkleAmp = 0.30f,
                SpeedMin = 0.5f, SpeedMax = 1.3f, SparkleChance = 0.06f,
                SwayAmplitude = 8f, SwaySpeed = 0.08f,
                PhaseShift = Mathf.PI * 0.75f,
                DriftAmplitude = 36f, DriftSpeed = 0.06f, DriftAngleDeg = 200f,
                Palette = StarPalette
            }));
            layers.Add(MakeLayer("StarsMid", new MenuStarLayer.Config
            {
                Count = 36, SizeMin = 10f, SizeMax = 24f,
                AlphaMin = 0.35f, AlphaMax = 0.85f, TwinkleAmp = 0.34f,
                SpeedMin = 0.9f, SpeedMax = 2.2f, SparkleChance = 0.30f,
                SwayAmplitude = 12f, SwaySpeed = 0.10f,
                PhaseShift = Mathf.PI * 0.25f,
                DriftAmplitude = 46f, DriftSpeed = 0.08f, DriftAngleDeg = 340f,
                Palette = StarPalette
            }));
            layers.Add(MakeLayer("StarsNear", new MenuStarLayer.Config
            {
                Count = 14, SizeMin = 22f, SizeMax = 44f,
                AlphaMin = 0.70f, AlphaMax = 1f, TwinkleAmp = 0.42f,
                SpeedMin = 1.4f, SpeedMax = 3.2f, SparkleChance = 0.7f,
                SwayAmplitude = 14f, SwaySpeed = 0.12f,
                PhaseShift = Mathf.PI * 1.75f,
                DriftAmplitude = 58f, DriftSpeed = 0.10f, DriftAngleDeg = 150f,
                Palette = BrightPalette
            }));
        }

        private MenuStarLayer MakeLayer(string layerName, MenuStarLayer.Config config)
        {
            var go = new GameObject(layerName, typeof(RectTransform), typeof(MenuStarLayer));
            go.transform.SetParent(transform, false);
            var layerRect = (RectTransform)go.transform;
            layerRect.anchorMin = Vector2.zero;
            layerRect.anchorMax = Vector2.one;
            layerRect.offsetMin = new Vector2(-400f, -400f); // запас под дрейф
            layerRect.offsetMax = new Vector2(400f, 400f);
            MenuStarLayer layer = go.GetComponent<MenuStarLayer>();
            layer.Configure(config);
            return layer;
        }

        // Контент, зависящий от размера канваса: рассыпаем звёзды, ставим яйцо,
        // вешаем декор на логотип и кнопки. Вызывается из Update, когда канвас
        // уже разложен (на первом кадре rect ещё нулевой)
        private void BuildContent()
        {
            foreach (MenuStarLayer layer in layers)
                layer.Build();
            BuildEgg();
            BuildDecorations();
        }

        private void BuildEgg()
        {
            var go = new GameObject("EggShip", typeof(RectTransform), typeof(Image), typeof(MenuEggShip));
            go.transform.SetParent(transform, false); // последний ребёнок — над звёздами
            var eggRect = (RectTransform)go.transform;
            eggRect.anchorMin = new Vector2(0.5f, 0.5f);
            eggRect.anchorMax = new Vector2(0.5f, 0.5f);
            eggRect.sizeDelta = new Vector2(96f, 96f); // бриф E5: было 220 — скромнее и ленивее

            Image image = go.GetComponent<Image>();
            Sprite sprite = MenuEggShip.LoadSprite();
            if (sprite == null)
            {
                Debug.LogWarning("[MenuBackdrop] спрайт яйца не найден (Art/ships/яйцо/яйцо1)");
                image.enabled = false;
            }
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false; // клики проходят к ловушке

            egg = go.GetComponent<MenuEggShip>();
            egg.Init(rect.rect.size);
        }

        private void BuildDecorations()
        {
            Transform panel = canvas.transform.Find("MainMenuUI/TerminalPanel");
            if (panel == null)
            {
                Debug.LogWarning("[MenuBackdrop] TerminalPanel не найден — декор пропущен");
                return;
            }

            Transform logo = panel.Find("LogoText");
            if (logo != null && logo.TryGetComponent(out TMP_Text _))
            {
                var title = new GameObject("MenuStarTitle", typeof(MenuStarTitle)).GetComponent<MenuStarTitle>();
                title.transform.SetParent(transform, false); // управление временем — тут; визуал — в панели
                title.Build((RectTransform)logo);
            }

            var buttonNames = new[] { "Button_NewGame", "Button_Continue", "Button_Settings", "Button_Exit" };
            foreach (string buttonName in buttonNames)
            {
                Transform button = panel.Find(buttonName);
                if (button == null)
                    continue;
                StarButtonDecorator decorator = button.gameObject.AddComponent<StarButtonDecorator>();
                decorator.Build();
            }
        }

        private void Update()
        {
            if (!contentBuilt)
            {
                if (rect.rect.width < 2f)
                    return; // канвас ещё не разложился
                contentBuilt = true;
                BuildContent();
            }

            // E8.3: мышиный параллакс убран (владелец: «подташнивает») — слои
            // живут только автономным дрейфом: sway + медленное плавание.
            // Unscaled — меню не зависит от timeScale.
            float time = Time.unscaledTime;
            for (int i = 0; i < layers.Count; i++)
            {
                MenuStarLayer layer = layers[i];
                Vector2 sway = new(
                    Mathf.Sin(time * layer.SwaySpeed) * layer.SwayAmplitude,
                    Mathf.Cos(time * layer.SwaySpeed * 0.7f) * layer.SwayAmplitude);
                // медленное плавание слоя в своём направлении (E4): период ~минута,
                // у слоёв встречные углы — слои расходятся/сходятся, глубина
                // читается без мыши; фазовый сдвиг i*2.4 рад разводит соседние
                float drift = Mathf.Sin(time * layer.DriftSpeed + i * 2.4f) * layer.DriftAmplitude;
                Vector2 driftOffset = new(Mathf.Cos(layer.DriftAngleRad) * drift, Mathf.Sin(layer.DriftAngleRad) * drift);
                layer.ApplyOffset(sway + driftOffset);
            }
        }

        // клик по пустому месту — яйцо летит в точку (E.3); клики по кнопкам
        // перехватываются кнопками и сюда не доходят
        public void OnPointerClick(PointerEventData eventData)
        {
            if (egg == null || !egg.Ready)
                return;
            // оверлей-канвас: worldCamera == null
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, eventData.position, canvas.worldCamera, out Vector2 local))
                egg.FlyTo(local);
        }
    }
}
