using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CurvedUI
{
    public static class LayerMaskField
    {
        private static List<string> _layers;
        private static string[] _layerNames;

        public static LayerMask DrawField(string label, LayerMask selected)
        {
            if (_layers == null)
            {
                _layers = new List<string>();
                _layerNames = new string[4];
            }
            else
            {
                _layers.Clear();
            }

            var emptyLayers = 0;
            for (var i = 0; i < 32; i++)
            {
                var layerName = LayerMask.LayerToName(i);

                if (layerName != "")
                {
                    for (; emptyLayers > 0; emptyLayers--) _layers.Add("Layer " + (i - emptyLayers));
                    _layers.Add(layerName);
                }
                else
                {
                    emptyLayers++;
                }
            }

            if (_layerNames.Length != _layers.Count)
            {
                _layerNames = new string[_layers.Count];
            }

            for (var i = 0; i < _layerNames.Length; i++) _layerNames[i] = _layers[i];

            selected.value = EditorGUILayout.MaskField(label, selected.value, _layerNames);

            return selected;
        }
    }
}