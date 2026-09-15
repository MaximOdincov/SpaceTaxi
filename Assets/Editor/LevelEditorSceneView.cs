using System.Linq;
using UnityEditor;
using UnityEngine;
using SpaceTaxi.World;

namespace SpaceTaxi.Tools
{
    // Гизмо уровня в Scene View (ТЗ: 09_LEVEL_DESIGN.md §1.3, §1.4): кольца,
    // жар, спавн, дистанции, рамка камеры, сетка; drag тел со snap и Undo.
    [InitializeOnLoad]
    public static class LevelEditorSceneView
    {
        // Вид игрока: высота из CameraFollow, 16:9.
        private const float ViewHeight = 320f;
        private const float ViewWidth = ViewHeight * 16f / 9f;

        private static readonly Color PlanetFill = new Color(0.6f, 0.8f, 1f, 0.08f);
        private static readonly Color StarFill = new Color(1f, 0.8f, 0.3f, 0.1f);
        private static readonly Color LandingRing = new Color(0.3f, 1f, 0.4f, 0.8f);
        private static readonly Color CaptureRing = new Color(0.4f, 0.8f, 1f, 0.7f);
        private static readonly Color HeatColor = new Color(1f, 0.5f, 0.2f, 0.8f);
        private static readonly Color SpawnColor = new Color(1f, 0.9f, 0.3f, 0.9f);
        private static readonly Color CameraFrame = new Color(0.6f, 0.9f, 1f, 0.5f);
        private static readonly Color GridColor = new Color(1f, 1f, 1f, 0.06f);
        private static readonly Color DangerColor = new Color(1f, 0.4f, 0.3f, 0.9f);
        private static readonly Color DistanceColor = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color TrajectoryColor = new Color(0.8f, 0.5f, 1f, 0.8f);
        private static readonly Color GravityWeak = new Color(0.4f, 0.7f, 1f, 0.5f);
        private static readonly Color GravityStrong = new Color(1f, 0.4f, 0.2f, 0.8f);

        private static bool dragging;
        private static Vector2 dragOffset;
        private static Vector2 dragPosition;

        static LevelEditorSceneView()
        {
            SceneView.duringSceneGui += OnSceneGui;
        }

        private static void OnSceneGui(SceneView view)
        {
            LevelEditorWindow window = LevelEditorWindow.OpenWindow;
            if (window == null || window.Level == null)
                return;
            LevelAsset level = window.Level;

            if (window.ShowGrid)
                DrawGrid(level);

            if (window.ShowGravityField)
                DrawGravityField(level);

            for (int i = 0; i < level.Bodies.Count; i++)
                DrawBody(level.Bodies[i], i == window.SelectedIndex);

            DrawSelectionDistances(level, window.SelectedIndex);
            DrawSpawn(level);
            DrawCameraFrame(level);
            if (window.ShowTrajectory)
                DrawSpawnTrajectory(level);

            DrawGravityProbe(level);
            HandleDrag(window, level);
        }

        // Зонд: с зажатым Ctrl в точке курсора — стрелка суммарного притяжения
        // и его величина. Показывает, куда «повезёт» корабль в этой точке.
        private static void DrawGravityProbe(LevelAsset level)
        {
            Event ev = Event.current;
            if (ev == null || !ev.control)
                return;

            var config = FindConfig();
            float k = config != null ? config.GravityParamK : 26000f;
            float clamp = config != null ? config.GravityAccelClamp : 240f;

            Vector2 world = MouseWorld(ev.mousePosition);
            Vector2 accel = GravityAt(level, world, k, clamp);
            float magnitude = accel.magnitude;
            if (magnitude < 0.5f)
                return;

            Vector2 dir = accel / magnitude;
            float length = Mathf.Min(60f, 10f + magnitude * 0.2f);
            Vector3 from = world;
            Vector3 to = world + dir * length;

            float t = Mathf.Clamp01(magnitude / clamp);
            Handles.color = Color.Lerp(GravityWeak, GravityStrong, t);
            Handles.DrawLine(from, to);
            Vector3 side = new Vector3(-dir.y, dir.x) * length * 0.25f;
            Handles.DrawLine(to, to - (Vector3)dir * length * 0.35f - side);
            Handles.DrawLine(to, to - (Vector3)dir * length * 0.35f + side);
            Handles.Label(to + (Vector3)dir * 10f,
                magnitude.ToString("0") + " ед/с²",
                new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = Handles.color } });
            SceneView.RepaintAll();
        }

        private static void DrawBody(CelestialBodyData body, bool selected)
        {
            Vector3 center = body.Position;
            bool star = body.Type == BodyType.Star;

            // Превью внешнего вида: спрайт и цвет из данных уровня.
            var visuals = body.Visuals;
            if (visuals != null && visuals.Sprite != null && Event.current.type == EventType.Repaint)
            {
                Vector2 guiCenter = HandleUtility.WorldToGUIPoint(center);
                Vector2 guiEdge = HandleUtility.WorldToGUIPoint(center + Vector3.right * body.Radius);
                float guiRadius = Vector2.Distance(guiCenter, guiEdge);
                Color tint = visuals.UseColor ? visuals.Color : Color.white;
                Rect rect = new Rect(guiCenter.x - guiRadius, guiCenter.y - guiRadius, guiRadius * 2f, guiRadius * 2f);

                Texture tex = visuals.Sprite.texture;
                Rect tr = visuals.Sprite.textureRect;
                Rect uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
                Color prevColor = GUI.color;
                GUI.color = tint;
                GUI.DrawTextureWithTexCoords(rect, tex, uv, true);
                GUI.color = prevColor;
            }

            // Заливка + контур + имя.
            Handles.color = star ? StarFill : PlanetFill;
            if (visuals == null || visuals.Sprite == null)
                Handles.DrawSolidDisc(center, Vector3.forward, body.Radius);
            Handles.color = selected ? Color.white : (star ? new Color(1f, 0.8f, 0.3f) : new Color(0.6f, 0.8f, 1f));
            Handles.DrawWireDisc(center, Vector3.forward, body.Radius);
            Handles.Label(center + Vector3.up * (body.Radius + 12f), body.DisplayName);

            // Кольца: override из данных или формула (константы из PhysicsConfig).
            var config = FindConfig();
            float landingOffset = config != null ? config.LandingRingOffset : 90f;
            float captureK = config != null ? config.CaptureRingK : 3.3f;
            float captureBase = config != null ? config.CaptureRingBase : 30f;

            // Посадочное кольцо — зелёный пунктир (только планеты).
            if (!star)
            {
                float landing = body.LandingRingRadius > 0f ? body.LandingRingRadius : body.Radius + landingOffset;
                DottedCircle(center, landing, LandingRing, 4f);
            }

            // Кольцо качели — голубой точечный, мельче.
            float capture = body.CaptureRingRadius > 0f ? body.CaptureRingRadius : body.Radius * captureK + captureBase;
            DottedCircle(center, capture, CaptureRing, 2f);

            // Жаровая зона — оранжевый пунктир + слабая заливка.
            if (star && body.HeatRadius > 0f)
            {
                Handles.color = new Color(HeatColor.r, HeatColor.g, HeatColor.b, 0.05f);
                Handles.DrawSolidDisc(center, Vector3.forward, body.HeatRadius);
                DottedCircle(center, body.HeatRadius, HeatColor, 4f);
            }
        }

        // Линии от выбранного тела до остальных с дистанциями; < 300 — тревожно.
        private static void DrawSelectionDistances(LevelAsset level, int selectedIndex)
        {
            if (selectedIndex < 0 || selectedIndex >= level.Bodies.Count)
                return;
            var from = level.Bodies[selectedIndex];

            foreach (var other in level.Bodies)
            {
                if (other == from)
                    continue;
                float d = Vector2.Distance(from.Position, other.Position);
                bool close = d < 300f;
                Handles.color = close ? DangerColor : DistanceColor;
                Handles.DrawLine(from.Position, other.Position);
                Handles.Label(
                    Vector3.Lerp(from.Position, other.Position, 0.5f),
                    d.ToString("0"), new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = close ? DangerColor : Color.gray } });
            }
        }

        // Спавн: маркер-треугольник + стрелка курса от поверхности.
        private static void DrawSpawn(LevelAsset level)
        {
            if (level.StartPlanetIndex < 0 || level.StartPlanetIndex >= level.Bodies.Count)
                return;
            var start = level.Bodies[level.StartPlanetIndex];
            float rad = level.SpawnAngleDeg * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            Vector2 pos = start.Position + dir * (start.Radius + 7f);

            Handles.color = SpawnColor;
            Vector3 p = pos;
            Vector3 side = new Vector3(-dir.y, dir.x) * 8f;
            Handles.DrawPolyLine(p + (Vector3)dir * 14f, p - side, p + side, p + (Vector3)dir * 14f);

            Vector3 a = p + (Vector3)dir * 60f;
            Handles.DrawLine(p, a);
            Handles.DrawLine(a, a - (Vector3)dir * 12f - side * 0.7f);
            Handles.DrawLine(a, a - (Vector3)dir * 12f + side * 0.7f);
            Handles.Label(a + (Vector3)dir * 10f, "спавн " + level.SpawnAngleDeg.ToString("0") + "°");
        }

        // Рамка «что видит игрок» вокруг спавна.
        private static void DrawCameraFrame(LevelAsset level)
        {
            Vector3 c = LevelEditorWindow.SpawnPointOf(level);
            float hw = ViewWidth * 0.5f;
            float hh = ViewHeight * 0.5f;
            Handles.color = CameraFrame;
            Handles.DrawPolyLine(
                new Vector3(c.x - hw, c.y - hh), new Vector3(c.x + hw, c.y - hh),
                new Vector3(c.x + hw, c.y + hh), new Vector3(c.x - hw, c.y + hh),
                new Vector3(c.x - hw, c.y - hh));
        }

        private static void DrawGrid(LevelAsset level)
        {
            if (level.Bodies.Count == 0)
                return;
            float minX = level.Bodies.Min(b => b.Position.x) - 400f;
            float maxX = level.Bodies.Max(b => b.Position.x) + 400f;
            float minY = level.Bodies.Min(b => b.Position.y) - 400f;
            float maxY = level.Bodies.Max(b => b.Position.y) + 400f;

            Handles.color = GridColor;
            for (float x = Mathf.Floor(minX / 100f) * 100f; x <= maxX; x += 100f)
                Handles.DrawLine(new Vector3(x, minY), new Vector3(x, maxY));
            for (float y = Mathf.Floor(minY / 100f) * 100f; y <= maxY; y += 100f)
                Handles.DrawLine(new Vector3(minX, y), new Vector3(maxX, y));
        }

        // Клик — выбрать тело, drag — переместить (snap 10, Shift — свободно).
        private static void HandleDrag(LevelEditorWindow window, LevelAsset level)
        {
            Event ev = Event.current;
            if (ev == null)
                return;

            if (ev.type == EventType.MouseDown && ev.button == 0 && !ev.alt)
            {
                Vector2 world = MouseWorld(ev.mousePosition);
                int hit = FindBodyAt(level, world);
                if (hit >= 0)
                {
                    window.SetSelected(hit);
                    dragging = true;
                    dragOffset = level.Bodies[hit].Position - world;
                    Undo.RecordObject(level, "Move Body");
                    ev.Use();
                }
            }
            else if (ev.type == EventType.MouseDrag && dragging)
            {
                int index = window.SelectedIndex;
                if (index >= 0 && index < level.Bodies.Count)
                {
                    Vector2 pos = MouseWorld(ev.mousePosition) + dragOffset;
                    if (window.SnapEnabled && !ev.shift)
                        pos = new Vector2(Mathf.Round(pos.x / 10f) * 10f, Mathf.Round(pos.y / 10f) * 10f);
                    level.Bodies[index].Position = pos;
                    dragPosition = pos;
                    EditorUtility.SetDirty(level);
                    ev.Use();
                }
            }
            else if (ev.type == EventType.MouseUp && dragging)
            {
                dragging = false;
                ev.Use();
            }
            else if (ev.type == EventType.Repaint && dragging)
            {
                // Координаты при перетаскивании — рядом с телом.
                Handles.color = Color.white;
                Handles.Label(dragPosition + Vector2.up * 30f,
                    "(" + dragPosition.x.ToString("0") + ", " + dragPosition.y.ToString("0") + ")");
            }
        }

        private static int FindBodyAt(LevelAsset level, Vector2 world)
        {
            for (int i = 0; i < level.Bodies.Count; i++)
                if (Vector2.Distance(level.Bodies[i].Position, world) < level.Bodies[i].Radius)
                    return i;
            return -1;
        }

        private static Vector2 MouseWorld(Vector2 mouse)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(mouse);
            return new Vector2(ray.origin.x, ray.origin.y);
        }

        private static void DottedCircle(Vector3 center, float radius, Color color, float dotSize)
        {
            Handles.color = color;
            const int segments = 64;
            Vector3 prev = center + new Vector3(radius, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                Vector3 next = center + new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius);
                Handles.DrawDottedLine(prev, next, dotSize);
                prev = next;
            }
        }

        // Ускорение суммарной гравитации в точке (формула игры, без MB).
        private static Vector2 GravityAt(LevelAsset level, Vector2 pos, float k, float clamp)
        {
            Vector2 accel = Vector2.zero;
            foreach (var body in level.Bodies)
            {
                Vector2 delta = body.Position - pos;
                float d2 = delta.sqrMagnitude;
                if (d2 < 0.0001f)
                    continue;
                float d = Mathf.Sqrt(d2);
                float a = Mathf.Min(k * body.Radius * body.MassMultiplier / d2, clamp);
                accel += delta / d * a;
            }
            return accel;
        }

        // Первый PhysicsConfig проекта — константы для превью.
        private static SpaceTaxi.Physics.PhysicsConfig FindConfig()
        {
            string[] guids = AssetDatabase.FindAssets("t:PhysicsConfig");
            return guids.Length > 0 ? AssetDatabase.LoadAssetAtPath<SpaceTaxi.Physics.PhysicsConfig>(AssetDatabase.GUIDToAssetPath(guids[0])) : null;
        }

        // Прогноз свободного полёта из спавна после взлёта (импульс TakeoffV).
        private static void DrawSpawnTrajectory(LevelAsset level)
        {
            var config = FindConfig();
            float takeoffV = config != null ? config.TakeoffV : 85f;
            float k = config != null ? config.GravityParamK : 26000f;
            float clamp = config != null ? config.GravityAccelClamp : 240f;

            if (level.StartPlanetIndex < 0 || level.StartPlanetIndex >= level.Bodies.Count)
                return;
            var start = level.Bodies[level.StartPlanetIndex];
            float rad = level.SpawnAngleDeg * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            Vector2 pos = start.Position + dir * (start.Radius + 7f);
            Vector2 vel = dir * takeoffV;

            const float h = 1f / 60f;
            const int steps = 6 * 60; // 6 секунд
            Handles.color = TrajectoryColor;
            Vector3 prev = pos;
            for (int i = 0; i < steps; i++)
            {
                vel += GravityAt(level, pos, k, clamp) * h;
                pos += vel * h;

                foreach (var body in level.Bodies)
                    if (Vector2.Distance(body.Position, pos) < body.Radius + 7f)
                        return; // врезались — прогноз закончен

                if (i % 10 == 0) // точка каждые ~10 шагов
                {
                    Handles.DrawDottedLine(prev, pos, 3f);
                    prev = pos;
                }
            }
        }

        // Стрелки гравиполя: направление и сила (цвет от синего к красному).
        private static void DrawGravityField(LevelAsset level)
        {
            var config = FindConfig();
            float k = config != null ? config.GravityParamK : 26000f;
            float clamp = config != null ? config.GravityAccelClamp : 240f;

            if (level.Bodies.Count == 0)
                return;
            float minX = level.Bodies.Min(b => b.Position.x) - 600f;
            float maxX = level.Bodies.Max(b => b.Position.x) + 600f;
            float minY = level.Bodies.Min(b => b.Position.y) - 600f;
            float maxY = level.Bodies.Max(b => b.Position.y) + 600f;
            const float step = 150f;

            for (float x = minX; x <= maxX; x += step)
            {
                for (float y = minY; y <= maxY; y += step)
                {
                    Vector2 pos = new Vector2(x, y);
                    Vector2 accel = GravityAt(level, pos, k, clamp);
                    float magnitude = accel.magnitude;
                    if (magnitude < 2f)
                        continue; // пустота — не рисуем

                    Vector2 dir = accel / magnitude;
                    float length = Mathf.Min(40f, 6f + magnitude * 0.12f);
                    Vector3 from = pos;
                    Vector3 to = pos + dir * length;

                    float t = Mathf.Clamp01(magnitude / clamp);
                    Handles.color = Color.Lerp(GravityWeak, GravityStrong, t);
                    Handles.DrawLine(from, to);
                    Vector3 side = new Vector3(-dir.y, dir.x) * length * 0.25f;
                    Handles.DrawLine(to, to - (Vector3)dir * length * 0.35f - side);
                    Handles.DrawLine(to, to - (Vector3)dir * length * 0.35f + side);
                }
            }
        }
    }
}
