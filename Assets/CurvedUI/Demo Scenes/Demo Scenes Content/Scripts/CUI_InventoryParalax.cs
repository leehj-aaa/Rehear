using UnityEngine;

namespace CurvedUI
{
    public class CUI_InventoryParalax : MonoBehaviour
    {
#pragma warning disable 0649
        [SerializeField] private Transform front;
        [SerializeField] private Transform back;
#pragma warning restore 0649

        private Vector3 _initFg;
        private Vector3 _initBg;

        public float change = 50;

        // Use this for initialization
        private void Start()
        {
            _initFg = front.position;
            _initBg = back.position;
        }

        // Update is called once per frame
        private void Update()
        {
            front.position = front.position.ModifyX(_initFg.x + Input.mousePosition.x.Remap(0, Screen.width, -change, change));
            back.position = back.position.ModifyX(_initBg.x - Input.mousePosition.x.Remap(0, Screen.width, -change, change));

            front.position = front.position.ModifyY(_initFg.y + Input.mousePosition.y.Remap(0, Screen.height, -change, change) * (Screen.height / Screen.width));
            back.position = back.position.ModifyY(_initBg.y - Input.mousePosition.y.Remap(0, Screen.height, -change, change) * (Screen.height / Screen.width));
        }
    }
}
