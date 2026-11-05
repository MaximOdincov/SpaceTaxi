using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using SpaceTaxi.Audio;
using SpaceTaxi.Physics;

namespace SpaceTaxi.Audio.Tools
{
    // Разовая настройка аудио: импорт-настройки, каталог, сцены. Повторный запуск безопасен
    public static class AudioSetupTool
    {
        private const string CatalogPath = "Assets/ScriptableObjects/AudioCatalog.asset";
        private const string InputAssetPath = "Assets/Settings/GameInput.inputactions";
        private const string MainMenuScene = "Assets/Scenes/MainMenu.unity";
        private const string GameScene = "Assets/Scenes/GameScene.unity";

        [MenuItem("Space Taxi/Audio Setup")]
        public static void SetupAll()
        {
            ConfigureImport();
            AudioCatalog catalog = BuildCatalog();
            SetupScenes(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log("[AudioSetup] настройка аудио завершена");
        }

        // только каталог и импорт-настройки, сцены не трогает
        [MenuItem("Space Taxi/Audio Rebuild Catalog")]
        public static void RebuildCatalog()
        {
            ConfigureImport();
            BuildCatalog();
            AssetDatabase.SaveAssets();
            Debug.Log("[AudioSetup] каталог пересобран");
        }

        // короткие SFX — Decompress On Load; музыка — Compress In Memory (Vorbis)
        private static void ConfigureImport()
        {
            string[] files = Directory.GetFiles("Assets/Audio", "*.ogg", SearchOption.AllDirectories);
            foreach (string path in files)
            {
                if (AssetImporter.GetAtPath(path) is not AudioImporter importer)
                    continue;
                AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                bool longClip = path.Contains("/Music/");
                settings.loadType = longClip ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
                if (longClip)
                    settings.compressionFormat = AudioCompressionFormat.Vorbis;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
            }
            Debug.Log($"[AudioSetup] импорт-настройки: {files.Length} файлов");
        }

        public static AudioCatalog BuildCatalog()
        {
            Directory.CreateDirectory("Assets/ScriptableObjects");
            AudioCatalog catalog = AssetDatabase.LoadAssetAtPath<AudioCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<AudioCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            var entries = new List<SoundEntry>
            {
                // двигатели — тихий фон, не истребитель
                Entry(SoundId.EngineMain, AudioGroup.Engine, 0.60f, loop: true,  clips: "sfx_engine_main_loop"),
                Entry(SoundId.EngineRetro, AudioGroup.Engine, 0.45f, loop: true,  clips: "sfx_engine_retro_loop"),
                Entry(SoundId.EngineStrafe, AudioGroup.Engine, 0.35f, loop: true,  clips: "sfx_engine_strafe_loop"),

                // жар — корабельная пожарная тревога
                Entry(SoundId.HeatZone, AudioGroup.Sfx, 0.40f, loop: true, clips: "sfx_heat_loop"),

                // корпус: слабый/средний — металл, сильный — низкочастотный треск
                Entry(SoundId.Collision, AudioGroup.Sfx, 0.75f, jitter: 0.08f, sequential: true,
                      clips: new[] { "sfx_collision_1", "sfx_collision_2", "sfx_collision_3" }),
                Entry(SoundId.LandingTouch, AudioGroup.Sfx, 0.95f, clips: "sfx_landing"),
                Entry(SoundId.Takeoff, AudioGroup.Sfx, 0.70f, clips: "sfx_takeoff"),
                Entry(SoundId.Explosion, AudioGroup.Sfx, 1.00f, clips: "sfx_explosion"),
                Entry(SoundId.Respawn, AudioGroup.Sfx, 0.65f, clips: "sfx_respawn"),

                // состояния и опасности (П9.2): клипы кладёт владелец —
                // слоты в GameInfo/05_AUDIO.md §6, до файлов — тишина
                Entry(SoundId.LowFuel, AudioGroup.Sfx, 0.60f, clips: "sfx_low_fuel"),
                Entry(SoundId.LowHull, AudioGroup.Sfx, 0.55f, clips: "sfx_low_hull"),
                Entry(SoundId.BlackHoleHum, AudioGroup.Sfx, 0.50f, loop: true, clips: "sfx_blackhole_hum"),
                Entry(SoundId.BlackHoleSwallow, AudioGroup.Sfx, 0.90f, clips: "sfx_blackhole_swallow"),
                Entry(SoundId.BeltRumble, AudioGroup.Sfx, 0.45f, loop: true, clips: "sfx_belt_rumble"),

                // заказы и экономика (П9.3): клипы кладёт владелец — слоты в
                // GameInfo/05_AUDIO.md §6, до файлов — тишина; «новый заказ»
                // — ComputerPing (реюз, отдельный OrderPing не заводился)
                Entry(SoundId.BoardBip,      AudioGroup.Sfx, 0.55f, clips: "sfx_board_bip"),
                Entry(SoundId.DeliveryChime, AudioGroup.Sfx, 0.70f, clips: "sfx_delivery_chime"),
                Entry(SoundId.TaskSuccess,   AudioGroup.Sfx, 0.60f, clips: "sfx_task_success"),
                Entry(SoundId.TaskFail,      AudioGroup.Sfx, 0.55f, clips: "sfx_task_fail"),
                Entry(SoundId.PurchaseCash,  AudioGroup.Sfx, 0.65f, clips: "sfx_purchase"),
                Entry(SoundId.DebtTone,      AudioGroup.Sfx, 0.60f, clips: "sfx_debt"),
                Entry(SoundId.DismissalTone, AudioGroup.Sfx, 0.70f, clips: "sfx_dismissal"),

                // интерфейс
                Entry(SoundId.UiClick, AudioGroup.Ui,     0.50f, jitter: 0f, clips: "ui_click"),
                Entry(SoundId.ComputerPing, AudioGroup.Ui,     0.55f, jitter: 0f, clips: "ui_computer_ping"),

                // фон: очень тихий низкий слой под музыкой
                Entry(SoundId.AmbientSpace, AudioGroup.Music,  0.16f, pitch: 0.45f, loop: true,  clips: "ambient_space_loop"),
                Entry(SoundId.MusicMenu, AudioGroup.Music,  0.50f, loop: true,  clips: "music_menu"),
                Entry(SoundId.MusicFlight, AudioGroup.Music, 0.55f, loop: true, clips: "Candidates/theme_01_magic_space"),
            };

            SerializedObject so = new SerializedObject(catalog);
            so.Update();
            var list = so.FindProperty("entries");
            list.arraySize = entries.Count;
            for (int i = 0; i < entries.Count; i++)
                list.GetArrayElementAtIndex(i).boxedValue = entries[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        // менеджер и музыку в обе сцены, звуки на корабль в игровой
        private static void SetupScenes(AudioCatalog catalog)
        {
            InputActionAsset input = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputAssetPath);
            if (input == null)
                Debug.LogWarning("[AudioSetup] GameInput.inputactions не найден, ShipAudio без ввода");

            SetupScene(MainMenuScene, catalog, SoundId.MusicMenu, ambient: false, input);
            SetupScene(GameScene, catalog, SoundId.MusicFlight, ambient: true, input);
        }

        private static void SetupScene(string scenePath, AudioCatalog catalog, SoundId music, bool ambient, InputActionAsset input)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            EnsureManager(catalog);
            EnsureSceneMusic(music, ambient);

            if (ambient) // игровая сцена — вешаем звуки на корабль
                EnsureShipAudio(input);

            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[AudioSetup] сцена {Path.GetFileNameWithoutExtension(scenePath)} готова");
        }

        private static void EnsureManager(AudioCatalog catalog)
        {
            AudioManager existing = Object.FindAnyObjectByType<AudioManager>();
            if (existing != null)
                return;
            var go = new GameObject("AudioManager");
            AudioManager manager = go.AddComponent<AudioManager>();
            var so = new SerializedObject(manager);
            so.FindProperty("catalog").objectReferenceValue = catalog;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureSceneMusic(SoundId music, bool ambient)
        {
            SceneMusic existing = Object.FindAnyObjectByType<SceneMusic>();
            if (existing == null)
                existing = new GameObject("SceneMusic").AddComponent<SceneMusic>();
            var so = new SerializedObject(existing);
            so.FindProperty("music").enumValueIndex = (int)music;
            so.FindProperty("ambient").boolValue = ambient;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureShipAudio(InputActionAsset input)
        {
            Ship ship = Object.FindAnyObjectByType<Ship>(FindObjectsInactive.Include);
            if (ship == null)
            {
                Debug.LogWarning("[AudioSetup] корабль не найден в сцене — ShipAudio/HeatAudio добавит программист при спавне");
                return;
            }
            ShipAudio shipAudio = ship.GetComponent<ShipAudio>();
            if (shipAudio == null)
                shipAudio = ship.gameObject.AddComponent<ShipAudio>();
            if (ship.GetComponent<HeatAudio>() == null)
                ship.gameObject.AddComponent<HeatAudio>();
            // поле input у ShipAudio удалено (ввод берётся из ShipController) —
            // сериализовать нечего, SerializedObject падал бы NRE
        }

        // --- Хелперы сборки каталога ---
        private static SoundEntry Entry(SoundId id, AudioGroup group, float volume, bool loop = false,
            float pitch = 1f, float jitter = 0.04f, bool sequential = false, params string[] clips)
        {
            var entry = new SoundEntry
            {
                Id = id,
                Group = group,
                Volume = volume,
                Pitch = pitch,
                PitchJitter = jitter,
                Loop = loop,
                Sequential = sequential,
            };
            var list = new List<AudioClip>(clips.Length);
            foreach (string name in clips)
            {
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(FindClipPath(name));
                if (clip == null)
                    Debug.LogWarning($"[AudioSetup] клип не найден: {name}");
                else
                    list.Add(clip);
            }
            entry.Clips = list.ToArray();
            return entry;
        }

        // поиск клипа по имени файла; подпапки — через "Candidates/имя"
        private static string FindClipPath(string file)
        {
            string[] roots = { "Assets/Audio/SFX/", "Assets/Audio/UI/", "Assets/Audio/Music/" };
            string[] exts = { ".ogg", ".mp3", ".wav" };
            foreach (string root in roots)
                foreach (string ext in exts)
                    if (File.Exists(root + file + ext))
                        return root + file + ext;
            return "Assets/Audio/SFX/" + file + ".ogg";
        }
    }
}
