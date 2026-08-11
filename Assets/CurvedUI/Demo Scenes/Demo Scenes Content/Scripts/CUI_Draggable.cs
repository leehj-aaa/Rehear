using UnityEngine;
using CurvedUI.Core;
using UnityEngine.EventSystems;

namespace CurvedUI
{
    /// <summary>
    /// This component enables accurate object dragging over curved canvas. It supports both mouse and gaze controllers. Add it to your canvas object with image component.
    /// </summary>
    public class CUI_Draggable : MonoBehaviour, IBeginDragHandler, IEndDragHandler, IDragHandler
    {
        private Vector2 _savedVector;
        private bool _isDragged;
        
        public void OnBeginDrag(PointerEventData data)
        {
            Debug.Log("OnBeginDrag");

            RaycastPosition(out var newPos);

            //save distance from click point to object center to allow for precise dragging
            _savedVector = new Vector2((transform as RectTransform).localPosition.x, (transform as RectTransform).localPosition.y) - newPos;

            _isDragged = true;
        }

        public void OnDrag(PointerEventData data)  {  }

        public void OnEndDrag(PointerEventData data)
        {
            Debug.Log("OnEndDrag");

            _isDragged = false;
        }

        private void LateUpdate()
        {
            if (!_isDragged) return;

            Debug.Log("OnDrag");

            //drag the transform along the mouse. We use raycast to determine its position on curved canvas.
            RaycastPosition(out var newPos);

            //add our initial distance from objects center
            (transform as RectTransform).localPosition = newPos + _savedVector;
        }
        
        private void RaycastPosition(out Vector2 newPos)
        {
            if (CurvedUIInputModule.ActiveControlMethod == ControlMethod.MOUSE)
            {
                //position when using mouse
                GetComponentInParent<CurvedUISettings>().RaycastToCanvasSpace(Camera.main.ScreenPointToRay(Input.mousePosition), out newPos);
            }
            else if (CurvedUIInputModule.ActiveControlMethod == ControlMethod.GAZE)
            {
                //position when using gaze - uses the center of the screen as guiding point.
                GetComponentInParent<CurvedUISettings>().RaycastToCanvasSpace(Camera.main.ScreenPointToRay(new Vector2(Screen.width / 2.0f, Screen.height / 2.0f)), out newPos);
            }
            else newPos = Vector2.zero;
        }
    }
}
