using UnityEngine;

namespace CurvedUI
{
	public class CUI_ChangeColor : MonoBehaviour
	{
		public void ChangeColorToBlue()
		{
			GetComponent<Renderer>().material.color = Color.blue;
		}
	
		public void ChangeColorToCyan()
		{
			GetComponent<Renderer>().material.color = Color.cyan;
		}
	
		public void ChangeColorToWhite()
		{
			GetComponent<Renderer>().material.color = Color.white;
		}
	}
}

