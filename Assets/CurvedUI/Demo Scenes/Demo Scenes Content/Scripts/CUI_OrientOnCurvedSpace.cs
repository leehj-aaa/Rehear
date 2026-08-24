using UnityEngine;

namespace CurvedUI
{
    public class CUI_OrientOnCurvedSpace : MonoBehaviour
    {
        public CurvedUISettings mySettings;
        
        private void Awake()
        {
            mySettings = GetComponentInParent<CurvedUISettings>();
        }

        private void Update()
        {
            var positionInCanvasSpace = mySettings.transform.worldToLocalMatrix.MultiplyPoint3x4(transform.parent.position);
            transform.position = mySettings.CanvasToCurvedCanvas(positionInCanvasSpace);
            transform.rotation = Quaternion.LookRotation(mySettings.CanvasToCurvedCanvasNormal(transform.parent.localPosition), transform.parent.up);
        }
    }
}
