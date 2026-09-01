using UnityEngine;
using UnityEngine.EventSystems;

namespace Roguelike.Tests.RouteGeneration
{
    public sealed class HoldScrollButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        private RouteHorizontalBrowser _browser;
        private int _direction;

        public void Initialize(RouteHorizontalBrowser browser, int direction)
        {
            _browser = browser;
            _direction = direction;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _browser?.SetHeld(_direction, true);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _browser?.SetHeld(_direction, false);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _browser?.SetHeld(_direction, false);
        }

        private void OnDisable()
        {
            _browser?.SetHeld(_direction, false);
        }
    }
}
