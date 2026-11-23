using System;
using UnityEngine;

namespace KingdomBouns.Core
{
    [Serializable]
    public class StrategyCameraSettings
    {
        [Tooltip("Висота площини карти (Y).")]
        public float GroundHeight = 0f;

        [Header("Zoom (perspective: висота камери над картою)")]
        public float MinHeight = 8f;
        public float MaxHeight = 40f;

        [Header("Zoom (orthographic: orthographicSize)")]
        public float MinOrthoSize = 4f;
        public float MaxOrthoSize = 20f;

        [Header("Межі: точка в центрі екрана не виходить за прямокутник XZ")]
        public bool UseBounds = true;
        public Rect Bounds = new Rect(-50f, -50f, 100f, 100f);

        [Header("Інерція після відпускання")]
        public bool UseInertia = true;
        [Tooltip("Більше — швидше зупиняється.")]
        public float InertiaDamping = 5f;
    }

    /// <summary>
    /// Камера стратегії над площиною карти: pan пальцем/мишею (точка карти «прилипає» до пальця),
    /// pinch/коліщатко — зум до точки, межі карти та інерція. Нахил і поворот камери задаєш у сцені — сервіс їх не змінює.
    /// </summary>
    public sealed class StrategyCameraService : IGameService, IDisposable
    {
        public Camera Camera => _camera;
        public bool IsEnabled { get; private set; } = true;

        private readonly Camera _camera;
        private readonly Transform _transform;
        private readonly StrategyCameraSettings _settings;

        private InputService _input;
        private Ticker _ticker;
        private Plane _ground;
        private Vector3 _velocity;
        private bool _isDragging;

        public StrategyCameraService(Camera camera, StrategyCameraSettings settings)
        {
            if (camera == null)
                throw new ArgumentNullException(nameof(camera));

            _camera = camera;
            _transform = camera.transform;
            _settings = settings ?? new StrategyCameraSettings();
            _ground = new Plane(Vector3.up, new Vector3(0f, _settings.GroundHeight, 0f));
        }

        public void Bind(ServicesProvider services)
        {
            _input = services.GetService<InputService>();
            _ticker = services.Ticker;
        }

        public void OnLaunchGame()
        {
            _input.onPointerDown += OnPointerDown;
            _input.onDrag += OnDrag;
            _input.onDragEnd += OnDragEnd;
            _input.onZoom += OnZoom;
            _ticker.onLateUpdate += Tick;

            ApplyZoom(1f);
            ClampToBounds();
        }

        public void Dispose()
        {
            if (_input != null)
            {
                _input.onPointerDown -= OnPointerDown;
                _input.onDrag -= OnDrag;
                _input.onDragEnd -= OnDragEnd;
                _input.onZoom -= OnZoom;
            }
            if (_ticker != null)
                _ticker.onLateUpdate -= Tick;
        }

        public void SetEnabled(bool value)
        {
            IsEnabled = value;
            _velocity = Vector3.zero;
            _isDragging = false;
        }

        /// <summary>Точка карти під екранною позицією (для вибору гексу).</summary>
        public bool TryGetGroundPoint(Vector2 screenPosition, out Vector3 worldPoint)
        {
            Ray ray = _camera.ScreenPointToRay(screenPosition);
            if (_ground.Raycast(ray, out float distance))
            {
                worldPoint = ray.GetPoint(distance);
                return true;
            }

            worldPoint = default;
            return false;
        }

        /// <summary>Миттєво поставити точку карти в центр екрана.</summary>
        public void FocusOn(Vector3 worldPoint)
        {
            _velocity = Vector3.zero;
            if (!TryGetCenterPoint(out Vector3 center))
                return;

            Vector3 offset = worldPoint - center;
            offset.y = 0f;
            _transform.position += offset;
            ClampToBounds();
        }

        private void OnPointerDown(Vector2 _)
        {
            _velocity = Vector3.zero;
        }

        private void OnDrag(Vector2 position, Vector2 delta)
        {
            if (!IsEnabled)
                return;

            if (!TryGetGroundPoint(position - delta, out Vector3 from) || !TryGetGroundPoint(position, out Vector3 to))
                return;

            Vector3 move = from - to;
            move.y = 0f;
            _transform.position += move;
            ClampToBounds();

            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            _velocity = Vector3.Lerp(_velocity, move / dt, 0.5f);
            _isDragging = true;
        }

        private void OnDragEnd()
        {
            _isDragging = false;
            if (!_settings.UseInertia)
                _velocity = Vector3.zero;
        }

        private void OnZoom(float factor, Vector2 pivot)
        {
            if (!IsEnabled)
                return;

            bool hasBefore = TryGetGroundPoint(pivot, out Vector3 before);
            ApplyZoom(factor);

            // Точка під пальцем/курсором лишається на місці.
            if (hasBefore && TryGetGroundPoint(pivot, out Vector3 after))
            {
                Vector3 correction = before - after;
                correction.y = 0f;
                _transform.position += correction;
            }

            ClampToBounds();
        }

        private void ApplyZoom(float factor)
        {
            if (_camera.orthographic)
            {
                _camera.orthographicSize = Mathf.Clamp(_camera.orthographicSize / factor,
                    _settings.MinOrthoSize, _settings.MaxOrthoSize);
                return;
            }

            Vector3 forward = _transform.forward;
            if (forward.y > -0.05f)
                return;   // камера дивиться майже горизонтально — зум по висоті не має сенсу

            float height = _transform.position.y - _settings.GroundHeight;
            float target = Mathf.Clamp(height / factor, _settings.MinHeight, _settings.MaxHeight);
            float along = (height - target) / -forward.y;
            _transform.position += forward * along;
        }

        private void Tick()
        {
            if (!IsEnabled || _isDragging || !_settings.UseInertia)
                return;

            if (_velocity.sqrMagnitude < 0.0001f)
            {
                _velocity = Vector3.zero;
                return;
            }

            float dt = Time.unscaledDeltaTime;
            _transform.position += _velocity * dt;
            _velocity *= Mathf.Exp(-_settings.InertiaDamping * dt);
            ClampToBounds();
        }

        private bool TryGetCenterPoint(out Vector3 point)
            => TryGetGroundPoint(new Vector2(_camera.pixelWidth * 0.5f, _camera.pixelHeight * 0.5f), out point);

        private void ClampToBounds()
        {
            if (!_settings.UseBounds || !TryGetCenterPoint(out Vector3 center))
                return;

            Rect b = _settings.Bounds;
            float x = Mathf.Clamp(center.x, b.xMin, b.xMax);
            float z = Mathf.Clamp(center.z, b.yMin, b.yMax);

            if (!Mathf.Approximately(x, center.x))
                _velocity.x = 0f;
            if (!Mathf.Approximately(z, center.z))
                _velocity.z = 0f;

            _transform.position += new Vector3(x - center.x, 0f, z - center.z);
        }
    }
}
