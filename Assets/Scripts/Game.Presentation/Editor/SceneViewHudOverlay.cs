using UnityEditor;
using UnityEngine;

namespace Game.Presentation.Editor
{
    [InitializeOnLoad]
    public static class SceneViewHudOverlay
    {
        static SceneViewHudOverlay()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            if (!EditorApplication.isPlaying) return;

            Handles.BeginGUI();

            // Плашка подсказки и быстрого перехода в Game View
            float w = 360f;
            float h = 50f;
            float x = sceneView.position.width - w - 15f;
            float y = 15f;

            GUI.Box(new Rect(x, y, w, h), "");
            GUI.Label(new Rect(x + 10, y + 6, w - 20, 20), "🎮 <b>Play Mode активен!</b> Боевой интерфейс в Game");

            if (GUI.Button(new Rect(x + 10, y + 26, w - 20, 20), "▶️ ПЕРЕКЛЮЧИТЬСЯ В ОКНО GAME"))
            {
                GameViewAutoActivator.FocusGameView();
            }

            Handles.EndGUI();
        }
    }
}
