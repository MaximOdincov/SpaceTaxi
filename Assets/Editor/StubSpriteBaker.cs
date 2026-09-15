using System.IO;
using UnityEditor;
using UnityEngine;
using SpaceTaxi.Core;

namespace SpaceTaxi.Core.Tools
{
    // Печёт PNG-заглушки в Assets/Sprites/ — их кладём в префабы Planet/Ship.
    // Форму и спрайт в префабах задаёт человек; код только масштабирует.
    public static class StubSpriteBaker
    {
        private const string Folder = "Assets/Sprites";

        [MenuItem("Space Taxi/Bake Stub Sprites")]
        public static void BakeAll()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets", "Sprites");

            BakeCircle("planet_stub.png", 256, 0.08f, 2.56f);   // диаметр 100 юнитов
            BakeMask("ship_stub.png", 192, StubSprites.InTriangle, 192f / 14f); // длина 14 юнитов
            BakeMask("line_stub.png", 256, StubSprites.InCapsule, 256f / 16f);  // длина 16 юнитов
            AssetDatabase.SaveAssets();
            Debug.Log("[StubSpriteBaker] готово: " + Folder);
        }

        private static void BakeCircle(string name, int size, float outline, float ppu)
        {
            WritePng(name, StubSprites.CreateCircleTexture(size, outline), ppu);
        }

        private static void BakeMask(string name, int size, System.Func<float, float, bool> inside, float ppu)
        {
            WritePng(name, StubSprites.CreateMaskTexture(size, inside), ppu);
        }

        private static void WritePng(string name, Texture2D tex, float ppu)
        {
            string path = Path.Combine(Folder, name);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.spritePixelsPerUnit = ppu;
            importer.SaveAndReimport();
        }
    }
}
