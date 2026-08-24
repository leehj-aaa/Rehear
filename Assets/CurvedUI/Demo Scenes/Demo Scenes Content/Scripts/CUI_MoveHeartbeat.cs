using UnityEngine;

namespace CurvedUI
{
	public class CUI_MoveHeartbeat : MonoBehaviour
	{

		public float speed;
		public bool wrapAroundParent = true;

		private RectTransform _rect;
		private RectTransform _parentRect;

		private void Start()
		{
			_rect = (transform as RectTransform);
			_parentRect = transform.parent as RectTransform;
		}

		private void Update()
		{
			_rect.anchoredPosition = new Vector2(_rect.anchoredPosition.x - speed * Time.deltaTime,
				_rect.anchoredPosition.y);

			if (wrapAroundParent)
			{
				if (_rect.anchoredPosition.x + _rect.rect.width < 0)
					_rect.anchoredPosition = new Vector2(_parentRect.rect.width, _rect.anchoredPosition.y);
			}
		}
	}
}
