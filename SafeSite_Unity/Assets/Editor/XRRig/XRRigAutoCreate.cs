using UnityEditor;
using UnityEditor.SceneManagement;

namespace SafeSite.Build.XRRig
{
    [InitializeOnLoad]
    internal static class XRRigAutoCreate
    {
        private const string EnabledPrefKey = "SafeSite.XRRig.AutoCreateOnSceneOpen";

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(EnabledPrefKey, false);
            set => EditorPrefs.SetBool(EnabledPrefKey, value);
        }

        static XRRigAutoCreate()
        {
            EditorSceneManager.sceneOpened += OnSceneOpened;
        }

        private static void OnSceneOpened(UnityEngine.SceneManagement.Scene scene, OpenSceneMode mode)
        {
            if (!Enabled || EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            // Only project scenes — never touch scenes opened from inside an imported package/sample.
            if (!scene.path.StartsWith("Assets/Scenes/"))
                return;

            // Defer out of the sceneOpened callback: this can fire right after a package resolve/domain
            // reload, and running AssetDatabase-heavy sample import synchronously in that window has
            // caused Unity to cache broken nested-prefab imports. Let the editor settle first.
            var scenePath = scene.path;
            EditorApplication.delayCall += () =>
            {
                var reopened = EditorSceneManager.GetSceneByPath(scenePath);
                if (reopened.IsValid() && reopened.isLoaded)
                    XRRigBuilder.BuildInScene(reopened, interactive: false);
            };
        }
    }
}
