using UnityEngine;

namespace CurvedUI
{
    public class CUI_RiseChildrenOverTime : MonoBehaviour
    {
        private float _current;

        public float Speed = 10;
        public float RiseBy = 50;

        private void Update()
        {
            _current += Speed * Time.deltaTime;
            if (Mathf.RoundToInt(_current) >= transform.childCount)
                _current = 0;
            if (Mathf.RoundToInt(_current) < 0)
                _current = transform.childCount - 1;

            for (var i = 0; i < transform.childCount; i++)
            {
                transform.GetChild(i).localPosition = Mathf.RoundToInt(_current) == i 
                    ? transform.GetChild(i).localPosition.ModifyZ(-RiseBy) 
                    : transform.GetChild(i).localPosition.ModifyZ(0);
            }
        }
    }
}
