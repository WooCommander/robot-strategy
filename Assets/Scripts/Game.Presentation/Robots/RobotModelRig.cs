using UnityEngine;

namespace Game.Presentation.Robots
{
    /// <summary>
    /// Анимация процедурной модели робота: шаги ног по фактической скорости, парение антиграва,
    /// вращение радара и поворот башни к цели после выстрела. Только визуал, симуляцию не трогает.
    /// </summary>
    public sealed class RobotModelRig : MonoBehaviour
    {
        private const float AimHoldSeconds = 2.5f;
        private const float TurretTurnSpeed = 270f;

        public Transform Body;
        public Transform Turret;
        public Transform Radar;
        public Transform LeftLeg;
        public Transform RightLeg;
        public bool Hovering;
        /// <summary>Высота верхней точки модели: по ней ставится полоска HP.</summary>
        public float Height;

        private Vector3 _lastPosition;
        private float _bodyBaseY;
        private float _walkPhase;
        private float _hoverPhase;
        private float _aimYaw;
        private float _aimUntil;

        private void Start()
        {
            _lastPosition = transform.position;
            if (Body != null) _bodyBaseY = Body.localPosition.y;
            // Разные фазы, чтобы группа антигравов не качалась синхронно.
            _hoverPhase = (transform.position.x + transform.position.z) * 0.7f;
        }

        /// <summary>Повернуть башню к точке на несколько секунд, затем вернуть по курсу.</summary>
        public void AimAt(Vector3 worldPoint)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            _aimYaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            _aimUntil = Time.time + AimHoldSeconds;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 delta = transform.position - _lastPosition;
            delta.y = 0f;
            float speed = delta.magnitude / dt;
            _lastPosition = transform.position;

            float bodyOffset = 0f;
            if (LeftLeg != null && RightLeg != null)
            {
                float stride = Mathf.Clamp01(speed / 1.5f);
                _walkPhase += speed * dt * 4.5f;
                float swing = Mathf.Sin(_walkPhase) * 28f * stride;
                LeftLeg.localRotation = Quaternion.Euler(swing, 0f, 0f);
                RightLeg.localRotation = Quaternion.Euler(-swing, 0f, 0f);
                bodyOffset = Mathf.Abs(Mathf.Cos(_walkPhase)) * 0.035f * stride;
            }
            if (Hovering) bodyOffset = Mathf.Sin(Time.time * 2.2f + _hoverPhase) * 0.05f;
            if (Body != null)
            {
                Vector3 position = Body.localPosition;
                position.y = _bodyBaseY + bodyOffset;
                Body.localPosition = position;
            }

            if (Radar != null) Radar.Rotate(Vector3.up, 90f * dt, Space.Self);

            if (Turret != null)
            {
                float targetYaw = Time.time < _aimUntil ? _aimYaw : 0f;
                Turret.localRotation = Quaternion.RotateTowards(Turret.localRotation, Quaternion.Euler(0f, targetYaw, 0f), TurretTurnSpeed * dt);
            }
        }
    }
}
