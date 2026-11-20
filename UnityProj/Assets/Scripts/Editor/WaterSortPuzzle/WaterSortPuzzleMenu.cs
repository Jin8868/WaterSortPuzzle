using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Game.WaterSortPuzzle.Editor
{
    /// <summary>从业务启动场景试玩完整的框架界面流程。</summary>
    public static class WaterSortPuzzleMenu
    {
        private const string BOOTSCENE = "Assets/Res/Scenes/Boot.unity"; // 框架启动场景。

        [MenuItem("★AlloyFramework★/倒水游戏/从启动场景试玩")]
        private static void StartPreview()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            // 切换前沿用编辑器的保存提示，避免丢失正在编辑的界面场景。
            if (SceneManager.GetActiveScene().path != BOOTSCENE)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(BOOTSCENE);
            }
            EditorApplication.isPlaying = true;
        }
    }
}
