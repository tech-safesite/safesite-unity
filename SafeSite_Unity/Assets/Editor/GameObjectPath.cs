using UnityEditor;
using UnityEngine;

public static class GameObjectPathUtility
{
    [MenuItem("Tools/Copy Selected Object Path")]
    private static void CopySelectedObjectPath()
    {
        if (Selection.activeGameObject == null)
            return;

        string path = GetPath(Selection.activeGameObject.transform);

        EditorGUIUtility.systemCopyBuffer = path;
        Debug.Log(path);
    }

    private static string GetPath(Transform transform)
    {
        string path = transform.name;

        while (transform.parent != null)
        {
            transform = transform.parent;
            path = transform.name + "/" + path;
        }

        return path;
    }
}