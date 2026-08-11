using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CurvedUI
{
    public class CUI_ChangeValueOnHold : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private bool pressed;
        private bool selected;

#pragma warning disable 0649
        [SerializeField] private Image bg;
        [SerializeField] private Color SelectedColor;
        [SerializeField] private Color NormalColor;

        [SerializeField] private CanvasGroup IntroCG;
        [SerializeField] private CanvasGroup MenuCG;
#pragma warning restore 0649


        // Update is called once per frame
        private void Update()
        {
            pressed = Input.GetKey(KeyCode.Space) || Input.GetButton("Fire1");

            ChangeVal();       
        }
        
        private void ChangeVal()
        {
            if (GetComponent<Slider>().normalizedValue == 1)
            {
                //fade intro screen if we reached max slider value
                IntroCG.alpha -= Time.deltaTime;
                MenuCG.alpha += Time.deltaTime;
            }
            else {
                //change slider value - increase if its selected and button is pressed
                GetComponent<Slider>().normalizedValue += (pressed && selected) ? Time.deltaTime : -Time.deltaTime;
            }

            //change if intro screen can block interactions based on its opacity
            IntroCG.blocksRaycasts = IntroCG.alpha > 0;
        }
        
        public void OnPointerEnter(PointerEventData data)
        {
            bg.color = SelectedColor;
            bg.GetComponent<CurvedUIVertexEffect>().TesselationRequired = true;
            selected = true;
        }

        public void OnPointerExit(PointerEventData data)
        {
            bg.color = NormalColor;
            bg.GetComponent<CurvedUIVertexEffect>().TesselationRequired = true;
            selected = false;
        }
    }
}
