using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Presentation.Editor
{
    public static class SceneSwitcherMenu
    {
        private const string BattleScenePath = "Assets/Scenes/BattleScene.unity";

        [MenuItem("Robot Strategy/Открыть сцену боя (BattleScene)", priority = 1)]
        public static void OpenBattleScene()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                var scene = EditorSceneManager.OpenScene(BattleScenePath);
                Debug.Log($"[Robot Strategy] Сцена '{scene.name}' успешно открыта!");
            }
        }

        [MenuItem("Robot Strategy/Запустить сцену боя в Play Mode", priority = 2)]
        public static void PlayBattleScene()
        {
            OpenBattleScene();
            EditorApplication.isPlaying = true;
        }
    }
}
