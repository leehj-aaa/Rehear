using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CurvedUI {

	public static class CurvedUIExtensionMethods
	{
		/// <summary>
		///Direct Vector3 comparison can produce wrong results sometimes due to float inaccuracies.
		///This is an approximate comparison.
		/// <returns></returns>
		public static bool AlmostEqual(this Vector3 a, Vector3 b, double accuracy = 0.01) 
			=> Vector3.SqrMagnitude(a - b) < accuracy;

		public static float Remap(this float value, float from1, float to1, float from2, float to2) 
			=> (value - from1) / (to1 - from1) * (to2 - from2) + from2;

		public static float RemapAndClamp(this float value, float from1, float to1, float from2, float to2) 
			=> value.Remap(from1, to1, from2, to2).Clamp(from2, to2);
		
		public static float Remap(this int value, float from1, float to1, float from2, float to2) 
			=> (value - from1) / (to1 - from1) * (to2 - from2) + from2;

		public static double Remap(this double value, double from1, double to1, double from2, double to2) 
			=> (value - from1) / (to1 - from1) * (to2 - from2) + from2;

		public static float Clamp(this float value, float min, float max) => Mathf.Clamp(value, min, max);

		public static float Clamp(this int value, int min, int max) => Mathf.Clamp(value, min, max);

		public static int Abs(this int value) => Mathf.Abs(value);

		public static float Abs(this float value) => Mathf.Abs(value);
		
		public static int ToInt(this float value) => Mathf.RoundToInt(value);

		public static int FloorToInt(this float value) => Mathf.FloorToInt(value);

		public static int CeilToInt(this float value) => Mathf.FloorToInt(value);

		public static Vector3 ModifyX(this Vector3 trans, float newVal)
		{
			trans = new Vector3(newVal, trans.y, trans.z);
			return trans;
		}

		public static Vector3 ModifyY(this Vector3 trans, float newVal)
		{
			trans = new Vector3(trans.x, newVal, trans.z);
			return trans;
		}

		public static Vector3 ModifyZ(this Vector3 trans, float newVal)
		{
			trans = new Vector3(trans.x, trans.y, newVal);
			return trans;
		}

		public static Vector2 ModifyVectorX(this Vector2 trans, float newVal)
		{
			trans = new Vector2(newVal, trans.y);
			return trans;
		}

		public static Vector2 ModifyVectorY(this Vector2 trans, float newVal)
		{
			trans = new Vector2(trans.x, newVal);
			return trans;
		}


	
		
		#region STRINGS
		public static string ToStringInline<T>(this IEnumerable<T> list, bool eachItemInNewLine = false, string eachItemPrefix = "")
		{
			if (list == null) return "NULL";
        
			var ret = "";
        
			foreach (var item in list)
			{
				if(string.IsNullOrEmpty(ret) == false) ret += eachItemInNewLine ? "\n" : ", ";
				ret += eachItemPrefix;
				ret += item.ToString();
			}
			return ret;
		}
        
		public static string ToStringSeparatedBy<T>(this IEnumerable<T> list, string separator = ", ", string lastSeparatorOverride = null)
		{
			if (list == null) return "";
        
			var ret = "";
			var array = list.ToList();

			for (var i = 0; i < array.Count; i++)
			{
				var item = array[i].ToString();
                
				//skip if null
				if (string.IsNullOrEmpty(item)) continue;
                
				//add to string
				ret += item;
                
				//add separator
				if (i < array.Count - 1)
				{
					//should last separator be different?
					ret += array.Count > 1 && i == array.Count - 2 && !string.IsNullOrEmpty(lastSeparatorOverride)
						? lastSeparatorOverride
						: separator;
				}
			}
			return ret;
		}
		#endregion
		
		
		
		#region TRANSFORM		
		public static Ray ToRay(this Transform trans) => new(trans.position, trans.forward);
		
		/// <summary>
		/// Resets transform's local position, rotation and scale
		/// </summary>
		public static void ResetTransform(this Transform trans)
		{
			trans.localPosition = Vector3.zero;
			trans.localRotation = Quaternion.identity;
			trans.localScale = Vector3.one;
		}
		#endregion
		
		
		
		#region COMPONENTS		
		public static T AddComponentIfMissing<T>(this GameObject go) where T : Component 
			=> go.GetComponent<T>() == null ? go.AddComponent<T>() : go.GetComponent<T>();
		
		/// <summary>
		/// Checks if given component is preset and if not, adds it and returns it.
		/// </summary>
		public static T AddComponentIfMissing<T>(this Component go) where T : Component 
			=> go.gameObject.AddComponentIfMissing<T>();
		
		public static void RemoveComponentsFromChildren(this Component parent, params Type[] types)
		{
			if (parent == null || types == null || types.Length == 0) return;
        
			var componentsToRemove = parent.GetComponentsInChildren<Transform>(true)
				.SelectMany(child => types.SelectMany(child.GetComponents))
				.ToList();
            
			componentsToRemove.ForEach(UnityEngine.Object.DestroyImmediate);
		}
		#endregion
	}

}
