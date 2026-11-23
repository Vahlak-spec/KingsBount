using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace KingdomBouns.Core
{
    /// <summary>
    /// Жести для мобільної стратегії: тап, довге натискання, перетягування, pinch-zoom; у редакторі — миша й коліщатко.
    /// Жест, що почався над UI, ігнорується до відпускання. Усі координати — екранні (пікселі).
    ///
    ///   _input.onTap += screenPos =&gt; { if (_camera.TryGetGroundPoint(screenPos, out var world)) SelectHex(world); };
    /// </summary>
    public sealed class InputService : IGameService, IDisposable
    {
        /// <summary>Палець/кнопка торкнулися не над UI (початок будь-якого жесту).</summary>
        public event Action<Vector2> onPointerDown;
        /// <summary>Короткий дотик без руху.</summary>
        public event Action<Vector2> onTap;
        /// <summary>Утримання без руху довше LongPressTime. Після нього тап не спрацьовує.</summary>
        public event Action<Vector2> onLongPress;
        /// <summary>Перетягування одним пальцем або центром двох пальців: (позиція, зміщення за кадр).</summary>
        public event Action<Vector2, Vector2> onDrag;
        /// <summary>Перетягування/pinch закінчилось (для інерції камери).</summary>
        public event Action onDragEnd;
        /// <summary>Масштаб: множник (&gt;1 — наблизити) і точка на екрані, до якої зумимо.</summary>
        public event Action<float, Vector2> onZoom;
        /// <summary>Android «Назад» / Escape.</summary>
        public event Action onBack;

        public float TapMaxDuration = 0.35f;
        public float LongPressTime = 0.5f;
        /// <summary>Поріг руху до початку drag, у дюймах екрана (≈2.5 мм).</summary>
        public float DragThresholdInches = 0.1f;
        public float WheelZoomStep = 0.15f;

        public bool IsEnabled { get; private set; } = true;
        public bool IsDragging => _gesture == Gesture.Drag || _gesture == Gesture.Pinch;

        private enum Gesture { None, Pending, Drag, Pinch, Ignored }

        private readonly List<PointerState> _pointers = new List<PointerState>(4);
        private readonly List<RaycastResult> _uiHits = new List<RaycastResult>();
        private PointerEventData _uiPointer;
        private EventSystem _uiPointerOwner;

        private Ticker _ticker;
        private Gesture _gesture;
        private Vector2 _startPos;
        private Vector2 _lastPos;
        private float _startTime;
        private bool _longPressFired;
        private float _lastPinchDistance;
        private Vector2 _lastPinchCenter;

        public void Bind(ServicesProvider services)
        {
            _ticker = services.Ticker;
        }

        public void OnLaunchGame()
        {
            _ticker.onUpdate += Tick;
        }

        public void Dispose()
        {
            if (_ticker != null)
                _ticker.onUpdate -= Tick;
        }

        /// <summary>Вимкнути жести карти (наприклад, поки відкрите модальне вікно). Поточний жест скасовується.</summary>
        public void SetEnabled(bool value)
        {
            IsEnabled = value;
            if (!value)
                CancelGesture();
        }

        public bool IsPointerOverUI(Vector2 screenPosition)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
                return false;

            if (_uiPointer == null || _uiPointerOwner != eventSystem)
            {
                _uiPointer = new PointerEventData(eventSystem);
                _uiPointerOwner = eventSystem;
            }

            _uiPointer.position = screenPosition;
            _uiHits.Clear();
            eventSystem.RaycastAll(_uiPointer, _uiHits);
            return _uiHits.Count > 0;
        }

        private float DragThresholdPixels
        {
            get
            {
                float dpi = Screen.dpi > 1f ? Screen.dpi : 160f;
                return DragThresholdInches * dpi;
            }
        }

        private void Tick()
        {
            PointerReader.Read(_pointers, out float scroll, out Vector2 mousePosition, out bool back);

            if (back)
                onBack?.Invoke();

            if (!IsEnabled)
                return;

            if (scroll != 0f && _pointers.Count == 0 && !IsPointerOverUI(mousePosition))
                onZoom?.Invoke(Mathf.Max(0.1f, 1f + scroll * WheelZoomStep), mousePosition);

            switch (_pointers.Count)
            {
                case 0: OnReleased(); break;
                case 1: OnSinglePointer(_pointers[0].Position); break;
                default: OnTwoPointers(_pointers[0].Position, _pointers[1].Position); break;
            }
        }

        private void OnReleased()
        {
            if (_gesture == Gesture.Pending && !_longPressFired && Time.unscaledTime - _startTime <= TapMaxDuration)
                onTap?.Invoke(_lastPos);
            else if (_gesture == Gesture.Drag || _gesture == Gesture.Pinch)
                onDragEnd?.Invoke();

            _gesture = Gesture.None;
        }

        private void OnSinglePointer(Vector2 position)
        {
            switch (_gesture)
            {
                case Gesture.None:
                    if (IsPointerOverUI(position))
                    {
                        _gesture = Gesture.Ignored;
                        return;
                    }

                    _gesture = Gesture.Pending;
                    _startPos = _lastPos = position;
                    _startTime = Time.unscaledTime;
                    _longPressFired = false;
                    onPointerDown?.Invoke(position);
                    return;

                case Gesture.Ignored:
                    return;

                case Gesture.Pinch:
                    // Один палець прибрали — продовжуємо як drag без стрибка камери.
                    _gesture = Gesture.Drag;
                    _lastPos = position;
                    return;

                case Gesture.Pending:
                    if ((position - _startPos).sqrMagnitude > DragThresholdPixels * DragThresholdPixels)
                    {
                        _gesture = Gesture.Drag;
                        _lastPos = _startPos;   // карта «наздоганяє» палець, а не відстає на поріг
                    }
                    else
                    {
                        if (!_longPressFired && Time.unscaledTime - _startTime >= LongPressTime)
                        {
                            _longPressFired = true;
                            onLongPress?.Invoke(position);
                        }
                        _lastPos = position;
                        return;
                    }
                    break;
            }

            // Gesture.Drag
            Vector2 delta = position - _lastPos;
            if (delta != Vector2.zero)
                onDrag?.Invoke(position, delta);
            _lastPos = position;
        }

        private void OnTwoPointers(Vector2 a, Vector2 b)
        {
            if (_gesture == Gesture.Ignored)
                return;

            Vector2 center = (a + b) * 0.5f;
            float distance = Vector2.Distance(a, b);

            if (_gesture != Gesture.Pinch)
            {
                if (_gesture == Gesture.None)
                {
                    if (IsPointerOverUI(center))
                    {
                        _gesture = Gesture.Ignored;
                        return;
                    }
                    onPointerDown?.Invoke(center);
                }

                _gesture = Gesture.Pinch;
                _lastPinchDistance = distance;
                _lastPinchCenter = center;
                return;
            }

            if (_lastPinchDistance > 1f && distance > 1f)
            {
                float factor = distance / _lastPinchDistance;
                if (!Mathf.Approximately(factor, 1f))
                    onZoom?.Invoke(factor, center);
            }

            Vector2 delta = center - _lastPinchCenter;
            if (delta != Vector2.zero)
                onDrag?.Invoke(center, delta);

            _lastPinchDistance = distance;
            _lastPinchCenter = center;
        }

        private void CancelGesture()
        {
            if (_gesture == Gesture.Drag || _gesture == Gesture.Pinch)
                onDragEnd?.Invoke();
            _gesture = _pointers.Count > 0 ? Gesture.Ignored : Gesture.None;
        }
    }
}
