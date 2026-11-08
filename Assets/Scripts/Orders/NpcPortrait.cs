using UnityEngine;

namespace SpaceTaxi.Orders
{
    // Портрет пассажира: с шансом 50% — готовый NPC из Resources/Art/npc/ready
    // (диспетчер исключён — отдельный персонаж), иначе сборка из слоёв 500×500:
    // тело (случайный цвет) + одежда (случайный цвет) + глаза + рот (без цвета).
    // Генерация детерминирована сидом — сейв восстанавливает портрет по
    // PortraitSeed (SaveSystem), цвета запечены в текстуру.
    public static class NpcPortrait
    {
        private const int Frame = 512;
        private const float ReadyChance = 0.5f;
        private const string DispatcherName = "dispetcher";

        private static Sprite[] ready, bodies, clothes, eyes, mouths;

        private static readonly Color[] BodyTints =
        {
            ColorRGB(0xE8, 0xB2, 0x7D), // песочный
            ColorRGB(0xD9, 0x8C, 0x6B), // терракота
            ColorRGB(0xC9, 0x6B, 0x7E), // пыльная роза
            ColorRGB(0x7D, 0xA9, 0xD9), // небесно-голубой
            ColorRGB(0x7D, 0xC9, 0xB4), // мятный
            ColorRGB(0xA0, 0x8C, 0xD9), // лавандовый
            ColorRGB(0x8F, 0xA3, 0xB8), // серо-голубой
            ColorRGB(0xB5, 0xC9, 0x6B), // фисташковый
        };

        private static readonly Color[] ClothesTints =
        {
            ColorRGB(0xC0, 0x49, 0x3D), // красный
            ColorRGB(0x2E, 0x5E, 0xAA), // синий
            ColorRGB(0x3D, 0x7A, 0x45), // зелёный
            ColorRGB(0x8A, 0x4E, 0xA8), // фиолетовый
            ColorRGB(0xD8, 0x8C, 0x2A), // оранжевый
            ColorRGB(0x2A, 0x8C, 0x8C), // бирюзовый
            ColorRGB(0xB0, 0x3A, 0x6E), // малиновый
            ColorRGB(0x5A, 0x5A, 0x66), // графит
        };

        public static Sprite Generate(int seed)
        {
            var rnd = new System.Random(seed);
            Sprite[] readyPool = Pool(ref ready, "Art/npc/ready");
            if (readyPool != null && rnd.NextDouble() < ReadyChance)
            {
                Sprite picked = readyPool[rnd.Next(readyPool.Length)];
                if (picked != null && picked.name != DispatcherName)
                    return picked;
                // выпал диспетчер — соберём случайного вместо него
            }
            return Compose(rnd);
        }

        private static Sprite Compose(System.Random rnd)
        {
            Sprite body = Pick(ref bodies, "Art/npc/body", rnd);
            if (body == null)
                return null; // слоёв нет — UI покажет цветную заглушку
            Sprite clothesSprite = Pick(ref clothes, "Art/npc/clothes", rnd);
            Sprite eyesSprite = Pick(ref eyes, "Art/npc/eyes", rnd);
            Sprite mouthSprite = Pick(ref mouths, "Art/npc/mouth", rnd);

            var tex = new Texture2D(Frame, Frame, TextureFormat.RGBA32, false);
            var dst = new Color32[Frame * Frame];
            Blit(body, BodyTints[rnd.Next(BodyTints.Length)], dst);
            Blit(clothesSprite, ClothesTints[rnd.Next(ClothesTints.Length)], dst);
            Blit(eyesSprite, Color.white, dst); // глаза и рот не красим
            Blit(mouthSprite, Color.white, dst);

            tex.SetPixels32(dst);
            tex.Apply(false, false);
            return Sprite.Create(tex, new Rect(0f, 0f, Frame, Frame), new Vector2(0.5f, 0.5f), Frame);
        }

        // слой поверх dst: rgb с тином, обычная альфа-композиция «over»
        private static void Blit(Sprite layer, Color tint, Color32[] dst)
        {
            if (layer == null)
                return;
            Texture2D tex = layer.texture;
            if (!tex.isReadable)
                return;
            Color32[] src = tex.GetPixels32();
            Rect rect = layer.textureRect;
            int rx = Mathf.RoundToInt(rect.x), ry = Mathf.RoundToInt(rect.y);
            int rw = Mathf.RoundToInt(rect.width), rh = Mathf.RoundToInt(rect.height);
            if (rw <= 0 || rh <= 0)
                return;

            for (int y = 0; y < Frame; y++)
            {
                int sy = Mathf.Min(ry + y * rh / Frame, tex.height - 1);
                for (int x = 0; x < Frame; x++)
                {
                    Color32 c = src[sy * tex.width + Mathf.Min(rx + x * rw / Frame, tex.width - 1)];
                    if (c.a == 0)
                        continue;
                    float sa = c.a / 255f;
                    int i = y * Frame + x;
                    float da = dst[i].a / 255f;
                    float oa = sa + da * (1f - sa);
                    if (oa <= 0f)
                        continue;
                    byte r = (byte)((c.r * tint.r * sa + dst[i].r * da * (1f - sa)) / oa);
                    byte g = (byte)((c.g * tint.g * sa + dst[i].g * da * (1f - sa)) / oa);
                    byte b = (byte)((c.b * tint.b * sa + dst[i].b * da * (1f - sa)) / oa);
                    dst[i] = new Color32(r, g, b, (byte)(oa * 255f));
                }
            }
        }

        private static Sprite Pick(ref Sprite[] field, string path, System.Random rnd)
        {
            Sprite[] pool = Pool(ref field, path);
            return pool != null ? pool[rnd.Next(pool.Length)] : null;
        }

        private static Sprite[] Pool(ref Sprite[] field, string path)
        {
            if (field == null)
            {
                field = Resources.LoadAll<Sprite>(path);
                if (field == null || field.Length == 0)
                {
                    Debug.LogWarning("[NPC] Пусто: Assets/Resources/" + path);
                    field = new Sprite[0];
                }
            }
            return field.Length > 0 ? field : null;
        }

        private static Color ColorRGB(int r, int g, int b)
        {
            return new Color32((byte)r, (byte)g, (byte)b, 255);
        }
    }
}
