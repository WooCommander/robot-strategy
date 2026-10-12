using UnityEngine;

namespace Game.Platform
{
    public class PlatformLifecycle : MonoBehaviour
    {
        private void OnApplicationPause(bool pauseStatus)
        {
            // Автоматическая пауза при сворачивании (ТЗ раздел 3)
            Debug.Log($"[Platform] Application pause: {pauseStatus}");
        }
    }
}
