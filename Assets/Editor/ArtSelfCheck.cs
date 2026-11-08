using System.Linq;
using UnityEditor;
using UnityEngine;
using SpaceTaxi.Economy;
using SpaceTaxi.Orders;

namespace SpaceTaxi.EditorTools
{
    // Самопроверка пользовательского арта: пулы Resources, спрайты моделей,
    // префабы, сборка NPC-портрета. Меню «Space Taxi/Art Self-Check» и CLI
    // (-executeMethod SpaceTaxi.EditorTools.ArtSelfCheck.RunFromCLI).
    public static class ArtSelfCheck
    {
        private static int failures;

        public static void RunFromCLI()
        {
            Run();
            Debug.Log($"[ArtSelfCheck] ИТОГ: {(failures == 0 ? "OK" : failures + " провалов")}");
        }

        [MenuItem("Space Taxi/Art Self-Check")]
        public static void Run()
        {
            failures = 0;
            CheckPool("Art/planets", 9);
            CheckPool("Art/asteroids", 9);
            foreach (string tier in new[] { "gas_poor", "gas_norm", "gas_rich", "gas_high" })
                Check(Resources.Load<Sprite>("Art/stations/" + tier) != null, "stations/" + tier);
            CheckPool("Art/npc/ready", 12);
            CheckPool("Art/npc/body", 4);
            CheckPool("Art/npc/clothes", 3);
            CheckPool("Art/npc/eyes", 2);
            CheckPool("Art/npc/mouth", 2);

            foreach (string id in new[] { "barin", "delta", "igla", "kaban", "meridian", "skrepka", "strela", "egg_rental", "egg_own" })
            {
                ShipConfig config = Resources.LoadAll<ShipConfig>("Ships").FirstOrDefault(c => c.Id == id);
                Check(config != null, "ShipConfig " + id);
                if (config == null)
                    continue;
                Check(config.ShipSprite != null, id + ".ShipSprite");
                Check(config.PaintVariantCount > 0, id + ".PaintVariants=" + config.PaintVariantCount);
                GameObject prefab = Resources.Load<GameObject>("Ships/" + id);
                Check(prefab != null, "prefab " + id);
                var renderer = prefab != null ? prefab.GetComponentInChildren<SpriteRenderer>() : null;
                Check(renderer != null && renderer.sprite != null, id + ".prefab sprite");
                Check(prefab != null && prefab.transform.Find("ExhaustMain") != null
                    && prefab.transform.Find("ExhaustRetro") != null, id + ".prefab slots");
            }

            Sprite portrait = NpcPortrait.Generate(12345);
            Check(portrait != null, "NpcPortrait seed 12345");
            for (int seed = 1; seed <= 60; seed++)
                Check(NpcPortrait.Generate(seed) != null, "NpcPortrait seed " + seed);

            Texture2D icons = Resources.Load<Texture2D>("Map/map_icons");
            Texture2D stamps = Resources.Load<Texture2D>("Map/map_stamps");
            Check(icons != null && icons.width == 768, "map_icons 768px");
            Check(stamps != null && stamps.width == 512, "map_stamps 512px");

            Debug.Log($"[ArtSelfCheck] провалов: {failures}");
        }

        private static void CheckPool(string path, int expected)
        {
            Sprite[] sprites = Resources.LoadAll<Sprite>(path);
            Check(sprites.Length == expected, path + " = " + sprites.Length + "/" + expected);
        }

        private static void Check(bool ok, string what)
        {
            if (!ok)
            {
                failures++;
                Debug.LogError("[ArtSelfCheck] ПРОВАЛ: " + what);
            }
            else
            {
                Debug.Log("[ArtSelfCheck] ок: " + what);
            }
        }
    }
}
