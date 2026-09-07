#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace Kamgam.UGUIBlurredBackground
{
    // Create a new type of Settings Asset.
    public class UGUIBlurredBackgroundSettings : ScriptableObject
    {
        public enum ShaderVariant { Performance, Gaussian };

        public const string SettingsFilePath = "Assets/UGUIBlurredBackgroundSettings.asset";

        [SerializeField, Tooltip(_logLevelTooltip)]
        public Logger.LogLevel LogLevel;
        public const string _logLevelTooltip = "Any log above this log level will not be shown. To turn off all logs choose 'NoLogs'";

        public const string _addShaderBeforeBuildTooltip = "Should the blur shader be added to the list of always included shaders before a build is started?\n\n" +
            "Disable only if you do not use any blurred images in your project but you still want to keep the asset around.";
        [Tooltip(_addShaderBeforeBuildTooltip)]
        public bool AddShaderBeforeBuild = true;

        [Tooltip("Here you can specify a render texture for debugging.")]
        public RenderTexture DebugRenderTextureScreen;

        [Tooltip("Here you can specify a render texture for debugging.")]
        public RenderTexture DebugRenderTextureWorld;

        [RuntimeInitializeOnLoadMethod]
        static void bindLoggerLevelToSetting()
        {
            // Notice: This does not yet create a setting instance!
            Logger.OnGetLogLevel = () => GetOrCreateSettings().LogLevel;
        }

        static UGUIBlurredBackgroundSettings cachedSettings;

        public static UGUIBlurredBackgroundSettings GetOrCreateSettings()
        {
            if (cachedSettings == null)
            {
                string typeName = typeof(UGUIBlurredBackgroundSettings).Name;

                cachedSettings = AssetDatabase.LoadAssetAtPath<UGUIBlurredBackgroundSettings>(SettingsFilePath);

                // Still not found? Then search for it.
                if (cachedSettings == null)
                {
                    string[] results = AssetDatabase.FindAssets("t:" + typeName);
                    if (results.Length > 0)
                    {
                        string path = AssetDatabase.GUIDToAssetPath(results[0]);
                        cachedSettings = AssetDatabase.LoadAssetAtPath<UGUIBlurredBackgroundSettings>(path);
                    }
                }

                if (cachedSettings != null)
                {
                    SessionState.EraseBool(typeName + "WaitingForReload");
                }

                // Still not found? Then create settings.
                if (cachedSettings == null)
                {
                    cachedSettings = ScriptableObject.CreateInstance<UGUIBlurredBackgroundSettings>();
                    cachedSettings.LogLevel = Logger.LogLevel.Warning;
                    cachedSettings.AddShaderBeforeBuild = true;

                    AssetDatabase.CreateAsset(cachedSettings, SettingsFilePath);
                    AssetDatabase.SaveAssets();

                    Logger.OnGetLogLevel = () => cachedSettings.LogLevel;
                }
            }

            return cachedSettings;
        }

        internal static SerializedObject GetSerializedSettings()
        {
            return new SerializedObject(GetOrCreateSettings());
        }

        [MenuItem("Tools/UGUI Blurred Background/Settings", priority = 101)]
        public static void OpenSettings()
        {
            var settings = UGUIBlurredBackgroundSettings.GetOrCreateSettings();
            if (settings != null)
            {
                Selection.activeObject = settings;
                EditorGUIUtility.PingObject(settings);
            }
            else
            {
                EditorUtility.DisplayDialog("Error", "UGUI Blurred Background Settings could not be found or created.", "Ok");
            }
        }

        public void Save()
        {
            EditorUtility.SetDirty(this);
#if UNITY_2021_2_OR_NEWER
            AssetDatabase.SaveAssetIfDirty(this);
#else
            AssetDatabase.SaveAssets();
#endif
        }

    }


#if UNITY_EDITOR
    [CustomEditor(typeof(UGUIBlurredBackgroundSettings))]
    public class UGUIBlurredBackgroundSettingsEditor : Editor
    {
        public UGUIBlurredBackgroundSettings settings;

        public void OnEnable()
        {
            settings = target as UGUIBlurredBackgroundSettings;
        }

        public override void OnInspectorGUI()
        {
            EditorGUILayout.LabelField("Version: " + Installer.Version);
            base.OnInspectorGUI();
        }
    }
#endif

    static class UGUIBlurredBackgroundSettingsProvider
    {
        [SettingsProvider]
        public static UnityEditor.SettingsProvider CreateUGUIBlurredBackgroundSettingsProvider()
        {
            var provider = new UnityEditor.SettingsProvider("Project/UGUI Blurred Background", SettingsScope.Project)
            {
                label = "UGUI Blurred Background",
                guiHandler = (searchContext) =>
                {
                    var settings = UGUIBlurredBackgroundSettings.GetSerializedSettings();

                    var style = new GUIStyle(GUI.skin.label);
                    style.wordWrap = true;

                    EditorGUILayout.LabelField("Version: " + Installer.Version);
                    if (drawButton(" Open Manual ", icon: "_Help"))
                    {
                        Installer.OpenManual(); 
                    }

                    var settingsObj = settings.targetObject as UGUIBlurredBackgroundSettings;

                    drawField("LogLevel", "Log Level", UGUIBlurredBackgroundSettings._logLevelTooltip, settings, style);
                    drawField("AddShaderBeforeBuild", "Add Shader Before Build", UGUIBlurredBackgroundSettings._addShaderBeforeBuildTooltip, settings, style);

                    settings.ApplyModifiedProperties();
                },

                // Populate the search keywords to enable smart search filtering and label highlighting.
                keywords = new System.Collections.Generic.HashSet<string>(new[] { "shader", "blur", "blurr", "background", "canvas", "ugui", "image", "rendering" })
            };

            return provider;
        }

        static void drawField(string propertyName, string label, string tooltip, SerializedObject settings, GUIStyle style)
        {
            EditorGUILayout.PropertyField(settings.FindProperty(propertyName), new GUIContent(label));
            if (!string.IsNullOrEmpty(tooltip))
            {
                GUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label(tooltip, style);
                GUILayout.EndVertical();
            }
            GUILayout.Space(10);
        }

        static bool drawButton(string text, string tooltip = null, string icon = null, params GUILayoutOption[] options)
        {
            GUIContent content;

            // icon
            if (!string.IsNullOrEmpty(icon))
                content = EditorGUIUtility.IconContent(icon);
            else
                content = new GUIContent();

            // text
            content.text = text;

            // tooltip
            if (!string.IsNullOrEmpty(tooltip))
                content.tooltip = tooltip;

            return GUILayout.Button(content, options);
        }
    }
}
#endif