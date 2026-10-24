using UnityEngine;

namespace Game.Presentation.Rendering
{
    public static class CameraZoom
    {
        /// <summary>
        /// Половина видимой высоты кадра в мировых единицах у точки: для ortho-камеры это orthographicSize,
        /// для перспективной — зависит от расстояния. По ней world-space подписи масштабируются вместе с зумом.
        /// </summary>
        public static float ViewHalfHeight(UnityEngine.Camera cam, Vector3 point)
        {
            if (cam.orthographic) return cam.orthographicSize;
            float distance = Vector3.Dot(point - cam.transform.position, cam.transform.forward);
            return Mathf.Max(0.01f, distance) * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        }
    }
}
