using UnityEngine;
using UnityEngine.UI;

namespace CurvedUI
{
    public class CUI_PickImageFromSet : MonoBehaviour
    {
        private static CUI_PickImageFromSet _picked;
        
        public void PickThis()
        {
            if (_picked != null)
                _picked.GetComponent<Button>().targetGraphic.color = Color.white;

            Debug.Log("Clicked this!", gameObject);


            _picked = this;
            _picked.GetComponent<Button>().targetGraphic.color = Color.red;
        }
    }
}


