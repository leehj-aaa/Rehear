using System.Collections.Generic;
using System.Linq;
using CurvedUI.Core.ControlMethods;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CurvedUI.Core
{
[ExecuteInEditMode]
#if ENABLE_INPUT_SYSTEM
public class CurvedUIInputModule : BaseInputModule { 
#else
public class CurvedUIInputModule : StandaloneInputModule {
#endif

    #region SETTINGS ---------------------------------------------------------------------------//
    #pragma warning disable 414, 0649
    //Common
    [SerializeField] private ControlMethod controlMethod;
    [SerializeField] private Hand usedHand = Hand.Right;
    [SerializeField] private Camera mainEventCamera;
    [SerializeField] private LayerMask raycastLayerMask = 1 << 5;
    [SerializeField] private Transform pointerTransformOverride;
    
    //other
    private const bool DisableOtherInputModulesOnStart = true; //default true
    #endregion // end of SETTINGS --------------------------------------------------------------//

    
    #region VARIABLES --------------------------------------------------------------------------//
    //Control methods
    [SerializeField] private MouseControlMethod mouseControlMethod = new();
    [SerializeField] private GazeControlMethod gazeControlMethod = new();
    [SerializeField] private UnityXRControlMethod unityXRControlMethod = new();
    [SerializeField] private MetaXRControlMethod metaXRControlMethod = new();
    [SerializeField] private SteamVRControlMethod steamVRControlMethod = new();
    [SerializeField] private CustomRayControlMethod customRayControlMethod = new();
    
    private Dictionary<ControlMethod, CurvedUIControlMethod> _controlMethodsDict;
    
    private Dictionary<ControlMethod, CurvedUIControlMethod> GetControlMethodsDict()  =>
        _controlMethodsDict ??= new Dictionary<ControlMethod, CurvedUIControlMethod>
        {
            { ControlMethod.MOUSE, mouseControlMethod },
            { ControlMethod.GAZE, gazeControlMethod },
            { ControlMethod.UNITY_XR, unityXRControlMethod },
            { ControlMethod.META_XR, metaXRControlMethod },
            { ControlMethod.STEAM_VR, steamVRControlMethod },
            { ControlMethod.CUSTOM_RAY, customRayControlMethod }
        };
    
    //Support Variables - common
    private static CurvedUIInputModule _instance;
    private GameObject _currentPointedAt;
    private bool _pressedDown; 
    private bool _pressedLastFrame;
    
    //support variables - new Event System 
    private Vector2 _lastEventDataPosition;
    private PointerInputModule.MouseButtonEventData _storedData;
    #pragma warning restore 414, 0649
#endregion // end of VARIABLES ----------------------------------------------------//



#region LIFECYCLE //-------------------------------------------------------------------//

    protected override void Awake()
    {
        if (!Application.isPlaying) return;

        Instance = this;
        
        base.Awake();
        
        EventCamera = mainEventCamera == null ? Camera.main : EventCamera;
    }

    protected override void OnEnable()
    {
        //initialize control methods
        GetActiveControlMethodSettings().Initialize(Application.isPlaying);
        
        base.OnEnable();
    }

    protected void Update()
    {
        //find camera, if we lost it
        if (mainEventCamera == null && Application.isPlaying) EventCamera = Camera.main;

        if (Time.frameCount % 120 == 0) //do it only once every 120 frames
        {
            //check if we don't have extra eventSystem on the scene, as this may mess up interactions.
            if (EventSystem.current != null && EventSystem.current.gameObject != this.gameObject)
                Debug.LogError("CURVEDUI: Second EventSystem component detected. This can make UI unusable." +
                               " Make sure there is only one EventSystem component on the scene." +
                               " Click on this message to have the extra one selected.",
                    EventSystem.current.gameObject);
        }
    }
#endregion // end of LIFECYCLE ------------------------------------------------------------//


#region EVENT PROCESSING
#region EVENT PROCESSING / GENERAL --------------------------------------------------------//

private PointerEventData.FramePressState GetFramePressedState()
{
    if (_pressedDown && !_pressedLastFrame) return PointerEventData.FramePressState.Pressed;
    if (!_pressedDown && _pressedLastFrame) return PointerEventData.FramePressState.Released;
    return PointerEventData.FramePressState.NotChanged;
}
#endregion // end of EVENT PROCESSING / GENERAL -------------------------------------------//

#if ENABLE_INPUT_SYSTEM //-----------------------------------------------------------------//
#region EVENT PROCESSING / NEW INPUT SYSTEM -----------------------------------------------//
    public override void Process()
    {
        //get EventData with the position and state of button in our pointing device
        var eventData = GetEventData(); 
        
        //Ask all RayCasters to cast rays and store the results
        //eventData mouse position will be updated by CurvedUI RayCasters with the position of the hit against canvas.
        eventSystem.RaycastAll(eventData.buttonData, m_RaycastResultCache); 
        eventData.buttonData.pointerCurrentRaycast = FindFirstRaycast(m_RaycastResultCache);
        
        //Calculate position delta - used for sliders, etc.
        if (eventData.buttonData.pointerCurrentRaycast.isValid)
        {
            eventData.buttonData.delta = _lastEventDataPosition - eventData.buttonData.position;
            _lastEventDataPosition = eventData.buttonData.position;
        }
        
        //debug values
        // StringBuilder sb = new StringBuilder();
        // sb.Append(" InputModule After Raycast:");
        // sb.Append(" pos:" + eventData.buttonData.position);
        // sb.Append(" delta:" + eventData.buttonData.delta);
        // sb.Append(" pointer press go: " + eventData.buttonData.pointerPress?.name ?? "none");
        // sb.Append(" raycast results: " + m_RaycastResultCache.Count);
        // Debug.Log(sb.ToString());
        
        //process events on raycast results.
        ProcessMove(eventData.buttonData, eventData.buttonData.pointerCurrentRaycast.gameObject);
        ProcessButton(eventData, eventData.buttonData);
        ProcessDrag(eventData, eventData.buttonData);
        ProcessScroll(eventData, eventData.buttonData);
        
        //save some values for later
        _pressedLastFrame = _pressedDown;
    }
    
    private PointerInputModule.MouseButtonEventData GetEventData()
    {
        //get button state and ray from the controller
        if (GetActiveControlMethodSettings().Process(usedHand, mainEventCamera) is { } args)
        {
            //CustomRay = args.Ray;//TODO: how this gets to ray? Do we even need this? This should not be stored in here
            _pressedDown = args.ButtonState;
        }
        
        //Update stored MouseButtonEventData
        if (_storedData == null)
        {
            //create new, if missing
            _storedData = new PointerInputModule.MouseButtonEventData {
                buttonData = new PointerEventData(EventSystem.current) {
                    button = PointerEventData.InputButton.Left
                }
            };
        }
        _storedData.buttonData.Reset();  //clear "used" flag
        _storedData.buttonData.useDragThreshold = true;
        _storedData.buttonData.position = MousePosition; //update mousepos
        _storedData.buttonState = GetFramePressedState(); //and button state we got from ControlMethod
        
        //save initial press position if that's the first frame of interaction
        if (_storedData.buttonState == PointerEventData.FramePressState.Pressed)
            _storedData.buttonData.pressPosition = _storedData.buttonData.position;

        //save current raycast target
        _currentPointedAt = _storedData.buttonData.pointerCurrentRaycast.gameObject;
        
        return _storedData;
    }
    
    private void ProcessMove(PointerEventData eventData, GameObject currentRaycastTarget)
    {
        // If we lost our target, send Exit events to all hovered objects and clear the list.
        if (currentRaycastTarget == null || eventData.pointerEnter == null)
        {
            foreach (var t in eventData.hovered)
                ExecuteEvents.Execute(t, eventData, ExecuteEvents.pointerExitHandler);

            eventData.hovered.Clear();

            if (currentRaycastTarget == null)
            {
                eventData.pointerEnter = null;
                return;
            }
        }

        if (eventData.pointerEnter == currentRaycastTarget && currentRaycastTarget)
            return;
        //------------------------------//
        
        
        // Send events to every object up to a common parent of past and current RaycastTarget--//
        var commonRoot = FindCommonRoot(eventData.pointerEnter, currentRaycastTarget)?.transform;
        
        if (eventData.pointerEnter != null)
        {
            for (var current = eventData.pointerEnter.transform; current != null && current != commonRoot; current = current.parent)
            {
                ExecuteEvents.Execute(current.gameObject, eventData, ExecuteEvents.pointerExitHandler);
                eventData.hovered.Remove(current.gameObject);
            }
        }

        eventData.pointerEnter = currentRaycastTarget;
        if (currentRaycastTarget != null)
        {
            for (var current = currentRaycastTarget.transform;
                 current != null && current != commonRoot;
                 current = current.parent)
            {
                ExecuteEvents.Execute(current.gameObject, eventData, ExecuteEvents.pointerEnterHandler);
                eventData.hovered.Add(current.gameObject);
            }
        }
        //----------------------------------------//
    }

    private void ProcessButton(PointerInputModule.MouseButtonEventData button, PointerEventData eventData)
    {
        var currentRaycastGo = eventData.pointerCurrentRaycast.gameObject;
        
        if (button.buttonState == PointerEventData.FramePressState.Pressed)
        {
            eventData.delta = Vector2.zero;
            eventData.dragging = false;
            eventData.pressPosition = eventData.position;
            eventData.pointerPressRaycast = eventData.pointerCurrentRaycast;
            eventData.eligibleForClick = true;
            
            //selectHandler
            var selectHandler = ExecuteEvents.GetEventHandler<ISelectHandler>(currentRaycastGo);
            if (selectHandler != null && selectHandler != eventSystem.currentSelectedGameObject)
                eventSystem.SetSelectedGameObject(null, eventData);

            //pointerDownHandler
            var newPressed = ExecuteEvents.ExecuteHierarchy(currentRaycastGo, eventData, ExecuteEvents.pointerDownHandler);

            //clickHandler
            if (newPressed == eventData.lastPress && (Time.unscaledTime - eventData.clickTime) < 0.28f)
                eventData.clickCount += 1;
            else
                eventData.clickCount = 1;
            eventData.clickTime = Time.unscaledTime;

            if (newPressed == null) 
                newPressed = ExecuteEvents.GetEventHandler<IPointerClickHandler>(currentRaycastGo);

            eventData.pointerPress = newPressed;
            eventData.rawPointerPress = currentRaycastGo;

            //dragHandler
            eventData.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(currentRaycastGo);
            if (eventData.pointerDrag != null)
                ExecuteEvents.Execute(eventData.pointerDrag, eventData, ExecuteEvents.initializePotentialDrag);
        }

        // FramePressState.Released
        if (button.buttonState == PointerEventData.FramePressState.Released)
        {
            ExecuteEvents.Execute(eventData.pointerPress, eventData, ExecuteEvents.pointerUpHandler);

            var pointerUpHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(currentRaycastGo);

            if (eventData.pointerPress == pointerUpHandler && eventData.eligibleForClick)
                ExecuteEvents.Execute(eventData.pointerPress, eventData, ExecuteEvents.pointerClickHandler);
            else if (eventData.dragging && eventData.pointerDrag != null)
                ExecuteEvents.ExecuteHierarchy(currentRaycastGo, eventData, ExecuteEvents.dropHandler);

            eventData.eligibleForClick = false;
            eventData.pointerPress = null;
            eventData.rawPointerPress = null;

            if (eventData.dragging && eventData.pointerDrag != null)
                ExecuteEvents.Execute(eventData.pointerDrag, eventData, ExecuteEvents.endDragHandler);

            eventData.dragging = false;
            eventData.pointerDrag = null;
        }
    }

    private void ProcessDrag(PointerInputModule.MouseButtonEventData button, PointerEventData eventData)
    {
        if (eventData.pointerDrag == null || !eventData.IsPointerMoving()) return;

        if (!eventData.dragging)
        {
            if (!eventData.useDragThreshold || (eventData.pressPosition - eventData.position).sqrMagnitude >=
                (double)eventSystem.pixelDragThreshold * eventSystem.pixelDragThreshold)
            {
                ExecuteEvents.Execute(eventData.pointerDrag, eventData, ExecuteEvents.beginDragHandler);
                eventData.dragging = true;
            }
        }

        if (eventData.dragging)
        {
            // pointerUpHandler on Objects we moved away from
            if (eventData.pointerPress != eventData.pointerDrag)
            {
                ExecuteEvents.Execute(eventData.pointerPress, eventData, ExecuteEvents.pointerUpHandler);

                eventData.eligibleForClick = false;
                eventData.pointerPress = null;
                eventData.rawPointerPress = null;
            }
            
            //dragHandler on currently dragged
            ExecuteEvents.Execute(eventData.pointerDrag, eventData, ExecuteEvents.dragHandler);
        }
    }
    
    private static void ProcessScroll(PointerInputModule.MouseButtonEventData button, PointerEventData eventData)
    {
        //any scroll this frame?
        if (Mathf.Approximately(eventData.scrollDelta.sqrMagnitude, 0.0f)) return;
        
        var eventHandler = ExecuteEvents.GetEventHandler<IScrollHandler>(eventData.pointerEnter);
        ExecuteEvents.ExecuteHierarchy(eventHandler, eventData, ExecuteEvents.scrollHandler);
    }
    

#endregion //end of EVENT PROCESSING / NEW INPUT SYSTEM ----------------------------------------------//   
#else // end of CURVEDUI_NEW_INPUT -------------------------------------------------------------------//
#region EVENT PROCESSING / LEGACY INPUT SYSTEM -------------------------------------------------------//

    /// <summary>
    /// Process() is called by UI system to process events 
    /// </summary>
    public override void Process()
    {
        if (GetActiveControlMethodSettings().Process(usedHand, mainEventCamera) is { } args)
        {
            //CustomRay = args.Ray;
            _pressedDown = args.ButtonState;
        }
        
        ProcessMouseEvent();
        
        //save button pressed state for reference in next frame
        _pressedLastFrame = _pressedDown;
    }
    
    protected override MouseState GetMousePointerEventData(int id)
    {
        var ret = base.GetMousePointerEventData(id);
        
        _currentPointedAt = ret.GetButtonState(PointerEventData.InputButton.Left)
            .eventData.buttonData.pointerCurrentRaycast.gameObject;

        ret.SetButtonState(PointerEventData.InputButton.Left, GetFramePressedState(), ret.GetButtonState(PointerEventData.InputButton.Left).eventData.buttonData);

        return ret;
    }
    
    private bool ShouldStartDrag(Vector2 pressPos, Vector2 currentPos, float threshold, bool useDragThreshold)
    {
        if (!useDragThreshold)return true;

        return (pressPos - currentPos).sqrMagnitude >= threshold * threshold;
    }
    
    protected override void ProcessDrag(PointerEventData pointerEvent)
    {
        var shouldStartDrag = ShouldStartDrag(pointerEvent.pressPosition, pointerEvent.position, eventSystem.pixelDragThreshold,
            pointerEvent.useDragThreshold);

        if (!pointerEvent.dragging && shouldStartDrag)
        {
            ExecuteEvents.Execute(pointerEvent.pointerDrag, pointerEvent, ExecuteEvents.beginDragHandler);
            pointerEvent.dragging = true;
        }

        if (pointerEvent.dragging)
        {
            if (pointerEvent.pointerPress != pointerEvent.pointerDrag)
            {
                ExecuteEvents.Execute(pointerEvent.pointerPress, pointerEvent, ExecuteEvents.pointerUpHandler);

                pointerEvent.eligibleForClick = false;
                pointerEvent.pointerPress = null;
                pointerEvent.rawPointerPress = null;
            }
            ExecuteEvents.Execute(pointerEvent.pointerDrag, pointerEvent, ExecuteEvents.dragHandler);
        }
    }
#endregion  // end of EVENT PROCESSING / LEGACY INPUT SYSTEM --------------------------------------//
#endif // end of CURVEDUI_NEW_INPUT ppppppp--------------------------------------------------------//
#endregion // end of EVENT PROCESSING -------------------------------------------------------------//
    



    #region HELPER FUNCTIONS ----------------------------------------------------------------------------//

    private static T EnableInputModule<T>() where T : BaseInputModule
    {
        if (FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include) is not { } eventSystem)
        {
            Debug.LogError("CurvedUI: Your EventSystem component is missing from the scene! Unity Canvas will not track interactions without it.");
            return null;
        }
        
        T ret = null;
        foreach (var module in eventSystem.GetComponents<BaseInputModule>())
        {
            if (module is T inputModule) {
                inputModule.enabled = true;
                ret = inputModule;
            } else if (DisableOtherInputModulesOnStart) 
                module.enabled = false;
        }
        
        if(ret == null)  ret = eventSystem.gameObject.AddComponent<T>();

        return ret;
    }
    #endregion  // end of HELPER FUNCTIONS -------------------------------------------------------------//



    

    #region SETTERS AND GETTERS - GENERAL ------------------------------------------------------------//
    public static CurvedUIInputModule Instance
    {
        get
        {
            if (_instance == null) _instance = EnableInputModule<CurvedUIInputModule>();
            return _instance;
        }
        private set => _instance = value;
    }

    public static bool CanInstanceBeAccessed => Instance != null;
    

    
    /// <summary>
    /// Current controller mode. Decides how user can interact with the canvas. 
    /// </summary>
    public static ControlMethod ActiveControlMethod
    {
        get => Instance.controlMethod;
        set => Instance.controlMethod = value;
    }
    
    public CurvedUIControlMethod GetActiveControlMethodSettings() 
        => GetControlMethodsDict().GetValueOrDefault(controlMethod, mouseControlMethod);
    
    public T GetControlMethodSettings<T>() where T : CurvedUIControlMethod 
        => GetControlMethodsDict().Values.OfType<T>().FirstOrDefault(_ => true);
    

    /// <summary>
    /// LayerMask used by Raycaster classes to perform a Physics.Raycast() in order to find
    /// point where user is aiming at the canvas.
    /// </summary>
    public LayerMask RaycastLayerMask
    {
        get => raycastLayerMask;
        set => raycastLayerMask = value;
    }

    /// <summary>
    /// Which hand can be used to interact with canvas. Left, Right or Both. Default Right.
    /// Used in control methods that differentiate hands (STEAMVR, OCULUSVR)
    /// </summary>
    public Hand UsedHand
    {
        get => usedHand;
        set => usedHand = value;
    }

    /// <summary>
    /// Gameobject of the handheld controller used for interactions - Oculus Touch, GearVR remote etc. 
    /// If ControllerTransformOverride is set, that transform will be returned instead.
    /// Used in STEAMVR, OCULUSVR, UNITY_XR control methods.
    /// </summary>
    public Transform PointerTransform => pointerTransformOverride != null 
        ? pointerTransformOverride 
        : GetActiveControlMethodSettings().GetPointerTransform(usedHand);

    /// <summary>
    /// If not null, this transform will be used as the Pointer.
    /// Its position will be used as PointingOrigin and its forward (blue) direction as PointingDirection.
    /// </summary>
    public Transform PointerTransformOverride {
        get => _instance.pointerTransformOverride;
        set => _instance.pointerTransformOverride = value;
    }
    
    /// <summary>
    /// Direction where the handheld controller points. Forward (blue) direction of the controller transform.
    /// If ControllerTransformOverride is set, its forward direction will be returned instead.
    /// </summary>
    public Vector3 PointerDirection => PointerTransform.forward;
    
    /// <summary>
    /// World Space position where the pointing ray starts. Usually the location of controller transform.
    /// If ControllerTransformOverride is set, its position will be returned instead.
    /// </summary>
    public Vector3 PointerOrigin => PointerTransform.position;
    
    /// <summary>
    /// GameObject we're currently pointing at. Updated every frame.
    /// </summary>
    public GameObject CurrentPointedAt => _currentPointedAt; //tODO: not working correctly

    public Camera EventCamera {
        get => mainEventCamera;
        private set
        {
            mainEventCamera = value;
            
            if (mainEventCamera != null) mainEventCamera.AddComponentIfMissing<CurvedUIPhysicsRaycaster>();
        }
    }

    /// <summary>
    /// Get a ray that represents where user is aiming the Pointer (Usually the hand-held controller).
    /// Depends on EventCamera and current Control Method. Used to RayCast against canvas collider.
    /// </summary>
    /// <returns></returns>
    public Ray GetEventRay(Camera eventCam = null) {

        if(pointerTransformOverride) return new Ray(PointerOrigin, PointerDirection);
        
        if (eventCam == null) eventCam = mainEventCamera;

        return GetActiveControlMethodSettings().GetEventRay(usedHand, eventCam);
    }

    /// <summary>
    /// What is the mouse position on screen now? Returns value from legacy or new Input System.
    /// Note: Unity reports wrong on-screen mouse position if a VR headset is connected.
    /// </summary>
    public static Vector2 MousePosition => MouseControlMethod.MousePosition;

    /// <summary>
    /// Is left mouse button pressed now? Returns value from legacy or new Input System.
    /// </summary>
    public static bool LeftMouseButton => MouseControlMethod.MouseLeftButtonIsPressed;
    
 #endregion // end of SETTERS AND GETTERS - GENERAL region ---------------------------------------------//






    #region SETTERS AND GETTERS - CUSTOM RAY
    /// <summary>
    /// When in CUSTOM_RAY controller mode, Canvas Raycaster will use this worldspace Ray to determine which Canvas objects are being selected.
    /// </summary>
    public static Ray CustomRay
    {
        get => Instance.customRayControlMethod.CustomControllerRay;
        set => Instance.customRayControlMethod.CustomControllerRay = value;
    }

    /// <summary>
    /// Tell CurvedUI if controller button is pressed when in CUSTOM_RAY controller mode. Input module will use this to interact with canvas.
    /// </summary>
    public static bool CustomRayButtonState
    {
        get => Instance.customRayControlMethod.CustomControllerButtonState;
        set => Instance.customRayControlMethod.CustomControllerButtonState = value;
    }
    #endregion
}

    #region ENUMS
    public enum ControlMethod
    {
        MOUSE = 0,
        GAZE = 1,
        CUSTOM_RAY = 3,
        META_XR = 5,
        STEAM_VR = 8, //SDK version 2.0 or later
        UNITY_XR = 9,
    }

    public enum Hand
    {
        Any = 0,
        Right = 1,
        Left = 2,
    }
    #endregion // ENUMS
}