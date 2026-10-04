// Put this in an "Editor" folder
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

public static class MoveRenderersToChild
{
    [MenuItem("Tools/Move Renderers To Child")]
    static void Run()
    {
        foreach (var obj in Selection.GetFiltered<GameObject>(SelectionMode.Assets))
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (!path.EndsWith(".prefab")) continue;

            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (Process(root))
                    PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        AssetDatabase.SaveAssets();
    }

    static bool Process(GameObject root)
    {
        var mf = root.GetComponent<MeshFilter>();
        var mr = root.GetComponent<MeshRenderer>();
        if (mf == null && mr == null) return false;
        if (root.transform.Find("Visual") != null) return false; // already done

        var child = new GameObject("Visual");
        child.transform.SetParent(root.transform, false);
        child.layer = root.layer;

        // MeshFilter first, then MeshRenderer (copies all settings and materials)
        if (mf != null) { ComponentUtility.CopyComponent(mf); ComponentUtility.PasteComponentAsNew(child); }
        if (mr != null) { ComponentUtility.CopyComponent(mr); ComponentUtility.PasteComponentAsNew(child); }

        var newRenderer = child.GetComponent<MeshRenderer>();

        // keep CarAI.bodyRenderers pointing at the right renderer
        var car = root.GetComponent<CarAI>();
        if (car != null && car.bodyRenderers != null)
        {
            for (int i = 0; i < car.bodyRenderers.Length; i++)
                if (car.bodyRenderers[i] == mr) car.bodyRenderers[i] = newRenderer;
        }

        // the renderer must be removed before the filter
        if (mr != null) Object.DestroyImmediate(mr);
        if (mf != null) Object.DestroyImmediate(mf);
        return true;
    }
}