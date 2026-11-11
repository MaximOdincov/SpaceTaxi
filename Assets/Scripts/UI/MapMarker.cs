using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpaceTaxi.UI
{
    // Кликабельный маркер карты: hover ×1.15 + подсветка, клик — наружу.
    // Вешается на корень группы маркера; события приходят с дочерней иконки (пузырём).
    // Фоновым ловушкам клика — hoverEnabled = false. Курсор системный всегда (Н6.K8):
    // собственный курсор-pointer менялся «криво» — убран.
    public class MapMarker : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public bool hoverEnabled = true;
        public Graphic visual; // что подсвечивать (иконка); пусто — первый Graphic в дереве

        public event Action Clicked;
        public event Action HoverEntered;
        public event Action HoverExited;

        private Vector3 baseScale;
        private Color baseColor;
        private bool captured;
        private bool hovered;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!hoverEnabled)
                return;
            Capture();
            hovered = true;
            transform.localScale = baseScale * 1.15f;
            if (visual != null)
                visual.color = Color.Lerp(baseColor, Color.white, 0.35f);
            HoverEntered?.Invoke();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!hovered)
                return;
            hovered = false;
            transform.localScale = baseScale;
            if (visual != null)
                visual.color = baseColor;
            HoverExited?.Invoke();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
                Clicked?.Invoke();
        }

        private void OnDisable()
        {
            if (hovered)
                OnPointerExit(null); // карта закрылась под курсором
        }

        private void Capture()
        {
            if (captured)
                return;
            captured = true;
            baseScale = transform.localScale;
            if (visual == null)
                visual = GetComponent<Graphic>();
            if (visual == null)
                visual = GetComponentInChildren<Graphic>();
            if (visual != null)
                baseColor = visual.color;
        }
    }
}
