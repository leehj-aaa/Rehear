using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using CurvedUI.Core;

namespace CurvedUI
{
    /// <summary>
    /// Raycaster used for interactions with 3D objects.
    /// </summary>
    public class CurvedUIPhysicsRaycaster : BaseRaycaster
    {
        #region VARIABLES AND SETTINGS
        [SerializeField]
        protected int sortOrder = 20;


        //variables
        private RaycastHit _hitInfo;
        private RaycastResult _result;
        #endregion


        #region CONSTRUCTOR
        protected CurvedUIPhysicsRaycaster() { }
        #endregion


        #region RAYCASTING
        public override void Raycast(PointerEventData eventData, List<RaycastResult> resultAppendList)
        {
            //check if we have camera from which to cast a ray
            if (CurvedUIInputModule.Instance == null || CurvedUIInputModule.Instance.EventCamera == null)
                return;

            if (Physics.Raycast(CurvedUIInputModule.Instance.GetEventRay(), out _hitInfo, float.PositiveInfinity, CompoundEventMask))
            {
                if (_hitInfo.collider.GetComponentInParent<CurvedUISettings>()) return; //a canvas is hit - these raycastsResults are handled by CurvedUIRaycasters

                _result = new RaycastResult
                {
                    gameObject = _hitInfo.collider.gameObject,
                    module = this,
                    distance = _hitInfo.distance,
                    index = resultAppendList.Count,
                    worldPosition = _hitInfo.point,
                    worldNormal = _hitInfo.normal,
                };
                resultAppendList.Add(_result);
            }

            //Debug.Log("CUIPhysRaycaster: " + resultAppendList.Count);
        }
        #endregion


        #region SETTERS AND GETTERS
        /// <summary>
        /// This Component's event mask + eventCamera's event mask.
        /// </summary>
        public int CompoundEventMask => (eventCamera != null) ? eventCamera.cullingMask & CurvedUIInputModule.Instance.RaycastLayerMask : -1;
        
        /// <summary>
        /// Camera used to process events
        /// </summary>
        public override Camera eventCamera => CurvedUIInputModule.Instance? CurvedUIInputModule.Instance.EventCamera : null;

        public virtual int Depth => (eventCamera != null) ? (int)eventCamera.depth : 0xFFFFFF;

        public override int sortOrderPriority => sortOrder;
        #endregion
    }
}
