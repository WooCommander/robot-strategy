using System;
using System.Collections.Generic;
using UnityEngine;
using Game.Core.Types;
using Game.Presentation.Rendering;

namespace Game.Presentation.Views
{
    /// <summary>
    /// Маршрут выбранного своего робота: бегущий пунктир по рельефу до цели и пульсирующее кольцо в точке назначения.
    /// </summary>
    public sealed class PathPreviewView : MonoBehaviour
    {
        private const float Lift = 0.14f;
        private const float SampleStep = 0.75f;
        private static readonly Color PathColor = new Color(0.45f, 1.5f, 2.1f, 0.9f);

        private readonly List<Vector3> _points = new List<Vector3>();
        private Func<SimVector2, float> _surfaceHeight;
        private LineRenderer _line;
        private MeshRenderer _target;
        private Material _lineMaterial;

        public void Initialize(Func<SimVector2, float> surfaceHeight)
        {
            _surfaceHeight = surfaceHeight;
            _lineMaterial = FxLibrary.Glow("Fx_PathDash", FxLibrary.Dash, PathColor);
            // Два штриха на метр; бегущее смещение задаётся в Update.
            _lineMaterial.mainTextureScale = new Vector2(2f, 1f);

            _line = gameObject.AddComponent<LineRenderer>();
            _line.sharedMaterial = _lineMaterial;
            _line.textureMode = LineTextureMode.Tile;
            _line.alignment = LineAlignment.View;
            _line.startWidth = _line.endWidth = 0.14f;
            _line.numCornerVertices = 2;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.enabled = false;

            _target = FxLibrary.CreateGroundRing("PathTarget", transform, 0.32f, 0.5f, PathColor, 0f);
            _target.enabled = false;
        }

        /// <summary>Показать маршрут робота или скрыть (null, чужой робот, нет пути).</summary>
        public void Show(RobotView robot)
        {
            if (_line == null) return;
            if (robot == null || robot.State == null || robot.Owner != PlayerId.Player1 || !robot.State.HasPath)
            {
                _line.enabled = false;
                _target.enabled = false;
                return;
            }

            _points.Clear();
            Vector3 start = robot.transform.position;
            _points.Add(new Vector3(start.x, start.y + Lift, start.z));
            List<SimVector2> path = robot.State.Path;
            for (int i = robot.State.PathIndex; i < path.Count; i++) AppendAlongTerrain(path[i]);

            _line.positionCount = _points.Count;
            for (int i = 0; i < _points.Count; i++) _line.SetPosition(i, _points[i]);
            _line.enabled = true;

            Vector3 end = _points[_points.Count - 1];
            _target.transform.position = new Vector3(end.x, end.y - Lift + 0.06f, end.z);
            _target.transform.localScale = Vector3.one * (1f + Mathf.Sin(Time.time * 5f) * 0.12f);
            _target.enabled = true;
        }

        /// <summary>Добавляет точку пути, деля длинный отрезок, чтобы линия огибала холмы, а не резала их.</summary>
        private void AppendAlongTerrain(SimVector2 waypoint)
        {
            Vector3 from = _points[_points.Count - 1];
            var to = new Vector2(waypoint.X, waypoint.Y);
            float distance = Vector2.Distance(new Vector2(from.x, from.z), to);
            int steps = Mathf.Max(1, Mathf.CeilToInt(distance / SampleStep));
            for (int step = 1; step <= steps; step++)
            {
                Vector2 point = Vector2.Lerp(new Vector2(from.x, from.z), to, (float)step / steps);
                float height = _surfaceHeight != null ? Mathf.Max(0f, _surfaceHeight(new SimVector2(point.x, point.y))) : 0f;
                _points.Add(new Vector3(point.x, height + Lift, point.y));
            }
        }

        private void Update()
        {
            if (_lineMaterial != null && _line.enabled) _lineMaterial.mainTextureOffset = new Vector2(-Time.time * 1.5f, 0f);
        }

        private void OnDestroy()
        {
            if (_lineMaterial != null) Destroy(_lineMaterial);
        }
    }
}
