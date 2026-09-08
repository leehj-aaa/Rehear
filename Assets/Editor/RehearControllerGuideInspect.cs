using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
internal static class RehearControllerGuideInspect
{
    static RehearControllerGuideInspect() { EditorApplication.update += Poll; }
    static void Poll()
    {
        const string request = "Temp/RehearControllerGuideInspect.request";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        File.Delete(request);
        var text = new StringBuilder();
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath("deed68a7fd2e9234899298cf80fae7ad"));
        Dump(model.transform, text);
        var all = EditorSceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true));
        foreach (var t in all.Where(t => t.name == "Right Controller")) Dump(t, text);
        var view = Object.FindFirstObjectByType<TutorialFigmaView>(FindObjectsInactive.Include);
        if (view) Dump(view.steps[1].transform, text);
        File.WriteAllText("Temp/RehearControllerGuideInspect.txt", text.ToString());
    }
    static void Dump(Transform root, StringBuilder text)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            text.AppendLine(AnimationUtility.CalculateTransformPath(t, root) + " pos=" + t.localPosition.ToString("F4") + " rot=" + t.localEulerAngles + " scale=" + t.localScale + " active=" + t.gameObject.activeSelf + " components=" + string.Join(",", t.GetComponents<Component>().Where(c=>c).Select(c=>c.GetType().Name)));
    }
}
