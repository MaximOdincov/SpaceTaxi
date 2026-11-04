using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using SpaceTaxi.World;

namespace SpaceTaxi.Tools
{
    // Редактор уровня: список тел, параметры, спавн, валидация; гизмо — LevelEditorSceneView
    public sealed class LevelEditorWindow : EditorWindow
    {
        [SerializeField] private LevelAsset level;
        [SerializeField] private int selectedIndex = -1;
        [SerializeField] private bool snapEnabled = true;
        [SerializeField] private bool showGrid = true;
        [SerializeField] private bool showTrajectory = false;
        [SerializeField] private bool showGravityField = false;

        // буфер копирования тел между уровнями
        private static string bodyClipboard;

        private Vector2 listScroll;
        private static LevelEditorWindow open;

        public LevelAsset Level => level;
        public int SelectedIndex => selectedIndex;
        public bool SnapEnabled => snapEnabled;
        public bool ShowGrid => showGrid;
        public bool ShowTrajectory => showTrajectory;
        public bool ShowGravityField => showGravityField;

        // выбор тела из Scene View (клик/drag)
        public void SetSelected(int index)
        {
            selectedIndex = index;
            Repaint();
        }

        public static LevelEditorWindow OpenWindow
        {
            get
            {
                if (open == null)
                    open = Resources.FindObjectsOfTypeAll<LevelEditorWindow>().FirstOrDefault();
                return open;
            }
        }

        [MenuItem("Space Taxi/Level Editor")]
        public static void Open()
        {
            open = GetWindow<LevelEditorWindow>("Level Editor");
            open.minSize = new Vector2(360f, 520f);
        }

        private void OnEnable()
        {
            open = this;
        }

        private void OnDisable()
        {
            if (open == this)
                open = null;
        }

        private void OnGUI()
        {
            level = (LevelAsset)EditorGUILayout.ObjectField("Level Asset", level, typeof(LevelAsset), false);
            if (level == null)
            {
                EditorGUILayout.HelpBox("Выбери LevelAsset (например, ScriptableObjects/TestLevel01).", MessageType.Info);
                return;
            }

            var so = new SerializedObject(level);
            so.Update();

            EditorGUILayout.PropertyField(so.FindProperty("LevelName"));

            DrawBodyList(so);
            DrawBodyPanel(so);
            DrawSpawnBlock(so);
            DrawValidation();

            // стрелки двигают выбранное тело: 1 юнит, с Shift — 10
            HandleArrowKeys();

            if (so.ApplyModifiedProperties())
                SceneView.RepaintAll();
        }

        private void DrawBodyList(SerializedObject so)
        {
            var bodies = so.FindProperty("Bodies");

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Планета"))
            {
                AddBody(bodies, BodyType.Planet);
            }
            if (GUILayout.Button("+ Звезда"))
            {
                AddBody(bodies, BodyType.Star);
            }
            if (GUILayout.Button("+ Чёрная дыра"))
            {
                AddBody(bodies, BodyType.BlackHole);
            }
            if (GUILayout.Button("+ Пояс"))
            {
                AddBody(bodies, BodyType.AsteroidBelt);
            }
            GUI.enabled = selectedIndex >= 0 && selectedIndex < bodies.arraySize;
            if (GUILayout.Button("Дублировать") && selectedIndex >= 0)
            {
                bodies.InsertArrayElementAtIndex(selectedIndex);
                selectedIndex++;
                MarkNextDuplicateName(bodies, selectedIndex);
            }
            if (GUILayout.Button("Удалить") && selectedIndex >= 0)
            {
                bodies.DeleteArrayElementAtIndex(selectedIndex);
                selectedIndex = Mathf.Min(selectedIndex, bodies.arraySize - 1);
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = selectedIndex >= 0 && selectedIndex < bodies.arraySize;
            if (GUILayout.Button("Копировать") && selectedIndex >= 0)
            {
                bodyClipboard = EditorJsonUtility.ToJson(level.Bodies[selectedIndex]);
            }
            if (GUILayout.Button("Фокус") && selectedIndex >= 0)
            {
                FocusSelected();
            }
            GUI.enabled = bodyClipboard != null;
            if (GUILayout.Button("Вставить") && bodyClipboard != null)
            {
                PasteBody(bodies);
            }
            GUI.enabled = true;
            if (GUILayout.Button("Показать всё"))
            {
                FrameAll();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2f);
            listScroll = EditorGUILayout.BeginScrollView(listScroll, GUILayout.Height(150f));
            for (int i = 0; i < bodies.arraySize; i++)
            {
                var element = bodies.GetArrayElementAtIndex(i);
                string name = element.FindPropertyRelative("DisplayName").stringValue;
                var type = (BodyType)element.FindPropertyRelative("Type").intValue;
                float radius = element.FindPropertyRelative("Radius").floatValue;
                bool station = element.FindPropertyRelative("HasStation").boolValue;
                bool selected = i == selectedIndex;

                string icon = (type == BodyType.Star ? "★" : type == BodyType.BlackHole ? "◉" : type == BodyType.AsteroidBelt ? "◌" : "●") + (station ? "⛽" : "");
                GUIStyle style = selected ? EditorStyles.helpBox : EditorStyles.label;
                if (GUILayout.Button($"{icon}  {name}  (r={radius:0})", style))
                {
                    selectedIndex = i;
                    SceneView.RepaintAll();
                    GUI.FocusControl(null);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawBodyPanel(SerializedObject so)
        {
            var bodies = so.FindProperty("Bodies");
            if (selectedIndex < 0 || selectedIndex >= bodies.arraySize)
                return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Тело", EditorStyles.boldLabel);
            var element = bodies.GetArrayElementAtIndex(selectedIndex);
            var type = (BodyType)element.FindPropertyRelative("Type").intValue;

            EditorGUILayout.PropertyField(element.FindPropertyRelative("DisplayName"));
            EditorGUILayout.PropertyField(element.FindPropertyRelative("Type"));
            EditorGUILayout.PropertyField(element.FindPropertyRelative("Position"));
            EditorGUILayout.PropertyField(element.FindPropertyRelative("Radius"));
            EditorGUILayout.PropertyField(element.FindPropertyRelative("MassMultiplier"));
            if (type == BodyType.Star)
                EditorGUILayout.PropertyField(element.FindPropertyRelative("HeatRadius"));
            if (type == BodyType.AsteroidBelt)
            {
                EditorGUILayout.PropertyField(element.FindPropertyRelative("BeltWidth"),
                    new GUIContent("Ширина пояса (разброс)"));
                EditorGUILayout.PropertyField(element.FindPropertyRelative("BeltAsteroidCount"),
                    new GUIContent("Астероидов (24–40)"));
            }
            if (type == BodyType.Planet)
                EditorGUILayout.PropertyField(element.FindPropertyRelative("HasStation"),
                    new GUIContent("Станция (заправка+ремонт)"));
            EditorGUILayout.PropertyField(element.FindPropertyRelative("Sector"),
                new GUIContent("Сектор (оплата)"));

            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField("Кольца", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(element.FindPropertyRelative("LandingRingRadius"),
                new GUIContent("Посадочное (0 = r + 90)"));
            EditorGUILayout.PropertyField(element.FindPropertyRelative("CaptureRingRadius"),
                new GUIContent("Качели (0 = r * 3.3 + 30)"));

            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField("Внешний вид", EditorStyles.boldLabel);
            var visuals = element.FindPropertyRelative("Visuals");
            EditorGUILayout.PropertyField(visuals.FindPropertyRelative("Sprite"));
            EditorGUILayout.PropertyField(visuals.FindPropertyRelative("Material"));
            EditorGUILayout.PropertyField(visuals.FindPropertyRelative("UseColor"), new GUIContent("Красить цветом"));
            if (visuals.FindPropertyRelative("UseColor").boolValue)
                EditorGUILayout.PropertyField(visuals.FindPropertyRelative("Color"), new GUIContent("Цвет"));
            EditorGUILayout.PropertyField(visuals.FindPropertyRelative("SpinSpeed"), new GUIContent("Вращение, град/с"));
        }

        private void DrawSpawnBlock(SerializedObject so)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Старт игрока", EditorStyles.boldLabel);

            var bodies = so.FindProperty("Bodies");
            var modeProp = so.FindProperty("SpawnMode");
            EditorGUILayout.PropertyField(modeProp);

            // popup имён планет: показываем планеты, храним индекс в общем списке
            var indexProp = so.FindProperty("StartPlanetIndex");
            List<string> names = new List<string>();
            List<int> indices = new List<int>();
            for (int i = 0; i < bodies.arraySize; i++)
            {
                var element = bodies.GetArrayElementAtIndex(i);
                if ((BodyType)element.FindPropertyRelative("Type").intValue == BodyType.Planet)
                {
                    names.Add(element.FindPropertyRelative("DisplayName").stringValue);
                    indices.Add(i);
                }
            }
            if (names.Count > 0)
            {
                int current = indices.IndexOf(indexProp.intValue);
                if (current < 0)
                    current = 0; // выбрана звезда — показываем первую планету
                int picked = EditorGUILayout.Popup("Стартовая планета", current, names.ToArray());
                indexProp.intValue = indices[picked];
            }
            EditorGUILayout.PropertyField(so.FindProperty("SpawnAngleDeg"));
            EditorGUILayout.PropertyField(so.FindProperty("WorldRadius"),
                new GUIContent("Граница системы"));

            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();
            snapEnabled = GUILayout.Toggle(snapEnabled, "Snap 10");
            showGrid = GUILayout.Toggle(showGrid, "Сетка 100");
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            showTrajectory = GUILayout.Toggle(showTrajectory, "Траектория");
            showGravityField = GUILayout.Toggle(showGravityField, "Гравиполе");
            EditorGUILayout.EndHorizontal();
            if (showTrajectory || showGravityField)
                SceneView.RepaintAll();
        }

        private void DrawValidation()
        {
            foreach (string warning in Validate(level))
                EditorGUILayout.HelpBox(warning, MessageType.Warning);
        }

        private void HandleArrowKeys()
        {
            if (Event.current == null || Event.current.type != EventType.KeyDown || selectedIndex < 0)
                return;
            if (selectedIndex >= level.Bodies.Count)
                return;

            float step = Event.current.shift ? 10f : 1f;
            Vector2 move = Vector2.zero;
            switch (Event.current.keyCode)
            {
                case KeyCode.UpArrow: move = Vector2.up * step; break;
                case KeyCode.DownArrow: move = Vector2.down * step; break;
                case KeyCode.LeftArrow: move = Vector2.left * step; break;
                case KeyCode.RightArrow: move = Vector2.right * step; break;
                default: return;
            }

            Undo.RecordObject(level, "Move Body");
            var body = level.Bodies[selectedIndex];
            Vector2 pos = body.Position + move;
            if (snapEnabled && !Event.current.shift)
                pos = new Vector2(Mathf.Round(pos.x / 10f) * 10f, Mathf.Round(pos.y / 10f) * 10f);
            body.Position = pos;
            EditorUtility.SetDirty(level);
            Event.current.Use();
            SceneView.RepaintAll();
        }

        private void AddBody(SerializedProperty bodies, BodyType type)
        {
            int index = bodies.arraySize;
            bodies.InsertArrayElementAtIndex(index);
            var element = bodies.GetArrayElementAtIndex(index);

            bool star = type == BodyType.Star;
            bool hole = type == BodyType.BlackHole;
            bool belt = type == BodyType.AsteroidBelt;
            int counter = 1;
            for (int i = 0; i < index; i++)
                if ((BodyType)bodies.GetArrayElementAtIndex(i).FindPropertyRelative("Type").intValue == type)
                    counter++;

            element.FindPropertyRelative("DisplayName").stringValue =
                (hole ? "Чёрная дыра " : star ? "Звезда " : belt ? "Пояс " : "Планета ") + counter;
            element.FindPropertyRelative("Type").intValue = (int)type;
            element.FindPropertyRelative("Position").vector2Value = FindFreeSpot(bodies, index);
            element.FindPropertyRelative("MassMultiplier").floatValue = hole ? 8f : star ? 4.5f : 1f;
            float radius = hole ? 30f : star ? 55f : belt ? 200f : 26f;
            element.FindPropertyRelative("Radius").floatValue = radius;
            element.FindPropertyRelative("HeatRadius").floatValue = star ? radius * 3.6f : 0f;
            if (belt)
            {
                element.FindPropertyRelative("BeltWidth").floatValue = 40f;
                element.FindPropertyRelative("BeltAsteroidCount").intValue = 30;
            }

            selectedIndex = index;
            SceneView.RepaintAll();
        }

        // правее существующих, без наложения
        private Vector2 FindFreeSpot(SerializedProperty bodies, int newIndex)
        {
            float maxX = 0f;
            for (int i = 0; i < newIndex; i++)
                maxX = Mathf.Max(maxX, bodies.GetArrayElementAtIndex(i).FindPropertyRelative("Position").vector2Value.x);
            return new Vector2(Mathf.Round((maxX + 400f) / 10f) * 10f, 0f);
        }

        private void MarkNextDuplicateName(SerializedProperty bodies, int index)
        {
            var element = bodies.GetArrayElementAtIndex(index);
            var nameProp = element.FindPropertyRelative("DisplayName");
            nameProp.stringValue = nameProp.stringValue + " копия";
        }

        // работает и в другой уровень
        private void PasteBody(SerializedProperty bodies)
        {
            var data = new CelestialBodyData();
            EditorJsonUtility.FromJsonOverwrite(bodyClipboard, data);
            data.DisplayName += " копия";

            int index = bodies.arraySize;
            bodies.InsertArrayElementAtIndex(index);
            var element = bodies.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("DisplayName").stringValue = data.DisplayName;
            element.FindPropertyRelative("Type").intValue = (int)data.Type;
            element.FindPropertyRelative("Position").vector2Value = FindFreeSpot(bodies, index);
            element.FindPropertyRelative("Radius").floatValue = data.Radius;
            element.FindPropertyRelative("MassMultiplier").floatValue = data.MassMultiplier;
            element.FindPropertyRelative("HeatRadius").floatValue = data.HeatRadius;
            element.FindPropertyRelative("BeltWidth").floatValue = data.BeltWidth;
            element.FindPropertyRelative("BeltAsteroidCount").intValue = data.BeltAsteroidCount;
            selectedIndex = index;
            SceneView.RepaintAll();
        }

        private void FocusSelected()
        {
            var body = level.Bodies[selectedIndex];
            var view = SceneView.lastActiveSceneView;
            if (view != null)
                view.Frame(new Bounds(body.Position, Vector3.one * (body.Radius * 6f + 300f)), false);
        }

        private void FrameAll()
        {
            if (level.Bodies.Count == 0)
                return;
            var bounds = new Bounds(level.Bodies[0].Position, Vector3.zero);
            foreach (var body in level.Bodies)
                bounds.Encapsulate(new Bounds(body.Position, Vector3.one * body.Radius * 2f));
            bounds.Expand(400f);
            var view = SceneView.lastActiveSceneView;
            if (view != null)
                view.Frame(bounds, false);
        }

        // предупреждения, не запрет
        public static List<string> Validate(LevelAsset lvl)
        {
            var warnings = new List<string>();
            var bodies = lvl.Bodies;

            for (int i = 0; i < bodies.Count; i++)
            {
                for (int j = i + 1; j < bodies.Count; j++)
                {
                    float d = Vector2.Distance(bodies[i].Position, bodies[j].Position);
                    if (d < 260f)
                        warnings.Add($"«{bodies[i].DisplayName}»—«{bodies[j].DisplayName}»: центры ближе 260 ({d:0}).");
                    if (d < bodies[i].Radius + bodies[j].Radius)
                        warnings.Add($"«{bodies[i].DisplayName}» и «{bodies[j].DisplayName}» пересекаются поверхностями.");
                }
            }

            Vector2 spawn = SpawnPointOf(lvl);
            foreach (var body in bodies)
            {
                if (body.Type != BodyType.Star)
                    continue;
                float toSpawn = Vector2.Distance(body.Position, spawn);
                if (toSpawn < body.HeatRadius + 550f)
                    warnings.Add($"Жар «{body.DisplayName}» ближе 550 к спавну ({toSpawn - body.HeatRadius:0} от края зоны).");
                foreach (var other in bodies)
                    if (other.Type != BodyType.Star
                        && Vector2.Distance(body.Position, other.Position) < body.HeatRadius + other.Radius)
                        warnings.Add($"Жар «{body.DisplayName}» задевает «{other.DisplayName}».");
            }

            if (lvl.StartPlanetIndex >= 0 && lvl.StartPlanetIndex < bodies.Count)
            {
                var start = bodies[lvl.StartPlanetIndex];
                if (start.Type == BodyType.Star)
                    warnings.Add("Стартовая планета — звезда. Спавн на звезде смертелен.");
                else if (Vector2.Distance(spawn, start.Position) < start.Radius + 7f)
                    warnings.Add("Спавн внутри стартовой планеты (меньше r + 7).");

                if (start.Type == BodyType.Planet && (start.Radius < 15f || start.Radius > 40f))
                    warnings.Add($"Радиус «{start.DisplayName}» вне нормы планет 15–40 ({start.Radius:0}).");
            }

            foreach (var body in bodies)
            {
                if (body.Type == BodyType.Planet && (body.Radius < 15f || body.Radius > 40f))
                    warnings.Add($"Радиус «{body.DisplayName}» вне нормы планет 15–40 ({body.Radius:0}).");
                if (body.Type == BodyType.Star && (body.Radius < 46f || body.Radius > 64f))
                    warnings.Add($"Радиус «{body.DisplayName}» вне нормы звёзд 46–64 ({body.Radius:0}).");
            }

            return warnings;
        }

        // как в игре: r + 7
        public static Vector2 SpawnPointOf(LevelAsset lvl)
        {
            if (lvl.StartPlanetIndex < 0 || lvl.StartPlanetIndex >= lvl.Bodies.Count)
                return Vector2.zero;
            var start = lvl.Bodies[lvl.StartPlanetIndex];
            float rad = lvl.SpawnAngleDeg * Mathf.Deg2Rad;
            return start.Position + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * (start.Radius + 7f);
        }
    }
}
