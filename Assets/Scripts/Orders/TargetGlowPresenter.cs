using UnityEngine;
using SpaceTaxi.Core;
using SpaceTaxi.Physics;
using SpaceTaxi.World;

namespace SpaceTaxi.Orders
{
    // Жёлтый ореол планеты назначения: домен сообщает смену цели строкой
    // (SetTarget — «А» ждёт, «Б» на борту), презентер сам создаёт круг,
    // цепляет его к телу и пульсирует
    public sealed class TargetGlowPresenter : MonoBehaviour
    {
        private Transform targetGlow;
        private SpriteRenderer glowRenderer;
        private string requestedBody;
        private float glowScaleBase = 1f;
        private GravityField gravityField;

        // домен зовёт каждый тик с текущим именем цели (или null — спрятать);
        // повторное то же имя без живого ореола — no-op, смены нет
        public void SetTarget(string bodyName)
        {
            if (requestedBody == bodyName && targetGlow != null && targetGlow.parent != null)
                return;
            requestedBody = bodyName;
            Refresh();
        }

        // пульс масштабом и альфой — заметный жёлтый круг за планетой;
        // создание/прикрепление — только при смене цели (Refresh), не каждый кадр
        private void Update()
        {
            if (glowRenderer == null || targetGlow == null || !targetGlow.gameObject.activeSelf)
                return;
            float wave = Mathf.Sin(Time.time * 3f);
            targetGlow.localScale = Vector3.one * glowScaleBase * (1f + 0.06f * wave);
            // жёлтый полупрозрачный пульс — жёлтый = цель заказа; тон тот же,
            // что у кольца цели на карте (MapScreenBinder.TargetRingColor)
            glowRenderer.color = new Color(1f, 0.94f, 0.3f, Mathf.Lerp(0.38f, 0.62f, 0.5f + 0.5f * wave));
        }

        private void Refresh()
        {
            if (string.IsNullOrEmpty(requestedBody))
            {
                if (targetGlow != null)
                    targetGlow.gameObject.SetActive(false);
                return;
            }

            if (gravityField == null)
                gravityField = FindAnyObjectByType<GravityField>();
            if (gravityField == null)
                return;

            if (targetGlow == null)
            {
                var glowGo = new GameObject("TargetGlow");
                glowRenderer = glowGo.AddComponent<SpriteRenderer>();
                glowRenderer.sortingOrder = -2; // позади планеты
                glowRenderer.sprite = StubSprites.SoftDot(128f, 0.55f); // просто жёлтый круг, не копия спрайта планеты
                targetGlow = glowGo.transform;
            }

            if (requestedBody != glowBodyName || targetGlow.parent == null)
            {
                foreach (CelestialBody body in gravityField.Bodies)
                {
                    if (body.DisplayName != requestedBody)
                        continue;
                    var bodyRenderer = body.GetComponent<SpriteRenderer>();
                    // диаметр круга — от реального размера планеты: круг чуть
                    // больше тела. Видимый шар = bodyScale × контент арта
                    // (вписывание в Radius делает CelestialBody.Init, шар в мире
                    // = 2·Radius); SoftDot(128) при масштабе 1 = bounds 128 юн
                    // (PPU 64/128 = 0.5), делим на размер спрайта ореола и
                    // масштаб тела-родителя
                    float softDotUnits = glowRenderer.sprite != null
                        ? glowRenderer.sprite.bounds.size.x
                        : 128f;
                    float bodyScale = Mathf.Max(Mathf.Abs(body.transform.localScale.x), 0.0001f);
                    float planetUnits = bodyRenderer != null && bodyRenderer.sprite != null
                        ? Mathf.Max(SpriteArt.ContentSize(bodyRenderer.sprite).x,
                                    SpriteArt.ContentSize(bodyRenderer.sprite).y)
                        : 2f * body.Radius / bodyScale;
                    targetGlow.SetParent(body.transform, false);
                    glowScaleBase = planetUnits * 1.25f / (softDotUnits * bodyScale);
                    break;
                }
            }

            glowBodyName = requestedBody;
            targetGlow.gameObject.SetActive(glowRenderer.sprite != null);
        }

        private string glowBodyName;

        // ореол — ребёнок тела уровня: при смерти презентера (снос сцены)
        // объект гибнет вместе с телами; явный Destroy — если тела пережили
        private void OnDestroy()
        {
            if (targetGlow != null)
                Destroy(targetGlow.gameObject);
        }
    }
}
