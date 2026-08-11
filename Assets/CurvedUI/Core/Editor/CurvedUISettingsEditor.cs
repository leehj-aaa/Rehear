using UnityEngine;
using System.Linq;
using CurvedUI.Core;
using CurvedUI.Core.ControlMethods.Editor;
using CurvedUI.Core.Integrations;
using CurvedUI.Core.Utilities.Editor;
using UnityEditor;
using UnityEngine.EventSystems;
#if CURVEDUI_TMP || TMP_PRESENT
using TMPro;
#endif

namespace CurvedUI
{
    [CustomEditor(typeof(CurvedUISettings))]
    public class CurvedUISettingsEditor : Editor
    {
#pragma warning disable 414
        private bool _showRemoveCurvedUI;
        private static bool _showAdvancedOptions;
        private bool _isLoadingCustomDefine;
        private static bool _isCuiEventSystemPresent;
        private bool _inPrefabMode;
#pragma warning restore 414


        #region LIFECYCLE
        private void Awake()
        {
            AddCurvedUIComponents();
        }

        private void OnEnable()
        {
            //if we're firing OnEnable, this means any compilation has ended. We're good!
            _isLoadingCustomDefine = false;

            //lets see if we have CurvedUIEventSystem present on the scene.
            _isCuiEventSystemPresent = FindObjectsByType<EventSystem>(FindObjectsSortMode.None)
                .Any(x => x is CurvedUIEventSystem);

            //hacky way to make sure event is connected only once, but it works!
            EditorApplication.hierarchyChanged -= AddCurvedUIComponents;
            EditorApplication.hierarchyChanged -= AddCurvedUIComponents;
            EditorApplication.hierarchyChanged += AddCurvedUIComponents;

            //Warnings-------------------------------------/
            //todo: why is this here? Should be in input module
            if (Application.isPlaying) PrintWarnings();
        }
        #endregion

        private void PrintWarnings()
        {
            var myTarget = (CurvedUISettings)target;

            //Canvas' layer not included in RaycastLayerMask warning
            if (!IsInLayerMask(myTarget.gameObject.layer, CurvedUIInputModule.Instance.RaycastLayerMask) &&
                myTarget.Interactable) {
                Debug.LogError("CURVEDUI: " + WarningLayerNotIncluded, myTarget.gameObject);
            }

            //check if the currently selected control method is enabled in Editor. Otherwise, show error.
            if (CurvedUIInputModule.Instance.GetActiveControlMethodSettings().scriptingDefineSymbol is
                    { Length: > 0 } symbol && DefineSymbolUtility.ContainsDefineSymbol(symbol) == false) {
                Debug.LogError(
                    $"CURVEDUI: Selected control method ({CurvedUIInputModule.ActiveControlMethod}) is not enabled." +
                    $" Enable it on CurvedUISettings component", myTarget.gameObject);
            }
        }


      
        public override void OnInspectorGUI()
        {
            if (target is not CurvedUISettings myTarget) return;

            //initial settings------------------------------------//
            GUI.changed = false;
            EditorGUIUtility.labelWidth = 150;
            _inPrefabMode = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage() != null;
            
            GUILayout.Label($"Version {CurvedUISettings.Version}", EditorStyles.miniLabel);
            
            DrawWarnings(myTarget);
            
            DrawControlMethods();
            
            DrawShapeSettings(myTarget);
            
            DrawAdvancedSettingsFoldout(myTarget);

            
            //set target dirty if sth changed
            if (GUI.changed && myTarget != null) EditorUtility.SetDirty(myTarget);
        }


        #region CUSTOM GUI ELEMENTS
        private void DrawWarnings(CurvedUISettings myTarget)
        {
            //Canvas is on layer that is not part of RaycastLayerMask
            if (CurvedUIInputModule.CanInstanceBeAccessed &&
                !IsInLayerMask(myTarget.gameObject.layer, CurvedUIInputModule.Instance.RaycastLayerMask) &&
                myTarget.Interactable)
            {
                EditorGUILayout.HelpBox(WarningLayerNotIncluded, MessageType.Error);
                GUILayout.Space(30);
            }

            //Improper event system warning
            if (CurvedUIInputModule.CanInstanceBeAccessed && _isCuiEventSystemPresent == false)
            {
                EditorGUILayout.HelpBox(ImproperEventSystemWarning,MessageType.Warning);
                GUILayout.BeginHorizontal();
                GUILayout.Space(150);
                if (GUILayout.Button("Use CurvedUI Event System")) SwapEventSystem();
                GUILayout.EndHorizontal();
                GUILayout.Space(30);
            }
        }
        
        private void Draw180DegWarning(CurvedUISettings myTarget)
        {
            if ((myTarget.Shape != CurvedUISettings.CurvedUIShape.RING && myTarget.Angle.Abs() > 180) ||
                (myTarget.Shape == CurvedUISettings.CurvedUIShape.SPHERE && myTarget.VerticalAngle > 180))
            {
                EditorGUILayout.HelpBox(Deg180Warning, MessageType.Warning);
                GUILayout.Space(30);
            }
        }

        private void DrawShapeSettings(CurvedUISettings myTarget)
        {
            GUILayout.Label("Shape", EditorStyles.boldLabel);
            
            //popup
            myTarget.Shape = (CurvedUISettings.CurvedUIShape)EditorGUILayout.EnumPopup("Canvas Shape", myTarget.Shape);
            
            //additional settings
            switch (myTarget.Shape)
            {
                case CurvedUISettings.CurvedUIShape.CYLINDER:
                {
                    myTarget.Angle = EditorGUILayout.IntSlider("Angle", myTarget.Angle, -360, 360);
                    myTarget.PreserveAspect = EditorGUILayout.Toggle("Preserve Aspect", myTarget.PreserveAspect);
                    break;
                }
                case CurvedUISettings.CurvedUIShape.CYLINDER_VERTICAL:
                {
                    myTarget.Angle = EditorGUILayout.IntSlider("Angle", myTarget.Angle, -360, 360);
                    myTarget.PreserveAspect = EditorGUILayout.Toggle("Preserve Aspect", myTarget.PreserveAspect);
                    break;
                }
                case CurvedUISettings.CurvedUIShape.RING:
                {
                    myTarget.RingExternalDiameter =
                        Mathf.Clamp(EditorGUILayout.IntField("External Diameter", myTarget.RingExternalDiameter), 1,
                            100000);
                    myTarget.Angle = EditorGUILayout.IntSlider("Angle", myTarget.Angle, 0, 360);
                    myTarget.RingFill = EditorGUILayout.Slider("Fill", myTarget.RingFill, 0.0f, 1.0f);
                    myTarget.RingFlipVertical =
                        EditorGUILayout.Toggle("Flip Canvas Vertically", myTarget.RingFlipVertical);
                    break;
                }
                case CurvedUISettings.CurvedUIShape.SPHERE:
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(150);
                    EditorGUILayout.HelpBox(
                        "Sphere shape is more expensive than a Cylinder shape. Keep this in mind when working on mobile VR.",
                        MessageType.Info);
                    GUILayout.EndHorizontal();
                    GUILayout.Space(10);

                    if (myTarget.PreserveAspect)
                    {
                        myTarget.Angle = EditorGUILayout.IntSlider("Angle", myTarget.Angle, -360, 360);
                    }
                    else
                    {
                        myTarget.Angle = EditorGUILayout.IntSlider("Horizontal Angle", myTarget.Angle, 0, 360);
                        myTarget.VerticalAngle =
                            EditorGUILayout.IntSlider("Vertical Angle", myTarget.VerticalAngle, 0, 180);
                    }

                    myTarget.PreserveAspect = EditorGUILayout.Toggle("Preserve Aspect", myTarget.PreserveAspect);
                    break;
                }
            } 
            
            Draw180DegWarning(myTarget);
        }


        private void DrawControlMethods()
        {
            GUILayout.Label("Global Settings", EditorStyles.boldLabel);

            //Do not allow to change Global Settings in Prefab Mode
            //These are stored in CurvedUInputModule
            if (_inPrefabMode || !CurvedUIInputModule.CanInstanceBeAccessed)
            {
                if (Application.isPlaying)
                    EditorGUILayout.HelpBox(ErrorInputModuleMissing, MessageType.Error);
                else
                    EditorGUILayout.HelpBox(WarningInputModuleNotAccessible, MessageType.Warning);
                return;
            }

            //Control Method dropdown--------------------------------//
            CurvedUIInputModule.ActiveControlMethod =
                (ControlMethod)EditorGUILayout.EnumPopup("Control Method", CurvedUIInputModule.ActiveControlMethod);
            GUILayout.BeginHorizontal();
            GUILayout.Space(150);
            GUILayout.BeginVertical();

            //Custom Settings for each Control Method---------------//
            ControlMethodInspector.Create(CurvedUIInputModule.Instance.GetActiveControlMethodSettings()).Draw();

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.Space(20);
        }

        private void DrawAdvancedSettingsFoldout(CurvedUISettings myTarget)
        {
            GUILayout.Space(30);
            if (!_showAdvancedOptions)
            {
                if (GUILayout.Button("Show Advanced Settings"))
                {
                    _showAdvancedOptions = true;
                    _isLoadingCustomDefine = false;
                }
            }
            else
            {
                //hide advances settings button.
                if (GUILayout.Button("Hide Advanced Settings")) _showAdvancedOptions = false;
                
                GUILayout.Space(20);
                
                DrawAdvancedSettings(myTarget);
            } 
            GUILayout.Space(20);
        }
        
        private void DrawAdvancedSettings(CurvedUISettings myTarget)
        {
            //InputModule Options - only if we're not in prefab mode and input module is available.
                if (_inPrefabMode || !CurvedUIInputModule.CanInstanceBeAccessed)
                    EditorGUILayout.HelpBox(WarningInputModuleNotAccessible, MessageType.Warning);
                else
                {
                    CurvedUIInputModule.Instance.RaycastLayerMask = LayerMaskField.DrawField("Raycast Layer Mask",
                        CurvedUIInputModule.Instance.RaycastLayerMask);

                    //pointer override
                    GUILayout.Space(20);
                    CurvedUIInputModule.Instance.PointerTransformOverride = (Transform)EditorGUILayout.ObjectField(
                        "Pointer Override", CurvedUIInputModule.Instance.PointerTransformOverride, typeof(Transform),
                        true);
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(150);
                    GUILayout.Label(
                        "(Optional) If set, its position and forward (blue) direction will be used to point at canvas.",
                        EditorStyles.helpBox);
                    GUILayout.EndHorizontal();
                }


                //quality
                GUILayout.Space(20);
                myTarget.Quality = EditorGUILayout.Slider("Quality", myTarget.Quality, 0.1f, 3.0f);
                GUILayout.BeginHorizontal();
                GUILayout.Space(150);
                GUILayout.Label(
                    "Smoothness of the curve. Bigger values mean more subdivisions. Decrease for better performance. Default 1",
                    EditorStyles.helpBox);
                GUILayout.EndHorizontal();

                //common options
                myTarget.Interactable = EditorGUILayout.Toggle("Interactable", myTarget.Interactable);
                myTarget.BlocksRaycasts = EditorGUILayout.Toggle("Blocks Raycasts", myTarget.BlocksRaycasts);
                if (myTarget.Shape != CurvedUISettings.CurvedUIShape.SPHERE)
                    myTarget.UseMeshCollider = EditorGUILayout.Toggle("Force Mesh Collider Use", myTarget.UseMeshCollider);

                //add components button
                GUILayout.Space(20);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Helper Functions", GUILayout.Width(146));
                GUILayout.BeginVertical();
                //add effect to children button
                if (GUILayout.Button("Add Curved Effect To Children")) AddCurvedUIComponents();
                //remove components button
                GUILayout.BeginHorizontal();
                if (!_showRemoveCurvedUI)
                {
                    if (GUILayout.Button("Remove CurvedUI from Canvas")) _showRemoveCurvedUI = true;
                }
                else
                {
                    if (GUILayout.Button("Remove CurvedUI")) RemoveCurvedUIComponents();
                    if (GUILayout.Button("Cancel")) _showRemoveCurvedUI = false;
                }

                GUILayout.EndHorizontal();
                //remove all defines button
                if (GUILayout.Button(_isLoadingCustomDefine ? "Please wait..." : "Clear Script Defines") &&
                    !_isLoadingCustomDefine)
                {
                    _isLoadingCustomDefine = true; //set a flag so we know sth is happening
                    
                    DefineSymbolUtility.ClearCurvedUIDefineSymbols();
                }
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();


                //documentation link
                GUILayout.Space(20);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Documentation", GUILayout.Width(146));
                if (GUILayout.Button("Open in web browser")) DocsUtility.OpenDocs(DocsUtility.Bookmark.General);  
                GUILayout.EndHorizontal();
        }
        #endregion


        #region HELPER FUNCTIONS
     
        
        // private static List<string> _layers;
        // private static string[] _layerNames;
        //
        // public static LayerMask LayerMaskField(string label, LayerMask selected)
        // {
        //     if (_layers == null)
        //     {
        //         _layers = new List<string>();
        //         _layerNames = new string[4];
        //     }
        //     else
        //     {
        //         _layers.Clear();
        //     }
        //
        //     var emptyLayers = 0;
        //     for (var i = 0; i < 32; i++)
        //     {
        //         var layerName = LayerMask.LayerToName(i);
        //
        //         if (layerName != "")
        //         {
        //             for (; emptyLayers > 0; emptyLayers--) _layers.Add("Layer " + (i - emptyLayers));
        //             _layers.Add(layerName);
        //         }
        //         else
        //         {
        //             emptyLayers++;
        //         }
        //     }
        //
        //     if (_layerNames.Length != _layers.Count)
        //     {
        //         _layerNames = new string[_layers.Count];
        //     }
        //
        //     for (var i = 0; i < _layerNames.Length; i++) _layerNames[i] = _layers[i];
        //
        //     selected.value = EditorGUILayout.MaskField(label, selected.value, _layerNames);
        //
        //     return selected;
        // }

        private bool IsInLayerMask(int layer, LayerMask layermask) => layermask == (layermask | (1 << layer));


        private void SwapEventSystem()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("Cant do this in Play mode!");
                return;
            }

            var system = FindFirstObjectByType<EventSystem>();
            if (system != null && system is not CurvedUIEventSystem)
            {
                //get replacement script
                var tmpGo = new GameObject("tempOBJ");
                var inst = tmpGo.AddComponent<CurvedUIEventSystem>();
                var replacement = MonoScript.FromMonoBehaviour(inst);
                DestroyImmediate(tmpGo);

                //swap serialized reference to CurvedUIEventSystem
                var so = new SerializedObject(system);
                var scriptProperty = so.FindProperty("m_Script");
                so.Update();
                scriptProperty.objectReferenceValue = replacement;
                so.ApplyModifiedProperties();
            }

            _isCuiEventSystemPresent = true;
        }

        /// <summary>
        ///Travel the hierarchy and add CurvedUIVertexEffect to every gameobject that can be bent.
        /// </summary>
        private void AddCurvedUIComponents() => (target as CurvedUISettings)?.AddEffectToChildren();


        /// <summary>
        /// Removes all CurvedUI components from this canvas.
        /// </summary>
        private void RemoveCurvedUIComponents()
        {
            if (target is not CurvedUISettings settings) return;

            //remove events
            EditorApplication.hierarchyChanged -= AddCurvedUIComponents;

            //destroy components
            settings.RemoveComponentsFromChildren(
                typeof(CurvedUITMP),
                typeof(CurvedUITMPSubmesh),
                typeof(CurvedUIVertexEffect),
                typeof(CurvedUIVertexEffect),
                typeof(CurvedUIRaycaster)
            );

            //trigger refresh on all graphics to remove curve effect
            settings.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)
                .ToList().ForEach(x => x.SetAllDirty());

            //destroy target
            DestroyImmediate(settings);
        }

        #endregion

        #region STRINGS

#pragma warning disable 414
        private const string Deg180Warning 
            = "Canvas with angle bigger than 180 degrees will not be interactable. " +
              "\nThis is caused by a Unity Event System requirement. " +
              "Use two canvases facing each other for fully interactive 360 degree UI.";
        
        private const string ImproperEventSystemWarning 
            = "Unity UI may become unresponsive in VR if game window loses focus. " +
              "Use CurvedUIEventSystem instead of standard EventSystem component to solve this issue.";
        
        private const string WarningLayerNotIncluded
            = "This Canvas' layer is not included in the RaycastLayerMask. " +
              "User will not be able to interact with it. Add its layer to RaycastLayerMask below to fix it, or set the " +
              "Interactable property to False to dismiss this message.";

        private const string WarningInputModuleNotAccessible 
            = "Some Global Settings are hidden. These are saved on CurvedUIInputModule and cannot be accessed right now.";

        private const string ErrorInputModuleMissing =
            "CurvedUIInputModule not found on the Scene. Canvas will not be interactable.";
#pragma warning restore 414

        #endregion
    }
}