using System.Collections.Generic;
using UnityEngine;
using SpaceTaxi.Core;
using SpaceTaxi.Physics;

namespace SpaceTaxi.World
{
    // Параллакс: три слоя, дрейфуют с долей скорости камеры. Палитра слоя —
    // набор спрайтов (звёзды, скопления, пыль, яркие sparkle-звёзды, декор),
    // выбор предмета и его масштаб/альфа — по сиду слоя (раскладка стабильна).
    [DefaultExecutionOrder(50)] // LateUpdate после CameraFollow — камера уже встала
    public class StarfieldParallax : MonoBehaviour
    {
        // вид предмета слоя: спрайт + характер отрисовки
        private sealed class Kind
        {
            public Sprite Sprite;
            public bool Glow;             // fake-glow: чайлд тем же спрайтом крупнее
            public float Units;           // целевой диаметр в юнитах (до вариации)
            public float Alpha;           // альфа до вариации
            public float GlowScale = 2f;  // масштаб гало относительно предмета
            public float GlowAlpha = 0.4f;
            public Color? TintOverride;   // null — случайный тинт из Tints
            public int Weight = 1;
        }

        private class Layer
        {
            public Transform Root;
            public Vector2[] Seeds;
            public Transform[] Stars;
            public float Factor;
            public Kind[] Kinds;
            public System.Random Rng;
        }

        // декор-коллаж художника: Assets/Resources/Background/decor.png (2×3);
        // файла может не быть — работаем на процедурных болванках, арта ждём молча
        private const string DecorPath = "Background/decor";
        private const int DecorCols = 2;
        private const int DecorRows = 3;
        private const float DecorCellUnits = 10f; // ширина ячейки в юнитах до масштаба слоя

        private readonly List<Layer> layers = new List<Layer>();
        private float tileX = 1400f;
        private float tileY = 950f;

        private static readonly Color[] Tints =
        {
            new Color(1f, 1f, 1f),
            new Color(0.78f, 0.86f, 1f),
            new Color(1f, 0.93f, 0.82f),
        };

        private static readonly Color SunTint = new Color(1f, 0.82f, 0.55f);

        private void Awake()
        {
            var cam = GetComponent<Camera>();
            float aspect = cam != null ? cam.aspect : 1.78f;

            // тайл с запасом под самый широкий зум
            tileX = Mathf.Max(1300f, 2f * 250f * aspect + 400f);
            tileY = Mathf.Max(950f, 2f * 250f + 450f);

            Sprite[] decor = LoadDecor();
            Build(0.12f, 90, FarPalette(decor));  // дальние звёзды + скопления + далёкие солнца
            Build(0.35f, 60, MidPalette(decor));  // средний слой
            Build(0.65f, 36, NearPalette(decor)); // ближние звёзды
        }

        private void Build(float factor, int count, Kind[] kinds)
        {
            var rootGo = new GameObject($"Stars_{factor:0.00}");
            var layer = new Layer
            {
                Root = rootGo.transform,
                Factor = factor,
                Seeds = new Vector2[count],
                Stars = new Transform[count],
                Kinds = kinds,
                Rng = new System.Random(0x51A70 + layers.Count * 7919), // детерминированная раскладка
            };

            float total = 0f;
            foreach (Kind kind in kinds)
                total += kind.Weight;

            for (int i = 0; i < count; i++)
            {
                Kind kind = Pick(layer.Rng, kinds, total);
                var starGo = new GameObject("Star");
                starGo.transform.SetParent(layer.Root, false);
                var r = starGo.AddComponent<SpriteRenderer>();
                r.sprite = kind.Sprite;
                r.sortingOrder = -10;
                Color c = kind.TintOverride ?? Tints[layer.Rng.Next(Tints.Length)];
                c.a = Mathf.Clamp01(kind.Alpha * (0.6f + 0.4f * (float)layer.Rng.NextDouble()));
                r.color = c;
                float s = kind.Units / NaturalUnits(kind.Sprite)
                          * (0.7f + 0.6f * (float)layer.Rng.NextDouble());
                starGo.transform.localScale = new Vector3(s, s, 1f);

                if (kind.Glow)
                {
                    var glowGo = new GameObject("Glow");
                    glowGo.transform.SetParent(starGo.transform, false);
                    var gr = glowGo.AddComponent<SpriteRenderer>();
                    gr.sprite = kind.Sprite;
                    gr.sortingOrder = -11; // гало под самим предметом
                    Color g = kind.TintOverride ?? Color.white;
                    g.a = Mathf.Clamp01(kind.GlowAlpha * (0.7f + 0.3f * (float)layer.Rng.NextDouble()));
                    gr.color = g;
                    float gs = kind.GlowScale * (0.85f + 0.3f * (float)layer.Rng.NextDouble());
                    glowGo.transform.localScale = new Vector3(gs, gs, 1f);
                }

                layer.Seeds[i] = new Vector2(
                    ((float)layer.Rng.NextDouble() - 0.5f) * tileX,
                    ((float)layer.Rng.NextDouble() - 0.5f) * tileY);
                layer.Stars[i] = starGo.transform;
            }
            layers.Add(layer);
        }

        private void LateUpdate()
        {
            // якорь — камера без тряски, иначе тряску за звёздами не видно
            var follow = CameraFollow.Instance;
            Vector3 anchor = follow != null
                ? follow.AnchorPosition
                : transform.position;

            foreach (Layer layer in layers)
            {
                // z = 0: у якоря z камеры (−10) — слои попали бы в near-плоскость
                layer.Root.position = new Vector3(anchor.x, anchor.y, 0f);
                Vector2 drift = (Vector2)anchor * layer.Factor;
                for (int i = 0; i < layer.Stars.Length; i++)
                    layer.Stars[i].localPosition = Wrap(layer.Seeds[i] - drift);
            }
        }

        private Vector2 Wrap(Vector2 v)
        {
            return new Vector2(WrapAxis(v.x, tileX), WrapAxis(v.y, tileY));
        }

        private static float WrapAxis(float value, float tile)
        {
            return (value % tile + tile) % tile - tile * 0.5f;
        }

        private static Kind Pick(System.Random rng, Kind[] kinds, float total)
        {
            float roll = (float)rng.NextDouble() * total;
            for (int i = 0; i < kinds.Length; i++)
            {
                roll -= kinds[i].Weight;
                if (roll <= 0f)
                    return kinds[i];
            }
            return kinds[kinds.Length - 1];
        }

        // натуральный размер спрайта в юнитах (болванки и декор нарезаны с разным PPU)
        private static float NaturalUnits(Sprite sprite)
        {
            Vector3 size = sprite.bounds.size;
            return Mathf.Max(size.x, size.y);
        }

        // палитры слоёв: звёзды/скопления/пыль везде, яркие sparkle и далёкие
        // солнца — преимущественно вдали
        private static Kind[] FarPalette(Sprite[] decor)
        {
            var kinds = new List<Kind>
            {
                Dust(1.5f, 0.4f, 50),
                StarKind(2.4f, 0.42f, 34),
                Cluster(9f, 0.4f, 8),
                Sparkle(4.5f, 0.55f, 8),
            };
            kinds.Add(Sun(7f, 3));
            AddDecor(kinds, decor, 0, 3, 34f, 0.3f);
            return kinds.ToArray();
        }

        private static Kind[] MidPalette(Sprite[] decor)
        {
            var kinds = new List<Kind>
            {
                Dust(1.4f, 0.4f, 18),
                StarKind(2.8f, 0.6f, 62),
                Cluster(8f, 0.5f, 8),
                Sparkle(5.5f, 0.7f, 12),
            };
            AddDecor(kinds, decor, 1, 6, 16f, 0.35f);
            return kinds.ToArray();
        }

        private static Kind[] NearPalette(Sprite[] decor)
        {
            var kinds = new List<Kind>
            {
                Dust(1.2f, 0.35f, 14),
                StarKind(4.2f, 0.85f, 72),
                Sparkle(7f, 0.9f, 14),
            };
            AddDecor(kinds, decor, 2, 3, 8f, 0.45f);
            return kinds.ToArray();
        }

        private static Kind Dust(float units, float alpha, int weight) => new Kind
        {
            Sprite = StubSprites.SoftDot(6f), // мягкая точка без тёмной окантовки
            Units = units,
            Alpha = alpha,
            Weight = weight,
            TintOverride = new Color(0.85f, 0.9f, 1f),
        };

        private static Kind StarKind(float units, float alpha, int weight) => new Kind
        {
            Sprite = StubSprites.Circle(6f),
            Units = units,
            Alpha = alpha,
            Weight = weight,
        };

        // яркая звезда с крестом-бликом и гало
        private static Kind Sparkle(float units, float alpha, int weight) => new Kind
        {
            Sprite = StubSprites.StarSparkle(6f),
            Units = units,
            Alpha = alpha,
            Weight = weight,
            Glow = true,
            GlowScale = 1.6f,
            GlowAlpha = 0.3f,
        };

        // компактная группа из 3-5 чётких звёзд (сид — форма группы)
        private static Kind Cluster(float units, float alpha, int weight) => new Kind
        {
            Sprite = StubSprites.StarCluster(6f, seed: 7),
            Units = units,
            Alpha = alpha,
            Weight = weight,
        };

        private static Kind Sun(float units, int weight) => new Kind
        {
            Sprite = StubSprites.SoftDot(6f, 0.7f), // горячее ядро + мягкий край
            Units = units,
            Alpha = 0.85f,
            Weight = weight,
            Glow = true,
            GlowScale = 2.4f,
            GlowAlpha = 0.4f,
            TintOverride = SunTint,
        };

        // декор-коллаж по слоям round-robin: ячейка i → слой i % 3
        // (0,3 → дальний; 1,4 → средний; 2,5 → ближний)
        private static void AddDecor(List<Kind> kinds, Sprite[] decor, int layerIndex, int weight, float units, float alpha)
        {
            if (decor == null)
                return;
            for (int i = layerIndex; i < decor.Length; i += 3)
                kinds.Add(new Kind
                {
                    Sprite = decor[i],
                    Units = units,
                    Alpha = alpha,
                    Weight = weight,
                });
        }

        // Декор появляется сам, когда арт положит файл: Multiple-импорт — берём
        // суб-спрайты художника как есть; иначе (Single/обычная текстура) режем
        // 2×3 сетку Sprite.Create-ом — работает при любых настройках импорта.
        private static Sprite[] LoadDecor()
        {
            Sprite[] loaded = Resources.LoadAll<Sprite>(DecorPath);
            if (loaded != null && loaded.Length >= DecorCols * DecorRows)
                return loaded;

            Texture2D tex = loaded != null && loaded.Length == 1
                ? loaded[0].texture
                : Resources.Load<Texture2D>(DecorPath);
            if (tex == null)
                return null;

            float cellW = tex.width / (float)DecorCols;
            float cellH = tex.height / (float)DecorRows;
            float ppu = cellW / DecorCellUnits;
            var sprites = new Sprite[DecorCols * DecorRows];
            for (int row = 0; row < DecorRows; row++)
            {
                for (int col = 0; col < DecorCols; col++)
                {
                    sprites[row * DecorCols + col] = Sprite.Create(
                        tex,
                        new Rect(col * cellW, row * cellH, cellW, cellH),
                        new Vector2(0.5f, 0.5f),
                        ppu);
                }
            }
            return sprites;
        }
    }
}
