using UnityEngine;

namespace CurvedUI
{
    public class CUI_PerlinNoisePosition : MonoBehaviour
    {
        public float samplingSpeed = 1;
        public Vector2 Range;

        private RectTransform _rect;

        private void Start()
        {
            _rect = transform as RectTransform;
        }

        private void Update()
        {
            _rect.anchoredPosition = new Vector2(Mathf.PerlinNoise(Time.time * samplingSpeed, Time.time * samplingSpeed).Remap(0, 1, -Range.x, Range.x),
                Mathf.PerlinNoise(Time.time * samplingSpeed * 1.333f, Time.time * samplingSpeed * 0.888f).Remap(0, 1, -Range.y, Range.y));
        }
    }
}
