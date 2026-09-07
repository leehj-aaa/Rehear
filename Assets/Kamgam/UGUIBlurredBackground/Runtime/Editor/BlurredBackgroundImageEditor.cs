#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Kamgam.UGUIBlurredBackground
{
    [CustomEditor(typeof(BlurredBackgroundImage))]
    [CanEditMultipleObjects]
    public class BlurredBackgroundEditor : UnityEditor.Editor
    {
        BlurredBackgroundImage obj;

        public void OnEnable()
        {
            obj = target as BlurredBackgroundImage;
        }

        public override void OnInspectorGUI()
        {
            bool usesSprite = obj.sprite != null;
            if (usesSprite)
            {
                EditorGUILayout.HelpBox(
                    "You have assigned a sprite to the Blurred Image.\n" +
                    "Please notice that sprites only have limited support.\n" +
                    "Usually the best option is to add a mask parent that uses the sprite instead.\n\n" +
                    "HINT: If you are using custom outlines for masking instead of transparent pixels then all you need to do is enable the 'Use Sprite Mesh' option below.",
                    MessageType.Warning);
            }

            GUILayout.BeginHorizontal();
            GUI.enabled = usesSprite;
            if (GUILayout.Button(new GUIContent("Create Mask From Sprite", "Only available if a sprite is assigned as image")))
            {
                createMaskAsParent(obj);
            }
            bool hasMaskParent = obj.transform.parent != null && obj.transform.parent.GetComponent<Mask>() != null && obj.transform.parent.GetComponent<Image>() != null;
            GUI.enabled = hasMaskParent;
            if (GUILayout.Button(new GUIContent("Revert Mask", "Only available if a sprite mask parent exists")))
            {
                revertMask(obj);
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            base.OnInspectorGUI();
            obj._EditorApplyChangedValues();
        }
        
        void createMaskAsParent(BlurredBackgroundImage blurredImage)
        {
            RectTransform childRect = blurredImage.GetComponent<RectTransform>();

            var transform = blurredImage.transform;
            int siblingIndex = transform.GetSiblingIndex();
            Transform parent = transform.parent;

            // Create new parent
            GameObject newParent = new GameObject(blurredImage.name + "_Mask");
            RectTransform parentRect = newParent.AddComponent<RectTransform>();

            // Copy transform
            parentRect.SetParent(parent, false);
            parentRect.anchoredPosition = childRect.anchoredPosition;
            parentRect.sizeDelta = childRect.sizeDelta;
            parentRect.anchorMin = childRect.anchorMin;
            parentRect.anchorMax = childRect.anchorMax;
            parentRect.pivot = childRect.pivot;

            // Move img
            blurredImage.transform.SetParent(newParent.transform, false);

            // Fill parent
            childRect.anchorMin = Vector2.zero;
            childRect.anchorMax = Vector2.one;
            childRect.offsetMin = Vector2.zero;
            childRect.offsetMax = Vector2.zero;

            // Restore sibling index
            newParent.transform.SetSiblingIndex(siblingIndex);
            
            // Add Mask
            var img = newParent.AddComponent<Image>();
            img.sprite = blurredImage.sprite;
            copyImageSettings(blurredImage, img);
            
            blurredImage.sprite = null;

            var mask = newParent.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            
            EditorGUIUtility.PingObject(blurredImage.gameObject);
            EditorApplication.delayCall += () => EditorGUIUtility.PingObject(newParent);
        }

        void revertMask(BlurredBackgroundImage blurredImage)
        {
            var img = obj.transform.parent.GetComponent<Image>();
            var sprite = img.sprite;
            blurredImage.sprite = sprite;
            copyImageSettings(img, blurredImage); 

            RectTransform childRect = blurredImage.GetComponent<RectTransform>();

            var transform = blurredImage.transform as RectTransform;
            var parent = transform.parent as RectTransform;
            int siblingIndex = parent.GetSiblingIndex();
            
            transform.SetParent(parent.parent, worldPositionStays: true);
            transform.SetSiblingIndex(siblingIndex);
            
            GameObject.DestroyImmediate(parent.gameObject);
        }
        
        void copyImageSettings(Image source, Image target)
        {
            if (source == null || target == null)
                return;

            target.type = source.type;
            target.preserveAspect = source.preserveAspect;
            target.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
            target.fillMethod = source.fillMethod;
            target.fillOrigin = source.fillOrigin;
            target.fillAmount = source.fillAmount;
            target.fillClockwise = source.fillClockwise;
            target.useSpriteMesh = source.useSpriteMesh;
        }
    }

#if UNITY_EDITOR
    public static class ImageCreator
    {
        [MenuItem("GameObject/UI/Blurred Background Image", priority = 2005)]
        public static void Create()
        {
            // Potential fix: Delay for users in hopes of Selection.activeGameObject updating (it seems delayed sometimes).
            EditorApplication.delayCall += () =>
            {
                var go = new GameObject("Image (Blurred Background)");
                var image = go.AddComponent<BlurredBackgroundImage>();
                
                // Use selection as parent
                var parent = Selection.activeGameObject;
                if (parent != null)
                {
                    go.transform.SetParent(parent.transform);
                    
                    if (PrefabUtility.GetPrefabAssetType(parent) != PrefabAssetType.NotAPrefab)
                    {
                        PrefabUtility.RecordPrefabInstancePropertyModifications(parent);
                    }
                    
                    EditorGUIUtility.PingObject(go);
                    Selection.objects = new Object[] { go };
                }
                else
                {
                    // Fallback: Use first canvas as parent
                    var canvas = CompatibilityUtils.FindObjectOfType<Canvas>(includeInactive: true);
                    if (canvas != null)
                    {
                        go.transform.SetParent(canvas.transform);
                        
                        EditorGUIUtility.PingObject(go);
                        Selection.objects = new Object[] { go };
                        
                        Debug.LogWarning("UGUIBlurredBackground: No parent found to add the image too. Added to the first canvas in the scene.");
                    }
                    else
                    {
                        Debug.LogError("UGUIBlurredBackground: No canvas found to add the image too. Please select a canvas.");
                    }
                }

                EditorScheduler.Schedule(1f, image.SetVerticesDirty);
            };
        }
    }
#endif
    
}
#endif