using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class RehearPhotoGripPivotSetup
{
    private const string Request = "Temp/RehearPhotoGripPivot.request";
    private const string Report = "Temp/RehearPhotoGripPivot.txt";
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    static RehearPhotoGripPivotSetup() => EditorApplication.update += Poll;

    private static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request);
        try { Apply(); }
        catch (Exception error) { File.WriteAllText(Report, error.ToString()); }
    }

    private static void Apply()
    {
        var report = new StringBuilder();
        // Exact Aud_W_01 values confirmed in the inspector while the authored
        // PhotoSlide pose is frozen. Do not infer these from an older saved pivot.
        var sourcePosition = new Vector3(-0.0936f, 0.0331f, -0.0323f);
        var sourceEuler = new Vector3(-19.95f, 134.089f, 81.225f);
        var sourceRotation = Quaternion.Euler(sourceEuler);
        var sourceScale = Vector3.one;
        report.AppendLine($"SOURCE Aud_W_01 inspector: pivotLocal={sourcePosition:F4}, pivotEuler={sourceEuler:F3}");
        foreach (var path in Directory.GetFiles("Assets/03_Prefabs/Audience/Presentation", "Aud_*.prefab")
                     .Select(value => value.Replace('\\', '/')).OrderBy(value => value, StringComparer.Ordinal))
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var player = root.GetComponentInChildren<AudienceAnimationPlayer>(true);
                var phone = (GameObject)typeof(AudienceAnimationPlayer).GetField("photoPhone", PrivateInstance)
                    ?.GetValue(player);
                var hand = root.GetComponentsInChildren<Transform>(true).Single(item => item.name == "hand_l");
                if (!phone) throw new InvalidOperationException(root.name + ": Photo Phone reference is missing.");

                var pivot = hand.Cast<Transform>().FirstOrDefault(item => item.name == "PhoneGripPivot");
                if (!pivot)
                {
                    var phonePosition = phone.transform.position;
                    var phoneRotation = phone.transform.rotation;
                    var phoneScale = phone.transform.localScale;
                    pivot = new GameObject("PhoneGripPivot").transform;
                    pivot.SetParent(hand, false);
                    pivot.SetPositionAndRotation(phonePosition, phoneRotation);
                    pivot.localScale = Vector3.one;
                    phone.transform.SetParent(pivot, true);
                    phone.transform.localPosition = Vector3.zero;
                    phone.transform.localRotation = Quaternion.identity;
                    phone.transform.localScale = phoneScale;
                }
                else if (phone.transform.parent != pivot)
                {
                    phone.transform.SetParent(pivot, true);
                }

                pivot.localPosition = sourcePosition;
                pivot.localRotation = sourceRotation;
                pivot.localScale = sourceScale;
                phone.transform.localPosition = Vector3.zero;
                phone.transform.localRotation = Quaternion.identity;

                PrefabUtility.SaveAsPrefabAsset(root, path);
                report.AppendLine($"PASS {root.name}: hand_l/PhoneGripPivot/Photo Phone " +
                                  $"pivotLocal={pivot.localPosition:F4}, pivotEuler={pivot.localEulerAngles:F2}");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        AssetDatabase.SaveAssets();
        File.WriteAllText(Report, report.ToString());
    }
}
