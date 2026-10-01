using UnityEngine;
using UnityEngine.EventSystems;

namespace Roguelike.Tests.RouteGeneration
{
    public sealed class RouteHorizontalBrowser : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        private RectTransform _viewport;
        private RectTransform _content;
        private bool _holdLeft;
        private bool _holdRight;
        private float _speed;
        private float _dragStartContentX;
        private Vector2 _dragStartPointer;

        public void Initialize(RectTransform viewport, RectTransform content, float speed)
        {
            _viewport = viewport;
            _content = content;
            _speed = Mathf.Max(1f, speed);
            ResetToStart();
        }

        public void SetHeld(int direction, bool held)
        {
            if (direction < 0)
                _holdLeft = held;
            else if (direction > 0)
                _holdRight = held;
        }

        public void ResetToStart()
        {
            if (_content == null)
                return;
            Vector2 position = _content.anchoredPosition;
            position.x = 0f;
            _content.anchoredPosition = position;
        }

        public void ClampToBounds()
        {
            if (_viewport == null || _content == null)
                return;
            float minimumX = Mathf.Min(0f, _viewport.rect.width - _content.rect.width);
            Vector2 position = _content.anchoredPosition;
            position.x = Mathf.Clamp(position.x, minimumX, 0f);
            _content.anchoredPosition = position;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_viewport == null || _content == null)
                return;
            _dragStartContentX = _content.anchoredPosition.x;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _viewport, eventData.position, eventData.pressEventCamera, out _dragStartPointer);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_viewport == null || _content == null)
                return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _viewport, eventData.position, eventData.pressEventCamera, out Vector2 pointer))
                return;
            Vector2 position = _content.anchoredPosition;
            position.x = _dragStartContentX + pointer.x - _dragStartPointer.x;
            _content.anchoredPosition = position;
            ClampToBounds();
        }

        private void Update()
        {
            if (_content == null)
                return;
            bool keyboardLeft = Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A);
            bool keyboardRight = Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D);
            float direction = (_holdLeft || keyboardLeft ? 1f : 0f) - (_holdRight || keyboardRight ? 1f : 0f);
            if (Mathf.Approximately(direction, 0f))
                return;
            Vector2 position = _content.anchoredPosition;
            position.x += direction * _speed * Time.unscaledDeltaTime;
            _content.anchoredPosition = position;
            ClampToBounds();
        }

        private void OnRectTransformDimensionsChange()
        {
            ClampToBounds();
        }
    }
}
