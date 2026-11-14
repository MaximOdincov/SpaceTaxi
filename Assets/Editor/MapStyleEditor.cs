using System.Collections.Generic;
using SpaceTaxi.UI;
using UnityEditor;
using UnityEngine;

namespace SpaceTaxi.Tools
{
    // Редактор стиля карты (запрос владельца после Н8): правки MapStyleConfig
    // применяются к живой карте кнопкой/авто; свои надписи и контуры ставятся
    // кликом по карте (режим размещения) и живут в ассете стиля. Расположение
    // планет НЕ редактируется — оно в LevelAsset (Level Editor), зафиксировано.
    // Открытие: Space Taxi → Map Style Editor.
    public sealed class MapStyleEditor : EditorWindow
    {
        private MapStyleConfig style;
        private MapScreenBinder binder;
        private bool autoApply = true;
        private readonly List<bool> folds = new List<bool>();

        [MenuItem("Space Taxi/Map Style Editor")]
        private static void Open()
        {
            GetWindow<MapStyleEditor>("Map Style").minSize = new Vector2(420f, 520f);
        }

        private void OnEnable()
        {
            ResolveRefs();
        }

        private void ResolveRefs()
        {
            binder = FindAnyObjectByType<MapScreenBinder>();
            if (style == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:MapStyleConfig");
                if (guids.Length > 0)
                    style = AssetDatabase.LoadAssetAtPath<MapStyleConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }
            if (style == null && binder != null)
            {
                MapStyleConfig live = binder.Style; // может быть runtime-дефолтом — не ассет
                if (live != null && AssetDatabase.Contains(live))
                    style = live;
            }
        }

        private void OnGUI()
        {
            ResolveRefs();
            DrawHeader();
            if (style == null)
            {
                EditorGUILayout.HelpBox("Стиль не найден — создай ассет кнопкой выше. " +
                    "Он ляжет в Assets/Resources/MapStyle_Default.asset и подхватится картой автоматически.", MessageType.Warning);
                return;
            }
            DrawDiffSection();

            var so = new SerializedObject(style);
            EditorGUI.BeginChangeCheck();
            DrawStyle(so);
            bool changed = EditorGUI.EndChangeCheck();
            if (changed)
            {
                so.ApplyModifiedProperties();
                if (autoApply)
                    ApplyToMap();
            }

            EditorGUILayout.Space(6f);
            DrawAnnotations(so);
            if (EditorGUI.EndChangeCheck())
            {
                so.ApplyModifiedProperties();
                if (autoApply)
                    ApplyToMap();
            }

            EditorGUILayout.Space(8f);
            DrawPlacementBar();
            EditorGUILayout.HelpBox(
                "Как пользоваться:\n" +
                "• Ручки выше меняются кнопкой «Перерисовать карту» (или авто-применением) — карта должна быть открыта (M в пле).\n" +
                "• «Клик: …» включает режим размещения — левый клик по бумаге ставит аннотацию в точку мира, правый отменяет. Текст/цвет/размер правятся в списке выше.\n" +
                "• Окно видимости minZoom/maxZoom: 0 = без границы. Контур — в юнитах мира (растёт с зумом), надпись может быть экранного размера (галочка).\n" +
                "• Аннотации сохраняются в ассет стиля и переживают выход из плея.\n" +
                "• Расположение планет здесь не меняется — только Level Editor.", MessageType.Info);
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField("Стиль карты (MapStyleConfig)", EditorStyles.boldLabel);
            // статус превью: правки видны сразу только при открытой карте
            string status = binder == null
                ? "GameScene не открыта — открой сцену и пле, затем карту (M)"
                : binder.IsBuilt
                    ? "КАРТА ОТКРЫТА — правки видны на ней сразу"
                    : "Карта закрыта — правки применятся при следующем открытии (M в пле)";
            EditorGUILayout.LabelField(status, EditorStyles.miniLabel);
            EditorGUILayout.BeginHorizontal();
            style = (MapStyleConfig)EditorGUILayout.ObjectField("Ассет", style, typeof(MapStyleConfig), false);
            if (GUILayout.Button("Создать", GUILayout.Width(70f)))
                CreateAsset();
            if (GUILayout.Button("В карту сцены", GUILayout.Width(100f)))
                AssignToBinder();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            autoApply = EditorGUILayout.ToggleLeft("Авто-применение", autoApply);
            if (GUILayout.Button("Перерисовать карту", GUILayout.Width(140f)))
                ApplyToMap();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4f);
        }

        // «что я наменял»: сравнение с заводскими дефолтами (аннотации не сравниваются —
        // это контент владельца, не стиль)
        private void DrawDiffSection()
        {
            var defs = CreateInstance<MapStyleConfig>();
            var changes = new List<string>();
            foreach (var f in typeof(MapStyleConfig).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (f.Name == "annotations")
                    continue;
                object cur = f.GetValue(style);
                object def = f.GetValue(defs);
                bool same = cur == null ? def == null : cur.Equals(def);
                if (!same)
                    changes.Add($"{Label(f.Name)}: {Fmt(def)} → {Fmt(cur)}");
            }
            DestroyImmediate(defs);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"Что изменено против заводского стиля: {changes.Count}", EditorStyles.boldLabel);
            if (changes.Count == 0)
            {
                EditorGUILayout.LabelField("пока ничего — все ручки на дефолтах.", EditorStyles.miniLabel);
            }
            else
            {
                _diffScroll = EditorGUILayout.BeginScrollView(_diffScroll, GUILayout.MaxHeight(84f));
                foreach (string c in changes)
                    EditorGUILayout.LabelField("• " + c, EditorStyles.miniLabel);
                EditorGUILayout.EndScrollView();
                if (GUILayout.Button("Сбросить ручки на заводские (аннотации останутся)"))
                    ResetStyleKnobs();
            }
            EditorGUILayout.LabelField($"Своих надписей и контуров: {style.annotations.Count} шт.", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4f);
        }

        // сброс визуальных ручек к дефолтам через SerializedObject (аннотации и
        // m_Script не копируются)
        private void ResetStyleKnobs()
        {
            var defs = CreateInstance<MapStyleConfig>();
            var so = new SerializedObject(style);
            var dso = new SerializedObject(defs);
            var it = dso.GetIterator();
            while (it.NextVisible(true))
            {
                if (it.name == "m_Script" || it.name == "annotations")
                    continue;
                so.CopyFromSerializedProperty(it); // копирует в свойство с тем же именем
            }
            so.ApplyModifiedProperties();
            DestroyImmediate(defs);
            AssetDatabase.SaveAssetIfDirty(style);
            ApplyToMap();
            Repaint();
        }

        private static Vector2 _diffScroll;

        private static string Label(string field)
        {
            switch (field)
            {
                case "fontOverride": return "Шрифт";
                case "labelFontSize": return "Кегль подписей тел";
                case "labelColor": return "Цвет подписей тел";
                case "districtFontSize": return "Кегль районов";
                case "districtColor": return "Цвет районов";
                case "districtFadeOutFraction": return "Районы гаснут при";
                case "targetRingPx": return "Кольцо цели, px";
                case "targetRingWidePx": return "Широкое кольцо цели, px";
                case "targetColor": return "Цель — цвет";
                case "waypointHaloPx": return "Кольцо метки, px";
                case "waypointWidePx": return "Широкое кольцо метки, px";
                case "waypointPinPx": return "Булавка, px";
                case "waypointColor": return "Метка — цвет";
                case "legendVisible": return "Легенда видна";
                case "legendSize": return "Размер легенды";
                case "titleVisible": return "Заголовок виден";
                case "titleText": return "Заголовок — окончание";
                case "fogInnerFrac": return "Туман: начало";
                case "paperWorldMargin": return "Поля бумаги";
                case "worldUnitsPerTile": return "Плотность зерна";
                case "maxViewZoom": return "Потолок зума";
                case "initialViewZoom": return "Начальный зум";
                case "rulerMinZoomFraction": return "Линейка при";
                case "zoomButtonScale": return "Кнопки зума ×";
                default: return field;
            }
        }

        private static string Fmt(object value)
        {
            if (value == null)
                return "сцена";
            if (value is Color c)
                return "#" + ColorUtility.ToHtmlStringRGB(c) + (c.a < 0.999f ? $" a={c.a:0.00}" : "");
            if (value is Vector2 v)
                return $"({v.x:0.#}; {v.y:0.#})";
            if (value is bool b)
                return b ? "вкл" : "выкл";
            if (value is string s)
                return "«" + s + "»";
            if (value is Object u)
                return u.name;
            if (value is float f)
                return f.ToString("0.##");
            return value.ToString();
        }

        private void DrawStyle(SerializedObject so)
        {
            EditorGUILayout.LabelField("Шрифт и подписи", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(so.FindProperty("fontOverride"), new GUIContent("Шрифт (null = Excalifont сцены)"));
            EditorGUILayout.PropertyField(so.FindProperty("labelFontSize"), new GUIContent("Кегль подписей тел"));
            EditorGUILayout.PropertyField(so.FindProperty("labelColor"), new GUIContent("Цвет подписей тел"));

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Районы", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(so.FindProperty("districtFontSize"), new GUIContent("Кегль районов"));
            EditorGUILayout.PropertyField(so.FindProperty("districtColor"), new GUIContent("Цвет районов"));
            EditorGUILayout.PropertyField(so.FindProperty("districtFadeOutFraction"), new GUIContent("Гаснут при contentScale ≥"));

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Цель заказа и метка", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(so.FindProperty("targetRingPx"), new GUIContent("Кольцо цели, px"));
            EditorGUILayout.PropertyField(so.FindProperty("targetRingWidePx"), new GUIContent("Широкое кольцо цели, px"));
            EditorGUILayout.PropertyField(so.FindProperty("targetColor"), new GUIContent("Цель — цвет (жёлтый)"));
            EditorGUILayout.PropertyField(so.FindProperty("waypointHaloPx"), new GUIContent("Кольцо метки, px"));
            EditorGUILayout.PropertyField(so.FindProperty("waypointWidePx"), new GUIContent("Широкое кольцо метки, px"));
            EditorGUILayout.PropertyField(so.FindProperty("waypointPinPx"), new GUIContent("Булавка, px"));
            EditorGUILayout.PropertyField(so.FindProperty("waypointColor"), new GUIContent("Метка — цвет (зелёный)"));

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Легенда, заголовок, край мира", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(so.FindProperty("legendVisible"), new GUIContent("Легенда видна"));
            EditorGUILayout.PropertyField(so.FindProperty("legendSize"), new GUIContent("Размер легенды"));
            EditorGUILayout.PropertyField(so.FindProperty("titleVisible"), new GUIContent("Заголовок виден"));
            EditorGUILayout.PropertyField(so.FindProperty("titleText"), new GUIContent("Заголовок — окончание"));
            EditorGUILayout.PropertyField(so.FindProperty("fogInnerFrac"), new GUIContent("Туман: начало (доля радиуса)"));

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Бумага и зум", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(so.FindProperty("paperWorldMargin"), new GUIContent("Поля бумаги (× радиуса мира)"));
            EditorGUILayout.PropertyField(so.FindProperty("worldUnitsPerTile"), new GUIContent("Плотность зерна, юнитов/тайл"));
            EditorGUILayout.PropertyField(so.FindProperty("maxViewZoom"), new GUIContent("Потолок зума"));
            EditorGUILayout.PropertyField(so.FindProperty("initialViewZoom"), new GUIContent("Начальный зум"));
            EditorGUILayout.PropertyField(so.FindProperty("rulerMinZoomFraction"), new GUIContent("Линейка при contentScale ≥"));
            EditorGUILayout.PropertyField(so.FindProperty("zoomButtonScale"), new GUIContent("Кнопки зума ×"));
        }

        private void DrawAnnotations(SerializedObject so)
        {
            SerializedProperty list = so.FindProperty("annotations");
            EditorGUILayout.LabelField($"Свои надписи и контуры ({list.arraySize})", EditorStyles.boldLabel);
            while (folds.Count < list.arraySize)
                folds.Add(false);

            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty el = list.GetArrayElementAtIndex(i);
                var ann = style.annotations[i];
                string caption = $"{i + 1}. {KindName(ann.kind)} — {(string.IsNullOrEmpty(ann.text) ? KindName(ann.kind) : ann.text)}";
                folds[i] = EditorGUILayout.Foldout(folds[i], caption, true);
                if (!folds[i])
                    continue;

                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(el.FindPropertyRelative("kind"), new GUIContent("Тип"));
                EditorGUILayout.PropertyField(el.FindPropertyRelative("text"), new GUIContent("Текст"));
                EditorGUILayout.PropertyField(el.FindPropertyRelative("worldPosition"), new GUIContent("Позиция (мир)"));

                if (ann.kind == AnnotationKind.Label)
                {
                    EditorGUILayout.PropertyField(el.FindPropertyRelative("fontSize"), new GUIContent("Кегль"));
                    EditorGUILayout.PropertyField(el.FindPropertyRelative("screenFixedSize"), new GUIContent("Экранный размер (не растёт с зумом)"));
                }
                else
                {
                    EditorGUILayout.PropertyField(el.FindPropertyRelative("radius"),
                        ann.kind == AnnotationKind.Rect ? new GUIContent("Полширины, юнитов") : new GUIContent("Радиус, юнитов"));
                    if (ann.kind == AnnotationKind.Rect)
                        EditorGUILayout.PropertyField(el.FindPropertyRelative("height"), new GUIContent("Полвысоты, юнитов"));
                    if (ann.kind == AnnotationKind.Rect)
                        EditorGUILayout.PropertyField(el.FindPropertyRelative("thicknessPx"), new GUIContent("Толщина линий, px"));
                }

                EditorGUILayout.PropertyField(el.FindPropertyRelative("color"), new GUIContent("Цвет"));
                EditorGUILayout.PropertyField(el.FindPropertyRelative("minZoom"), new GUIContent("Видима при зуме ≥ (0 = всегда)"));
                EditorGUILayout.PropertyField(el.FindPropertyRelative("maxZoom"), new GUIContent("Видима при зуме ≤ (0 = всегда)"));

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Удалить", GUILayout.Width(80f)))
                {
                    list.DeleteArrayElementAtIndex(i);
                    folds.RemoveAt(i);
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button("К центру вида", GUILayout.Width(110f)))
                    el.FindPropertyRelative("worldPosition").vector2Value = Vector2.zero;
                EditorGUILayout.EndHorizontal();
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(2f);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Надпись"))
                AppendAnnotation(so, AnnotationKind.Label);
            if (GUILayout.Button("+ Кольцо"))
                AppendAnnotation(so, AnnotationKind.Ring);
            if (GUILayout.Button("+ Круг"))
                AppendAnnotation(so, AnnotationKind.Circle);
            if (GUILayout.Button("+ Рамка"))
                AppendAnnotation(so, AnnotationKind.Rect);
            EditorGUILayout.EndHorizontal();
        }

        private void AppendAnnotation(SerializedObject so, AnnotationKind kind)
        {
            SerializedProperty list = so.FindProperty("annotations");
            list.arraySize++;
            SerializedProperty el = list.GetArrayElementAtIndex(list.arraySize - 1);
            el.FindPropertyRelative("kind").enumValueIndex = (int)kind;
            el.FindPropertyRelative("text").stringValue = KindName(kind);
            el.FindPropertyRelative("worldPosition").vector2Value = Vector2.zero;
            folds.Add(true);
            so.ApplyModifiedProperties();
            ApplyToMap();
        }

        private void DrawPlacementBar()
        {
            EditorGUILayout.LabelField("Размещение кликом", EditorStyles.boldLabel);
            bool anyMode = MapScreenBinder.AnnotationPlacementMode;
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Toggle(anyMode && MapScreenBinder.PlacementKind == AnnotationKind.Label, "Клик: надпись", "Button"))
                StartPlacement(AnnotationKind.Label);
            if (GUILayout.Toggle(anyMode && MapScreenBinder.PlacementKind == AnnotationKind.Ring, "Клик: кольцо", "Button"))
                StartPlacement(AnnotationKind.Ring);
            if (GUILayout.Toggle(anyMode && MapScreenBinder.PlacementKind == AnnotationKind.Circle, "Клик: круг", "Button"))
                StartPlacement(AnnotationKind.Circle);
            if (GUILayout.Toggle(anyMode && MapScreenBinder.PlacementKind == AnnotationKind.Rect, "Клик: рамка", "Button"))
                StartPlacement(AnnotationKind.Rect);
            EditorGUILayout.EndHorizontal();
            if (anyMode)
                EditorGUILayout.HelpBox("Режим включён: левый клик по бумаге (карта должна быть открыта) поставит " +
                    KindName(MapScreenBinder.PlacementKind) + ", правый — отменит. Режим снимается после постановки.", MessageType.None);
        }

        private void StartPlacement(AnnotationKind kind)
        {
            MapScreenBinder.AnnotationPlacementMode = true;
            MapScreenBinder.PlacementKind = kind;
            Repaint();
        }

        private void ApplyToMap()
        {
            if (binder == null)
                binder = FindAnyObjectByType<MapScreenBinder>();
            if (binder != null)
                binder.RefreshMapStyle();
        }

        private void CreateAsset()
        {
            string path = "Assets/Resources/MapStyle_Default.asset";
            int n = 1;
            while (AssetDatabase.LoadMainAssetAtPath(path) != null)
                path = $"Assets/Resources/MapStyle_Default_{n++}.asset";
            style = CreateInstance<MapStyleConfig>();
            AssetDatabase.CreateAsset(style, path);
            AssetDatabase.SaveAssets();
            AssignToBinder();
        }

        private void AssignToBinder()
        {
            if (binder == null)
                binder = FindAnyObjectByType<MapScreenBinder>();
            if (binder == null)
            {
                EditorUtility.DisplayDialog("Map Style", "Открой GameScene — биндер карты не найден.", "Ок");
                return;
            }
            var so = new SerializedObject(binder);
            so.FindProperty("style").objectReferenceValue = style;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(binder);
            ApplyToMap();
        }

        private static string KindName(AnnotationKind kind)
        {
            switch (kind)
            {
                case AnnotationKind.Ring: return "кольцо";
                case AnnotationKind.Circle: return "круг";
                case AnnotationKind.Rect: return "рамка";
                default: return "надпись";
            }
        }
    }
}
