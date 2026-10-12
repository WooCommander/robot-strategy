using UnityEditor;
using UnityEngine;

namespace Game.Presentation.Editor
{
    [InitializeOnLoad]
    public static class GameViewAutoActivator
    {
        static GameViewAutoActivator()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                FocusGameView();
            }
        }

        public static void FocusGameView()
        {
            System.Type gameViewType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
            if (gameViewType != null)
            {
                var window = EditorWindow.GetWindow(gameViewType, false, "Game", true);
                if (window != null)
                {
                    window.Focus();
                    Debug.Log("[Robot Strategy] Окно 'Game' автоматически активировано для отображения боевого интерфейса!");
                }
            }
        }
    }
}
