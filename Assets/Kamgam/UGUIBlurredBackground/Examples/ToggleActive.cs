using UnityEngine;

namespace Kamgam.UGUIBlurredBackground
{
	public class ToggleActive : MonoBehaviour
	{
		public void Toggle()
		{
			gameObject.SetActive(!gameObject.activeSelf);
		} 
	}
}
