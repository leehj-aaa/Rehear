using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using CurvedUI.Core;
using CurvedUI.Core.ControlMethods;
using Selectable = UnityEngine.UI.Selectable;

namespace CurvedUI
{
    public class CurvedUIRaycaster : GraphicRaycaster
    {
        //Settings--------------------------------------//
        // CurvedUIRaycaster must modify the position of the eventData to make it valid for the curved canvas. 
        // It can either create a copy, or override the original. The copy will only be used for this canvas, in this frame. 
        // The overridden original will be carried to other canvases and next frames.
        //
        // Set this to TRUE if this raycaster should override the original eventData.
        // Overriding eventData allows canvas to use 1:1 scrolling. Scroll rects and sliders behave as they should on a curved surface and follow the pointer.
        // This however breaks the interactions with flat canvases in the same scene as original eventData will not be correct for them any more. 
        //
        // Setting this to FALSE will create a copy of the eventData for each canvas.
        // Flat canvases on the same scene will work fine, but scroll rects on curved canvases will move faster / slower than the pointer.
        // May break dragging and scrolling as there will be no past eventdata to calculate delta position from.
        // default true.
        private readonly bool _overrideEventData = true;
        
        [SerializeField] private bool showDebug;

        
        //Variables --------------------------------------//
        private Canvas _myCanvas;
        private CurvedUISettings _mySettings;
        private Vector3 _cyllinderMidPoint;
        private List<GameObject> _objectsUnderPointer = new();
        private Vector2 _lastCanvasPos = Vector2.zero;
        private GameObject _colliderContainer;
        private PointerEventData _lastFrameEventData;
        private PointerEventData _curEventData;
        private PointerEventData _eventDataToUse;
        private Ray _cachedRay;
        private Graphic _gph;
        private GazeControlMethod _cachedGazeSettings;

        //gaze click
        private readonly List<GameObject> _selectablesUnderGaze = new();
        private readonly List<GameObject> _selectablesUnderGazeLastFrame = new();
        private float _objectsUnderGazeLastChangeTime;
        private bool _gazeClickExecuted;
        private bool _pointingAtCanvas;
        private bool _pointingAtCanvasLastFrame;

        
#region LIFECYCLE
        protected override void Awake()
        {
            base.Awake();
            _mySettings = GetComponentInParent<CurvedUISettings>();
            _cachedGazeSettings = CurvedUIInputModule.Instance.GetControlMethodSettings<GazeControlMethod>();
 
       
            if (_mySettings == null) return;
            _myCanvas = _mySettings.GetComponent<Canvas>();

            _cyllinderMidPoint = new Vector3(0, 0, -_mySettings.GetCyllinderRadiusInCanvasSpace());

            //this must be set to false to make sure proper interactions.
            //Otherwise, Unity may ignore Selectables on edges of heavily curved canvas.
            ignoreReversedGraphics = false;
        }

        protected override void Start()
        {
            if (_mySettings == null) return;

            CheckEventCamera();

            CreateCollider();
        }

        protected virtual void Update()
        {
            if (_mySettings == null) return;
            
            //Gaze click process.
            if (CurvedUIInputModule.ActiveControlMethod == ControlMethod.GAZE && _cachedGazeSettings.GazeUseTimedClick)
            {
                if (_pointingAtCanvas)
                {
                    //first frame gaze enters canvas. Make sure we dont click immidiately upon entering canvas
                    if (!_pointingAtCanvasLastFrame)
                        ResetGazeTimedClick();

                    ProcessGazeTimedClick();

                    //save current selectablesUnderGaze
                    _selectablesUnderGazeLastFrame.Clear();
                    _selectablesUnderGazeLastFrame.AddRange(_selectablesUnderGaze);

                    //find selectables we're currently pointing at in objects under pointer
                    _selectablesUnderGaze.Clear();
                    _selectablesUnderGaze.AddRange(_objectsUnderPointer);
                    _selectablesUnderGaze.RemoveAll(obj =>
                        obj.GetComponent<Selectable>() == null || obj.GetComponent<Selectable>().interactable == false);

                    //Animate progress bar
                    if (gazeProgressImage)
                    {
                        if (gazeProgressImage.type != Image.Type.Filled) gazeProgressImage.type = Image.Type.Filled;

                        gazeProgressImage.fillAmount =
                            (Time.time - _objectsUnderGazeLastChangeTime).RemapAndClamp(_cachedGazeSettings.GazeClickTimerDelay, _cachedGazeSettings.GazeClickTimer + _cachedGazeSettings.GazeClickTimerDelay, 0, 1);
                    }
                }
                else if (!_pointingAtCanvas && _pointingAtCanvasLastFrame) //first frame after gaze pointer leaves this canvas.
                { 
                    //not poiting at canvas, reset the timer.
                    ResetGazeTimedClick();

                    if (gazeProgressImage)  gazeProgressImage.fillAmount = 0;
                }
            }

            _pointingAtCanvasLastFrame = _pointingAtCanvas;

            //reset this variable. It will be checked again during next Raycast method run.
            _pointingAtCanvas = false;
        }
#endregion


#region GAZE INTERACTION

private void ProcessGazeTimedClick()
        {
            //debug
            //string str = " Object under pointer: ";
            //foreach (GameObject go in objectsUnderPointer) str += go.name + ", ";
            //Debug.Log(str);

            //two lists are not the same - selected objects changed
            if (_selectablesUnderGazeLastFrame.Count == 0 || _selectablesUnderGazeLastFrame.Count != _selectablesUnderGaze.Count)
            {
                ResetGazeTimedClick();
                return;
            }

            //Check if objects changed since last frame
            for (var i = 0; i < _selectablesUnderGazeLastFrame.Count && i < _selectablesUnderGaze.Count; i++)
            {
                if (_selectablesUnderGazeLastFrame[i].GetInstanceID() != _selectablesUnderGaze[i].GetInstanceID())
                {
                    ResetGazeTimedClick();
                    return;
                }
            }

            //Check if time is done and we havent executed the click yet
            if (!_gazeClickExecuted && Time.time > _objectsUnderGazeLastChangeTime + _cachedGazeSettings.GazeClickTimer + _cachedGazeSettings.GazeClickTimerDelay)
            {
                Click();
                _gazeClickExecuted = true;
            }
        }

        private void ResetGazeTimedClick()
        {
            _objectsUnderGazeLastChangeTime = Time.time;
            _gazeClickExecuted = false;
        }
#endregion




#region PHYSICS RAYCASTING
        public override void Raycast(PointerEventData eventData, List<RaycastResult> resultAppendList)
        {
            // StringBuilder sb = new StringBuilder();
            // sb.Append(" Raycast with eventdata:");
            // sb.Append(" pos:" + eventData.position);
            // sb.Append(" delta:" + eventData.delta);
            // sb.Append(" pointer press go: " + eventData.pointerPress?.name ?? "none");
            // Debug.Log(sb.ToString());

            if (_mySettings == null)
            {
                base.Raycast(eventData, resultAppendList);
                return;
            }

            if (!_mySettings.Interactable)
                return;

            //check if we have a world camera to process events by
            if (!CheckEventCamera()) {
                return;
            }
            
            //get a ray to raycast with depending on the control method
            _cachedRay = CurvedUIInputModule.Instance.GetEventRay(_myCanvas.worldCamera);

            //special case for GAZE and WORLD MOUSE
            if (CurvedUIInputModule.ActiveControlMethod == ControlMethod.GAZE)
                UpdateSelectedObjects(eventData);
       
            //Create a copy of the eventData to be used by this canvas. 
            if (_curEventData == null)
                _curEventData = new PointerEventData(EventSystem.current);

            if (!_overrideEventData)
            {
                _curEventData.pointerEnter = eventData.pointerEnter;
                _curEventData.rawPointerPress = eventData.rawPointerPress;
                _curEventData.pointerDrag = eventData.pointerDrag;
                _curEventData.pointerCurrentRaycast = eventData.pointerCurrentRaycast;
                _curEventData.pointerPressRaycast = eventData.pointerPressRaycast;
                _curEventData.hovered.Clear();
                _curEventData.hovered.AddRange(eventData.hovered);
                _curEventData.eligibleForClick = eventData.eligibleForClick;
                _curEventData.pointerId = eventData.pointerId;
                _curEventData.position = eventData.position;
                _curEventData.delta = eventData.delta;
                _curEventData.pressPosition = eventData.pressPosition;
                _curEventData.clickTime = eventData.clickTime;
                _curEventData.clickCount = eventData.clickCount;
                _curEventData.scrollDelta = eventData.scrollDelta;
                _curEventData.useDragThreshold = eventData.useDragThreshold;
                _curEventData.dragging = eventData.dragging;
                _curEventData.button = eventData.button;
            }



            if (_mySettings.Angle != 0 && _mySettings.enabled)
            { // use custom raycasting only if Curved effect is enabled


                //Getting remappedPosition on the curved canvas ------------------------------//
                //This will be later passed to GraphicRaycaster so it can discover interactions as usual.
                //If we did not hit the curved canvas, return - no interactions are possible
                //Physical raycast to find interaction point
                var remappedPosition = eventData.position;
                switch (_mySettings.Shape)
                {
                    case CurvedUISettings.CurvedUIShape.CYLINDER:
                    {
                        if (!RaycastToCyllinderCanvas(_cachedRay, out remappedPosition, false)) return;
                        break;
                    }
                    case CurvedUISettings.CurvedUIShape.CYLINDER_VERTICAL:
                    {
                        if (!RaycastToCyllinderVerticalCanvas(_cachedRay, out remappedPosition, false)) return;
                        break;
                    }
                    case CurvedUISettings.CurvedUIShape.RING:
                    {
                        if (!RaycastToRingCanvas(_cachedRay, out remappedPosition, false)) return;
                        break;
                    }
                    case CurvedUISettings.CurvedUIShape.SPHERE:
                    {
                        if (!RaycastToSphereCanvas(_cachedRay, out remappedPosition, false)) return;
                        break;
                    }
                }

                //if we got here, it means user is pointing at this canvas.
                _pointingAtCanvas = true;

                //Creating eventData for canvas Raycasting -------------------//
                //Which eventData were going to use?
                _eventDataToUse = _overrideEventData ? eventData : _curEventData;

                // Swap event data pressPosition to our remapped pos if this is the frame of the press
                if (_eventDataToUse.pressPosition == _eventDataToUse.position)
                    _eventDataToUse.pressPosition = remappedPosition;

                // Swap event data position to our remapped pos
                _eventDataToUse.position = remappedPosition;
            }


            //store objects under pointer so they can quickly retrieved if needed by other scripts
            _objectsUnderPointer = eventData.hovered;

            _lastFrameEventData = eventData;

            // Use base class raycast method to finish the raycast if we hit anything
            // FlatRaycast(overrideEventData ? eventData : curEventData, resultAppendList);

            base.Raycast(_overrideEventData ? eventData : _curEventData, resultAppendList);
        }



        public virtual bool RaycastToCyllinderCanvas(Ray ray3D, out Vector2 oCanvasPos, bool outputInCanvasSpace = false)
        {
            if (showDebug)
            {
                Debug.DrawLine(ray3D.origin, ray3D.GetPoint(1000), Color.red);
            }

            var hit = new RaycastHit();
            if (Physics.Raycast(ray3D, out hit, float.PositiveInfinity, GetRaycastLayerMask()))
            {
                //find if we hit this canvas - this needs to be uncommented
                if (_overrideEventData && hit.collider.gameObject != this.gameObject && (_colliderContainer == null || hit.collider.transform.parent != _colliderContainer.transform))
                {
                    oCanvasPos = Vector2.zero;
                    return false;
                }

                //direction from the cyllinder center to the hit point
                var localHitPoint = _myCanvas.transform.worldToLocalMatrix.MultiplyPoint3x4(hit.point);
                var directionFromCyllinderCenter = (localHitPoint - _cyllinderMidPoint).normalized;

                //angle between middle of the projected canvas and hit point direction
                var angle = -AngleSigned(directionFromCyllinderCenter.ModifyY(0), _mySettings.Angle < 0 ? Vector3.back : Vector3.forward, Vector3.up);

                //convert angle to canvas coordinates
                var canvasSize = _myCanvas.GetComponent<RectTransform>().rect.size;

                //map the intersection point to 2d point in canvas space
                Vector2 pointOnCanvas = new Vector3(0, 0, 0);
                pointOnCanvas.x = angle.Remap(-_mySettings.Angle / 2.0f, _mySettings.Angle / 2.0f, -canvasSize.x / 2.0f, canvasSize.x / 2.0f);
                pointOnCanvas.y = localHitPoint.y;


                if (outputInCanvasSpace)
                    oCanvasPos = pointOnCanvas;
                else //convert the result to screen point in camera. This will be later used by raycaster and world camera to determine what we're pointing at
                    oCanvasPos = _myCanvas.worldCamera.WorldToScreenPoint(_myCanvas.transform.localToWorldMatrix.MultiplyPoint3x4(pointOnCanvas));

                if (showDebug)
                {
                    Debug.DrawLine(hit.point, hit.point.ModifyY(hit.point.y + 10), Color.green);
                    Debug.DrawLine(hit.point, _myCanvas.transform.localToWorldMatrix.MultiplyPoint3x4(_cyllinderMidPoint), Color.yellow);
                }

                return true;
            }

            oCanvasPos = Vector2.zero;
            return false;
        }


        public virtual bool RaycastToCyllinderVerticalCanvas(Ray ray3D, out Vector2 oCanvasPos, bool outputInCanvasSpace = false)
        {

            if (showDebug)
            {
                Debug.DrawLine(ray3D.origin, ray3D.GetPoint(1000), Color.red);
            }

            var hit = new RaycastHit();
            if (Physics.Raycast(ray3D, out hit, float.PositiveInfinity, GetRaycastLayerMask()))
            {
                //find if we hit this canvas
                if (_overrideEventData && hit.collider.gameObject != this.gameObject && (_colliderContainer == null || hit.collider.transform.parent != _colliderContainer.transform))
                {
                    oCanvasPos = Vector2.zero;
                    return false;
                }

                //direction from the cyllinder center to the hit point
                var localHitPoint = _myCanvas.transform.worldToLocalMatrix.MultiplyPoint3x4(hit.point);
                var directionFromCyllinderCenter = (localHitPoint - _cyllinderMidPoint).normalized;

                //angle between middle of the projected canvas and hit point direction
                var angle = -AngleSigned(directionFromCyllinderCenter.ModifyX(0), _mySettings.Angle < 0 ? Vector3.back : Vector3.forward, Vector3.left);

                //convert angle to canvas coordinates
                var canvasSize = _myCanvas.GetComponent<RectTransform>().rect.size;

                //map the intersection point to 2d point in canvas space
                Vector2 pointOnCanvas = new Vector3(0, 0, 0);
                pointOnCanvas.y = angle.Remap(-_mySettings.Angle / 2.0f, _mySettings.Angle / 2.0f, -canvasSize.y / 2.0f, canvasSize.y / 2.0f);
                pointOnCanvas.x = localHitPoint.x;


                if (outputInCanvasSpace)
                    oCanvasPos = pointOnCanvas;
                else //convert the result to screen point in camera. This will be later used by raycaster and world camera to determine what we're pointing at
                    oCanvasPos = _myCanvas.worldCamera.WorldToScreenPoint(_myCanvas.transform.localToWorldMatrix.MultiplyPoint3x4(pointOnCanvas));

                if (showDebug)
                {
                    Debug.DrawLine(hit.point, hit.point.ModifyY(hit.point.y + 10), Color.green);
                    Debug.DrawLine(hit.point, _myCanvas.transform.localToWorldMatrix.MultiplyPoint3x4(_cyllinderMidPoint), Color.yellow);
                }

                return true;
            }

            oCanvasPos = Vector2.zero;
            return false;
        }

        public virtual bool RaycastToRingCanvas(Ray ray3D, out Vector2 oCanvasPos, bool outputInCanvasSpace = false)
        {
			var myLayerMask = GetRaycastLayerMask();

            var hit = new RaycastHit();
            if (Physics.Raycast(ray3D, out hit, float.PositiveInfinity, myLayerMask))
            {
                //find if we hit this canvas
                if (_overrideEventData && hit.collider.gameObject != this.gameObject && (_colliderContainer == null || hit.collider.transform.parent != _colliderContainer.transform))
                {
                    oCanvasPos = Vector2.zero;
                    return false;
                }


                //local hit point on canvas and a direction from center
                var localHitPoint = _myCanvas.transform.worldToLocalMatrix.MultiplyPoint3x4(hit.point);
                var directionFromRingCenter = localHitPoint.ModifyZ(0).normalized;
                var canvasSize = _myCanvas.GetComponent<RectTransform>().rect.size;


                //angle between middle of the projected canvas and hit point direction from center
                var angle = -AngleSigned(directionFromRingCenter.ModifyZ(0), Vector3.up, Vector3.back);

                //map the intersection point to 2d point in canvas space
                var pointOnCanvas = new Vector2(0, 0);

                if (showDebug)
                    Debug.Log("angle: " + angle);


                //map x coordinate based on angle between vector up and direction to hitpoint
                if (angle < 0)
                {
                    pointOnCanvas.x = angle.Remap(0, -_mySettings.Angle, -canvasSize.x / 2.0f, canvasSize.x / 2.0f);
                }
                else {
                    pointOnCanvas.x = angle.Remap(360, 360 - _mySettings.Angle, -canvasSize.x / 2.0f, canvasSize.x / 2.0f);
                }


                //map y coordinate based on hitpoint distance from the center and external diameter
                pointOnCanvas.y = localHitPoint.magnitude.Remap(_mySettings.RingExternalDiameter * 0.5f * (1 - _mySettings.RingFill), _mySettings.RingExternalDiameter * 0.5f,
                    -canvasSize.y * 0.5f * (_mySettings.RingFlipVertical ? -1 : 1), canvasSize.y * 0.5f * (_mySettings.RingFlipVertical ? -1 : 1));


                if (outputInCanvasSpace)
                    oCanvasPos = pointOnCanvas;
                else //convert the result to screen point in camera. This will be later used by raycaster and world camera to determine what we're pointing at
                    oCanvasPos = _myCanvas.worldCamera.WorldToScreenPoint(_myCanvas.transform.localToWorldMatrix.MultiplyPoint3x4(pointOnCanvas));
                return true;
            }

            oCanvasPos = Vector2.zero;
            return false;
        }


        public virtual bool RaycastToSphereCanvas(Ray ray3D, out Vector2 oCanvasPos, bool outputInCanvasSpace = false)
        {

            var hit = new RaycastHit();
            if (Physics.Raycast(ray3D, out hit, float.PositiveInfinity, GetRaycastLayerMask()))
            {
                //find if we hit this canvas
                if (_overrideEventData && hit.collider.gameObject != this.gameObject && (_colliderContainer == null || hit.collider.transform.parent != _colliderContainer.transform))
                {
                    oCanvasPos = Vector2.zero;
                    return false;
                }

                var canvasSize = _myCanvas.GetComponent<RectTransform>().rect.size;
                var radius = (_mySettings.PreserveAspect ? _mySettings.GetCyllinderRadiusInCanvasSpace() : canvasSize.x / 2.0f);

                //local hit point on canvas, direction from its center and a vector perpendicular to direction, so we can use it to calculate its angle in both planes.
                var localHitPoint = _myCanvas.transform.worldToLocalMatrix.MultiplyPoint3x4(hit.point);
                var sphereCenter = new Vector3(0, 0, _mySettings.PreserveAspect ? -radius : 0);
                var directionFromSphereCenter = (localHitPoint - sphereCenter).normalized;
                var xzPlanePerpendicular = Vector3.Cross(directionFromSphereCenter, directionFromSphereCenter.ModifyY(0)).normalized * (directionFromSphereCenter.y < 0 ? 1 : -1);

                //horizontal and vertical angle between middle of the sphere and the hit point.
                //We do some fancy checks to determine vectors we compare them to,
                //to make sure they are negative on the left and bottom side of the canvas
                var hAngle = -AngleSigned(directionFromSphereCenter.ModifyY(0), (_mySettings.Angle > 0 ? Vector3.forward : Vector3.back), (_mySettings.Angle > 0 ? Vector3.up : Vector3.down));
                var vAngle = -AngleSigned(directionFromSphereCenter, directionFromSphereCenter.ModifyY(0), xzPlanePerpendicular);

                //find the size of the canvas expressed as measure of the arc it occupies on the sphere
                var hAngularSize = Mathf.Abs(_mySettings.Angle) * 0.5f;
                var vAngularSize = Mathf.Abs(_mySettings.PreserveAspect ? hAngularSize * canvasSize.y / canvasSize.x : _mySettings.VerticalAngle * 0.5f);

                //map the intersection point to 2d point in canvas space
                var pointOnCanvas = new Vector2(hAngle.Remap(-hAngularSize, hAngularSize, -canvasSize.x * 0.5f, canvasSize.x * 0.5f),
                                                    vAngle.Remap(-vAngularSize, vAngularSize, -canvasSize.y * 0.5f, canvasSize.y * 0.5f));

                if (showDebug)
                {
                    Debug.Log("h: " + hAngle + " / v: " + vAngle + " poc: " + pointOnCanvas);
                    Debug.DrawRay(_myCanvas.transform.localToWorldMatrix.MultiplyPoint3x4(sphereCenter), _myCanvas.transform.localToWorldMatrix.MultiplyVector(directionFromSphereCenter) * Mathf.Abs(radius), Color.red);
                    Debug.DrawRay(_myCanvas.transform.localToWorldMatrix.MultiplyPoint3x4(sphereCenter), _myCanvas.transform.localToWorldMatrix.MultiplyVector(xzPlanePerpendicular) * 300, Color.magenta);
                }

                if (outputInCanvasSpace)
                    oCanvasPos = pointOnCanvas;
                else // convert the result to screen point in camera.This will be later used by raycaster and world camera to determine what we're pointing at
                    oCanvasPos = _myCanvas.worldCamera.WorldToScreenPoint(_myCanvas.transform.localToWorldMatrix.MultiplyPoint3x4(pointOnCanvas));

                return true;
            }

            oCanvasPos = Vector2.zero;
            return false;
        }
#endregion



#region COLLIDER MANAGEMENT

        /// <summary>
        /// Creates a mesh collider for curved canvas based on current angle and curve segments.
        /// </summary>
        /// <returns>The collider.</returns>
        protected void CreateCollider()
        {

            //remove all colliders on this object
            var cols = new List<Collider>();
            cols.AddRange(this.GetComponents<Collider>());
            for (var i = 0; i < cols.Count; i++)
            {
                Destroy(cols[i]);
            }

            if (!_mySettings.BlocksRaycasts) return; //null;

            if (_mySettings.Shape == CurvedUISettings.CurvedUIShape.SPHERE && !_mySettings.PreserveAspect && _mySettings.VerticalAngle == 0) return;// null;

            //create a collider based on mapping type
            switch (_mySettings.Shape)
            {

                case CurvedUISettings.CurvedUIShape.CYLINDER:
                {
                    //creating a convex (lower performance - many parts) collider for when we have a rigidbody attached
                    if (_mySettings.UseMeshCollider == false || GetComponent<Rigidbody>() != null || GetComponentInParent<Rigidbody>() != null)
                    {
                        if (_colliderContainer != null)
                            GameObject.Destroy(_colliderContainer);

                        _colliderContainer = CreateConvexCyllinderCollider();
                    }
                    else // create a faster single mesh collier when possible
                    {
                        SetupMeshColliderUsingMesh(CreateCyllinderColliderMesh());
                    }
                    return;
                }
                case CurvedUISettings.CurvedUIShape.CYLINDER_VERTICAL:
                {
                    //creating a convex (lower performance - many parts) collider for when we have a rigidbody attached
                    if (_mySettings.UseMeshCollider == false || GetComponent<Rigidbody>() != null || GetComponentInParent<Rigidbody>() != null)
                    {
                        if (_colliderContainer != null)
                            GameObject.Destroy(_colliderContainer);

                        _colliderContainer = CreateConvexCyllinderCollider(true);
                    }
                    else // create a faster single mesh collier when possible
                    {
                        SetupMeshColliderUsingMesh(CreateCyllinderColliderMesh(true));
                    }
                    return;
                }
                case CurvedUISettings.CurvedUIShape.RING:
                {
                    var col = this.gameObject.AddComponent<BoxCollider>();
                    col.size = new Vector3(_mySettings.RingExternalDiameter, _mySettings.RingExternalDiameter, 1.0f);
                    return;
                }
                case CurvedUISettings.CurvedUIShape.SPHERE:
                {
                    //rigidbody in parent?
                    if (GetComponent<Rigidbody>() != null || GetComponentInParent<Rigidbody>() != null)
                        Debug.LogWarning("CurvedUI: Sphere shape canvases as children of rigidbodies do not support user input. Switch to Cyllinder shape or remove the rigidbody from parent.", this.gameObject);

                    SetupMeshColliderUsingMesh(CreateSphereColliderMesh());
                    return;
                }
                default: return;
            }

        }

        /// <summary>
        /// Adds necessary components and fills them with given mesh data.
        /// </summary>
        private void SetupMeshColliderUsingMesh(Mesh mesh)
        {
            var mf = this.AddComponentIfMissing<MeshFilter>();
            var mc = this.gameObject.AddComponent<MeshCollider>();
            mf.mesh = mesh;
            mc.sharedMesh = mesh;
        }
        
        private GameObject CreateConvexCyllinderCollider(bool vertical = false)
        {
            var go = new GameObject("_CurvedUIColliders");
            go.layer = this.gameObject.layer;
            go.transform.SetParent(this.transform);
            go.transform.ResetTransform();

            var meshie = new Mesh();
            var vertices = new Vector3[4];
            (_myCanvas.transform as RectTransform).GetWorldCorners(vertices);
            meshie.vertices = vertices;
            
            //rearrange them to be in an easy to interpolate order and convert to canvas local spce
            var worldToLocalMatrix = _myCanvas.transform.worldToLocalMatrix;
            if (vertical)
            {
                vertices[0] = worldToLocalMatrix.MultiplyPoint3x4(meshie.vertices[1]);
                vertices[1] = worldToLocalMatrix.MultiplyPoint3x4(meshie.vertices[2]);
                vertices[2] = worldToLocalMatrix.MultiplyPoint3x4(meshie.vertices[0]);
                vertices[3] = worldToLocalMatrix.MultiplyPoint3x4(meshie.vertices[3]);
            }
            else
            {
                vertices[0] = worldToLocalMatrix.MultiplyPoint3x4(meshie.vertices[1]);
                vertices[1] = worldToLocalMatrix.MultiplyPoint3x4(meshie.vertices[0]);
                vertices[2] = worldToLocalMatrix.MultiplyPoint3x4(meshie.vertices[2]);
                vertices[3] = worldToLocalMatrix.MultiplyPoint3x4(meshie.vertices[3]);
            }

            meshie.vertices = vertices;

            //create a new array of vertices, subdivided as needed
            var verts = new List<Vector3>();
            var vertsCount = Mathf.Max(8, Mathf.RoundToInt(_mySettings.BaseCircleSegments * Mathf.Abs(_mySettings.Angle) / 360.0f));

            for (var i = 0; i < vertsCount; i++)
            {
                verts.Add(Vector3.Lerp(meshie.vertices[0], meshie.vertices[2], (i * 1.0f) / (vertsCount - 1)));
            }

            //curve the verts in canvas local space
            if (_mySettings.Angle != 0)
            {
                var canvasRect = _myCanvas.GetComponent<RectTransform>().rect;
                var radius = _mySettings.GetCyllinderRadiusInCanvasSpace();

                for (var i = 0; i < verts.Count; i++)
                {
                    var newpos = verts[i];
                    if (vertical)
                    {
                        var theta = (verts[i].y / canvasRect.size.y) * _mySettings.Angle * Mathf.Deg2Rad;
                        newpos.y = Mathf.Sin(theta) * radius;
                        newpos.z += Mathf.Cos(theta) * radius - radius;
                        verts[i] = newpos;
                    }
                    else
                    {
                        var theta = (verts[i].x / canvasRect.size.x) * _mySettings.Angle * Mathf.Deg2Rad;
                        newpos.x = Mathf.Sin(theta) * radius;
                        newpos.z += Mathf.Cos(theta) * radius - radius;
                        verts[i] = newpos;
                    }
                }
            }


            //create our box colliders and arrange them in a nice cyllinder
            var boxDepth = _mySettings.GetTesslationSize(false).x / 10;
            for (var i = 0; i < verts.Count - 1; i++)
            {
                var newBox = new GameObject("Box collider");
                newBox.layer = this.gameObject.layer;
                newBox.transform.SetParent(go.transform);
                newBox.transform.ResetTransform();
                newBox.AddComponent<BoxCollider>();

                if (vertical)
                {
                    newBox.transform.localPosition = new Vector3(0, (verts[i + 1].y + verts[i].y) * 0.5f, (verts[i + 1].z + verts[i].z) * 0.5f);
                    newBox.transform.localScale = new Vector3(boxDepth, Vector3.Distance(vertices[0], vertices[1]), Vector3.Distance(verts[i + 1], verts[i]));
                    newBox.transform.localRotation = Quaternion.LookRotation((verts[i + 1] - verts[i]), vertices[0] - vertices[1]);
                }
                else
                {
                    newBox.transform.localPosition = new Vector3((verts[i + 1].x + verts[i].x) * 0.5f, 0, (verts[i + 1].z + verts[i].z) * 0.5f);
                    newBox.transform.localScale = new Vector3(boxDepth, Vector3.Distance(vertices[0], vertices[1]), Vector3.Distance(verts[i + 1], verts[i]));
                    newBox.transform.localRotation = Quaternion.LookRotation((verts[i + 1] - verts[i]), vertices[0] - vertices[1]);
                }

            }

            return go;

        }

        private Mesh CreateCyllinderColliderMesh(bool vertical = false)
        {

            var meshie = new Mesh();
            var vertices = new Vector3[4];
            (_myCanvas.transform as RectTransform).GetWorldCorners(vertices);
            meshie.vertices = vertices;

            //rearrange them to be in an easy to interpolate order and convert to canvas local spce
            var worldToLocalMatrix = _myCanvas.transform.worldToLocalMatrix;
            if (vertical)
            {
                
                vertices[0] = worldToLocalMatrix.MultiplyPoint3x4(meshie.vertices[1]);
                vertices[1] = worldToLocalMatrix.MultiplyPoint3x4(meshie.vertices[2]);
                vertices[2] = worldToLocalMatrix.MultiplyPoint3x4(meshie.vertices[0]);
                vertices[3] = worldToLocalMatrix.MultiplyPoint3x4(meshie.vertices[3]);
            }
            else
            {
                vertices[0] = worldToLocalMatrix.MultiplyPoint3x4(meshie.vertices[1]);
                vertices[1] = worldToLocalMatrix.MultiplyPoint3x4(meshie.vertices[0]);
                vertices[2] = worldToLocalMatrix.MultiplyPoint3x4(meshie.vertices[2]);
                vertices[3] = worldToLocalMatrix.MultiplyPoint3x4(meshie.vertices[3]);
            }

            meshie.vertices = vertices;

            //create a new array of vertices, subdivided as needed
            var verts = new List<Vector3>();
            var vertsCount = Mathf.Max(8, Mathf.RoundToInt(_mySettings.BaseCircleSegments * Mathf.Abs(_mySettings.Angle) / 360.0f));

            for (var i = 0; i < vertsCount; i++)
            {
                verts.Add(Vector3.Lerp(meshie.vertices[0], meshie.vertices[2], (i * 1.0f) / (vertsCount - 1)));
                verts.Add(Vector3.Lerp(meshie.vertices[1], meshie.vertices[3], (i * 1.0f) / (vertsCount - 1)));
            }


            //curve the verts in canvas local space
            if (_mySettings.Angle != 0)
            {
                var canvasRect = _myCanvas.GetComponent<RectTransform>().rect;
                var radius = _mySettings.GetCyllinderRadiusInCanvasSpace();

                for (var i = 0; i < verts.Count; i++)
                {

                    var newPos = verts[i];
                    if (vertical)
                    {
                        var theta = (verts[i].y / canvasRect.size.y) * _mySettings.Angle * Mathf.Deg2Rad;
                        newPos.y = Mathf.Sin(theta) * radius;
                        newPos.z += Mathf.Cos(theta) * radius - radius;
                        verts[i] = newPos;
                    }
                    else
                    {
                        var theta = (verts[i].x / canvasRect.size.x) * _mySettings.Angle * Mathf.Deg2Rad;
                        newPos.x = Mathf.Sin(theta) * radius;
                        newPos.z += Mathf.Cos(theta) * radius - radius;
                        verts[i] = newPos;
                    }


                }
            }

            meshie.vertices = verts.ToArray();

            //create triangles drom verts
            var tris = new List<int>();
            for (var i = 0; i < verts.Count / 2 - 1; i++)
            {
                if (vertical)
                {
                    //forward tris
                    tris.Add(i * 2 + 0);
                    tris.Add(i * 2 + 1);
                    tris.Add(i * 2 + 2);

                    tris.Add(i * 2 + 1);
                    tris.Add(i * 2 + 3);
                    tris.Add(i * 2 + 2);
                }
                else {
                    //forward tris
                    tris.Add(i * 2 + 2);
                    tris.Add(i * 2 + 1);
                    tris.Add(i * 2 + 0);

                    tris.Add(i * 2 + 2);
                    tris.Add(i * 2 + 3);
                    tris.Add(i * 2 + 1);
                }
            }
            meshie.triangles = tris.ToArray();

            return meshie;
        }

        private Mesh CreateSphereColliderMesh()
        {

            var meshie = new Mesh();

            var corners = new Vector3[4];
            (_myCanvas.transform as RectTransform).GetWorldCorners(corners);

            var verts = new List<Vector3>(corners);
            for (var i = 0; i < verts.Count; i++)
            {
                verts[i] = _mySettings.transform.worldToLocalMatrix.MultiplyPoint3x4(verts[i]);
            }

            if (_mySettings.Angle != 0)
            {
                // Tesselate quads and apply transformation
                var startingVertexCount = verts.Count;
                for (var i = 0; i < startingVertexCount; i += 4)
                    ModifyQuad(verts, i, _mySettings.GetTesslationSize(false));

                // Remove old quads
                verts.RemoveRange(0, startingVertexCount);

                //curve verts
                float vangle = _mySettings.VerticalAngle;
                float cylinderAngle = _mySettings.Angle;
                var canvasSize = (_myCanvas.transform as RectTransform).rect.size;
                var radius = _mySettings.GetCyllinderRadiusInCanvasSpace();

                //caluclate vertical angle for aspect - consistent mapping
                if (_mySettings.PreserveAspect)
                {
                    vangle = _mySettings.Angle * (canvasSize.y / canvasSize.x);
                }
                else {//if we're not going for constant aspect, set the width of the sphere to equal width of the original canvas
                    radius = canvasSize.x / 2.0f;
                }

                //curve the vertices 
                for (var i = 0; i < verts.Count; i++)
                {

                    var theta = (verts[i].x / canvasSize.x).Remap(-0.5f, 0.5f, (180 - cylinderAngle) / 2.0f - 90, 180 - (180 - cylinderAngle) / 2.0f - 90);
                    theta *= Mathf.Deg2Rad;
                    var gamma = (verts[i].y / canvasSize.y).Remap(-0.5f, 0.5f, (180 - vangle) / 2.0f, 180 - (180 - vangle) / 2.0f);
                    gamma *= Mathf.Deg2Rad;

                    verts[i] = new Vector3(Mathf.Sin(gamma) * Mathf.Sin(theta) * radius,
                        -radius * Mathf.Cos(gamma),
                        Mathf.Sin(gamma) * Mathf.Cos(theta) * radius + (_mySettings.PreserveAspect ? -radius : 0));
                }
            }
            meshie.vertices = verts.ToArray();

            //create triangles from verts
            var tris = new List<int>();
            for (var i = 0; i < verts.Count; i += 4)
            {
                tris.Add(i + 0);
                tris.Add(i + 1);
                tris.Add(i + 2);

                tris.Add(i + 3);
                tris.Add(i + 0);
                tris.Add(i + 2);
            }


            meshie.triangles = tris.ToArray();
            return meshie;
        }


        #endregion


#region SUPPORT FUNCTIONS

private bool IsInLayerMask(int layer, LayerMask layermask)
        {
            return layermask == (layermask | (1 << layer));
        }

        private LayerMask GetRaycastLayerMask() {
            return CurvedUIInputModule.Instance.RaycastLayerMask;
        }

        private Image gazeProgressImage => _cachedGazeSettings.GazeTimedClickProgressImage;

        /// <summary>
        /// Determine the signed angle between two vectors, with normal 'n'
        /// as the rotation axis.
        /// </summary>
        private float AngleSigned(Vector3 v1, Vector3 v2, Vector3 n)
        {
            return Mathf.Atan2(
                Vector3.Dot(n, Vector3.Cross(v1, v2)),
                Vector3.Dot(v1, v2)) * Mathf.Rad2Deg;
        }

        private bool ShouldStartDrag(Vector2 pressPos, Vector2 currentPos, float threshold, bool useDragThreshold)
        {
            if (!useDragThreshold)
                return true;

            return (pressPos - currentPos).sqrMagnitude >= threshold * threshold;
        }

        protected virtual void ProcessMove(PointerEventData pointerEvent)
        {
            var targetGo = pointerEvent.pointerCurrentRaycast.gameObject;
            HandlePointerExitAndEnter(pointerEvent, targetGo);
        }

       
        protected void UpdateSelectedObjects(PointerEventData eventData)
        {

            //deselect last object if we moved beyond it
            var selectedStillUnderGaze = false;
            foreach (var go in eventData.hovered)
            {
                if (go == eventData.selectedObject)
                {
                    selectedStillUnderGaze = true;
                    break;
                }
            }
            if (!selectedStillUnderGaze) eventData.selectedObject = null;


            //find new object to select in hovered objects
            foreach (var go in eventData.hovered)
            {
                if (go == null) continue;

                //go through only go that can be selected and are drawn by the canvas
                _gph = go.GetComponent<Graphic>();

                if (go.GetComponent<Selectable>() != null && _gph != null && _gph.depth != -1 && _gph.raycastTarget)
                {
                    if (eventData.selectedObject != go)
                        eventData.selectedObject = go;
                    break;
                }
            }


            if (_mySettings.ControlMethod == ControlMethod.GAZE)
            {
                //Test for selected object being dragged and initialize dragging, if needed.
                //We do this here to trick unity's StandAloneInputModule into thinking we used a touch or mouse to do it.
                if (eventData.IsPointerMoving() && eventData.pointerDrag != null
                    && !eventData.dragging
                    && ShouldStartDrag(eventData.pressPosition, eventData.position, EventSystem.current.pixelDragThreshold, eventData.useDragThreshold))
                {
                    ExecuteEvents.Execute(eventData.pointerDrag, eventData, ExecuteEvents.beginDragHandler);
                    eventData.dragging = true;
                }
            }
        }

        // walk up the tree till a common root between the last entered and the current entered is foung
        // send exit events up to (but not inluding) the common root. Then send enter events up to
        // (but not including the common root).
        public void HandlePointerExitAndEnter(PointerEventData currentPointerData, GameObject newEnterTarget)
        {
            // if we have no target / pointerEnter has been deleted
            // just send exit events to anything we are tracking
            // then exit
            if (newEnterTarget == null || currentPointerData.pointerEnter == null)
            {
                for (var i = 0; i < currentPointerData.hovered.Count; ++i)
                    ExecuteEvents.Execute(currentPointerData.hovered[i], currentPointerData, ExecuteEvents.pointerExitHandler);

                currentPointerData.hovered.Clear();

                if (newEnterTarget == null)
                {
                    currentPointerData.pointerEnter = newEnterTarget;
                    return;
                }
            }

            // if we have not changed hover target
            if (currentPointerData.pointerEnter == newEnterTarget && newEnterTarget)
                return;

            var commonRoot = FindCommonRoot(currentPointerData.pointerEnter, newEnterTarget);

            // and we already an entered object from last time
            if (currentPointerData.pointerEnter != null)
            {
                // send exit handler call to all elements in the chain
                // until we reach the new target, or null!
                var t = currentPointerData.pointerEnter.transform;

                while (t != null)
                {
                    // if we reach the common root break out!
                    if (commonRoot != null && commonRoot.transform == t)
                        break;

                    ExecuteEvents.Execute(t.gameObject, currentPointerData, ExecuteEvents.pointerExitHandler);
                    currentPointerData.hovered.Remove(t.gameObject);
                    t = t.parent;
                }
            }

            // now issue the enter call up to but not including the common root
            currentPointerData.pointerEnter = newEnterTarget;
            if (newEnterTarget != null)
            {
                var t = newEnterTarget.transform;

                while (t != null && t.gameObject != commonRoot)
                {
                    ExecuteEvents.Execute(t.gameObject, currentPointerData, ExecuteEvents.pointerEnterHandler);
                    currentPointerData.hovered.Add(t.gameObject);
                    t = t.parent;
                }
            }
        }

        private static GameObject FindCommonRoot(GameObject g1, GameObject g2)
        {
            if (g1 == null || g2 == null)
                return null;

            var t1 = g1.transform;
            while (t1 != null)
            {
                var t2 = g2.transform;
                while (t2 != null)
                {
                    if (t1 == t2)
                        return t1.gameObject;
                    t2 = t2.parent;
                }
                t1 = t1.parent;
            }
            return null;
        }

        /// <summary>
        /// REturns a screen point under which a ray intersects the curved canvas in its event camera view
        /// </summary>
        /// <returns><c>true</c>, if screen space point by ray was gotten, <c>false</c> otherwise.</returns>
        /// <param name="ray">Ray.</param>
        /// <param name="o_positionOnCanvas">O position on canvas.</param>
        private bool GetScreenSpacePointByRay(Ray ray, out Vector2 oPositionOnCanvas)
        {
            switch (_mySettings.Shape)
            {
                case CurvedUISettings.CurvedUIShape.CYLINDER:
                    return RaycastToCyllinderCanvas(ray, out oPositionOnCanvas, false);
                case CurvedUISettings.CurvedUIShape.CYLINDER_VERTICAL:
                    return RaycastToCyllinderVerticalCanvas(ray, out oPositionOnCanvas, false);
                case CurvedUISettings.CurvedUIShape.RING:
                    return RaycastToRingCanvas(ray, out oPositionOnCanvas, false);
                case CurvedUISettings.CurvedUIShape.SPHERE:
                    return RaycastToSphereCanvas(ray, out oPositionOnCanvas, false);
                default:
                {
                    oPositionOnCanvas = Vector2.zero;
                    return false;
                }
            }

        }

        private bool CheckEventCamera()
        {
            //check if we have a world camera to process events by
            if (_myCanvas.worldCamera == null)
            {
                //try assigning from InputModule
                if (CurvedUIInputModule.Instance && CurvedUIInputModule.Instance.EventCamera)
                    _myCanvas.worldCamera = CurvedUIInputModule.Instance.EventCamera;
                else if (Camera.main) //assign Main Camera
                    _myCanvas.worldCamera = Camera.main;
            }
            
            if(_myCanvas.worldCamera == null)
                Debug.LogWarning($"CurvedUI: No WorldCamera assigned to " +
                                 $"Canvas '{gameObject.name}; to use for event processing!", _myCanvas.gameObject);
            else if (_myCanvas.worldCamera.gameObject.activeInHierarchy == false)
                Debug.LogWarning($"CurvedUI: Camera {nameof(_myCanvas.worldCamera.name)} assigned as " +
                                 $"this canvas's WorldCamera is disabled.", _myCanvas.gameObject);

            return _myCanvas.worldCamera != null;
        }
        #endregion





        #region PUBLIC
        /// <summary>
        /// Returns true if user's pointer is currently pointing inside this canvas.
        /// </summary>
        public bool pointingAtCanvas => _pointingAtCanvas;

        public void RebuildCollider()
        {
            _cyllinderMidPoint = new Vector3(0, 0, -_mySettings.GetCyllinderRadiusInCanvasSpace());
            CreateCollider();
        }

        /// <summary>
        /// Returns all objects currently under the pointer
        /// </summary>
        /// <returns>The objects under pointer.</returns>
        public List<GameObject> GetObjectsUnderPointer()
        {
            if (_objectsUnderPointer == null) _objectsUnderPointer = new List<GameObject>();
            return _objectsUnderPointer;
        }


        /// <summary>
        /// Returns all the canvas objects that are visible under given Screen Position of EventCamera
        /// </summary>
        public List<GameObject> GetObjectsUnderScreenPos(Vector2 screenPos, Camera eventCamera = null)
        {
            if (eventCamera == null)
                eventCamera = _myCanvas.worldCamera;

            return GetObjectsHitByRay(eventCamera.ScreenPointToRay(screenPos));
        }

        /// <summary>
        /// Returns all the canvas objects that are intersected by given ray
        /// </summary>
        /// <returns>The objects hit by ray.</returns>
        /// <param name="ray">Ray.</param>
        public List<GameObject> GetObjectsHitByRay(Ray ray)
        {
            var results = new List<GameObject>();

            Vector2 pointerPosition;

            //ray outside the canvas, return null
            if (!GetScreenSpacePointByRay(ray, out pointerPosition))
                return results;

            //lets find the graphics under ray!
            var sSortedGraphics = new List<Graphic>();
            var foundGraphics = GraphicRegistry.GetGraphicsForCanvas(_myCanvas);
            for (var i = 0; i < foundGraphics.Count; ++i)
            {
                var graphic = foundGraphics[i];

                // -1 means it hasn't been processed by the canvas, which means it isn't actually drawn
                if (graphic.depth == -1 || !graphic.raycastTarget)
                    continue;

                if (!RectTransformUtility.RectangleContainsScreenPoint(graphic.rectTransform, pointerPosition, eventCamera))
                    continue;

                if (graphic.Raycast(pointerPosition, eventCamera))
                    sSortedGraphics.Add(graphic);

            }

            sSortedGraphics.Sort((g1, g2) => g2.depth.CompareTo(g1.depth));
            for (var i = 0; i < sSortedGraphics.Count; ++i)
                results.Add(sSortedGraphics[i].gameObject);

            sSortedGraphics.Clear();

            return results;
        }

        /// <summary>
        /// Sends OnClick event to every Button under pointer.
        /// </summary>
        public void Click()
        {
            for (var i = 0; i < GetObjectsUnderPointer().Count; i++)
            {
                if (GetObjectsUnderPointer()[i].GetComponent<Slider>())//slider requires a diffrent way to click.
                {
                    //Click calculated via RectTransformUtility - that's the way Slider class does it under the hood.
                    var mSlider = GetObjectsUnderPointer()[i].GetComponent<Slider>();
                    Vector2 clickPoint;
                    RectTransformUtility.ScreenPointToLocalPointInRectangle((mSlider.handleRect.parent as RectTransform), _lastFrameEventData.position, _myCanvas.worldCamera, out clickPoint);
                    clickPoint -= mSlider.handleRect.parent.GetComponent<RectTransform>().rect.position;
                    if (mSlider.direction == Slider.Direction.LeftToRight || mSlider.direction == Slider.Direction.RightToLeft)
                        mSlider.normalizedValue = clickPoint.x / (mSlider.handleRect.parent as RectTransform).rect.width;
                    else
                        mSlider.normalizedValue = clickPoint.y / (mSlider.handleRect.parent as RectTransform).rect.height;


                    //prompt update from fill Graphic to avoid flicker
                    GetObjectsUnderPointer()[i].GetComponent<Slider>().fillRect.GetComponent<Graphic>().SetAllDirty();


                    //log
                    //Debug.Log("x: " + clickPoint.x + ", width:" + (m_slider.transform as RectTransform).rect.width + ", value:" + clickPoint.x / (m_slider.transform as RectTransform).rect.width);
                }
                else
                {
                    ExecuteEvents.Execute(GetObjectsUnderPointer()[i], _lastFrameEventData, ExecuteEvents.pointerDownHandler);
                    ExecuteEvents.Execute(GetObjectsUnderPointer()[i], _lastFrameEventData, ExecuteEvents.pointerClickHandler);
                    ExecuteEvents.Execute(GetObjectsUnderPointer()[i], _lastFrameEventData, ExecuteEvents.pointerUpHandler);
                }
            }
        }
#endregion



#region TESSELATION

private void ModifyQuad(List<Vector3> verts, int vertexIndex, Vector2 requiredSize)
        {

            // Read the existing quad vertices
            var quad = new List<Vector3>();
            for (var i = 0; i < 4; i++)
                quad.Add(verts[vertexIndex + i]);

            // horizotal and vertical directions of a quad. We're going to tesselate parallel to these.
            var horizontalDir = quad[2] - quad[1];
            var verticalDir = quad[1] - quad[0];

            // Find how many quads we need to create
            var horizontalQuads = Mathf.CeilToInt(horizontalDir.magnitude * (1.0f / Mathf.Max(1.0f, requiredSize.x)));
            var verticalQuads = Mathf.CeilToInt(verticalDir.magnitude * (1.0f / Mathf.Max(1.0f, requiredSize.y)));

            // Create the quads!
            var yStart = 0.0f;
            for (var y = 0; y < verticalQuads; ++y)
            {

                var yEnd = (y + 1.0f) / verticalQuads;
                var xStart = 0.0f;

                for (var x = 0; x < horizontalQuads; ++x)
                {
                    var xEnd = (x + 1.0f) / horizontalQuads;

                    //Add new quads to list
                    verts.Add(TesselateQuad(quad, xStart, yStart));
                    verts.Add(TesselateQuad(quad, xStart, yEnd));
                    verts.Add(TesselateQuad(quad, xEnd, yEnd));
                    verts.Add(TesselateQuad(quad, xEnd, yStart));

                    //begin the next quad where we ened this one
                    xStart = xEnd;
                }
                //begin the next row where we ended this one
                yStart = yEnd;
            }
        }


        private Vector3 TesselateQuad(List<Vector3> quad, float x, float y)
        {

            var ret = Vector3.zero;

            //1. calculate weighting factors
            var weights = new List<float>(){
                (1-x) * (1-y),
                (1-x) * y,
                x * y,
                x * (1-y),
            };

            //2. interpolate pos using weighting factors
            for (var i = 0; i < 4; i++)
                ret += quad[i] * weights[i];
            return ret;
        }

#endregion

    }
}
