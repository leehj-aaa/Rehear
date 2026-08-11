using UnityEngine;
using UnityEngine.UI;

namespace CurvedUI
{
    public class CUI_AnimateCurvedFillOnStart : MonoBehaviour
    {
        private void Update()
        {
            var set = GetComponent<CurvedUISettings>();
            var text = GetComponentInChildren<Text>();

            if (Time.time < 1.5f)
            {
                set.RingFill = Mathf.PerlinNoise(Time.time * 30.23234f, Time.time * 30.2313f) * 0.15f;
                text.text = "Accesing Mainframe...";

            }
            else if (Time.time < 2.5f)
            {
                set.RingFill = Mathf.Clamp(set.RingFill + Time.deltaTime * 3, 0, 1);
                text.text = "Mainframe Active";
            }
        }
    }
}
