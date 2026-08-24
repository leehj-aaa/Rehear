using System.Collections.Generic;
using CurvedUI.Core.Utilities;
using TMPro;
using UnityEngine;
[assembly: OptionalDependency("TMPro.TextMeshProUGUI", "CURVEDUI_TMP")]

namespace CurvedUI.Core.Integrations
{
    [ExecuteInEditMode][DefaultExecutionOrder(110)]
    public class CurvedUITMP : MonoBehaviour
    {

#if CURVEDUI_TMP || TMP_PRESENT
        //internal
        private CurvedUIVertexEffect crvdVE;
        private TextMeshProUGUI tmpText;
        private CurvedUISettings mySettings;

        private List<UIVertex> m_UIVerts = new();
        private UIVertex m_tempVertex;
        private CurvedUITMPSubmesh m_tempSubMsh;

        private Vector2 savedSize;
        private Vector3 savedUp;
        private Vector3 savedPos;
        private Vector3 savedLocalScale;
        private Vector3 savedGlobalScale;
        private string savedText;
        private List<CurvedUITMPSubmesh> subMeshes = new();

        //flags
        public bool Dirty; // set this to true to force mesh update.
        private bool curvingRequired;
        private bool tesselationRequired;
        private bool quitting;

        //mesh data
        private Vector3[] vertices;
        //These are commented here and throught the script,
        //cause CurvedUI operates only on vertex positions,
        //but left here for future-proofing against some TMP features.
        //private Color32[] colors32;
        //private Vector2[] uv;
        //private Vector2[] uv2;
        //private Vector2[] uv3;
        //private Vector2[] uv4;
        //private Vector3[] normals;
        //private Vector4[] tangents;
        //private int[] indices;

        #region LIFECYCLE

        private void Start()
        {
            if (mySettings == null)
                mySettings = GetComponentInParent<CurvedUISettings>();
        }


        private void OnEnable()
        {
            FindTMP();

            if (tmpText)
            {
                tmpText.RegisterDirtyMaterialCallback(TesselationRequiredCallback);
                TMPro_EventManager.TEXT_CHANGED_EVENT.Add(TMPTextChangedCallback);

                tmpText.SetText(tmpText.text);
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.EditorApplication.update += LateUpdate;
#endif
        }


        private void OnDisable()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.EditorApplication.update -= LateUpdate;
#endif
            if (tmpText)
            {
                tmpText.UnregisterDirtyMaterialCallback(TesselationRequiredCallback);
                TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(TMPTextChangedCallback);
            }
        }


        private void OnDestroy()
        {
            quitting = true;
        }


        private void LateUpdate()
        {
            //if we're missing stuff, find it
            if (!tmpText) FindTMP();

            if (mySettings == null) return;

           if (tmpText == null || quitting) return;
            
           
            //Edit Mesh on TextMeshPro component
            if (ShouldTesselate())
                tesselationRequired = true;

            if (Dirty || tesselationRequired || (curvingRequired && !Application.isPlaying))
            {
                if (mySettings == null)
                {
                    enabled = false;
                    return;
                }
                    
                //Get the flat vertices from TMP object.
                //store a copy of flat UIVertices for later so we dont have to retrieve the Mesh every framee.
                tmpText.renderMode = TextRenderFlags.Render;
                if(savedText != tmpText.text) tmpText.SetAllDirty(); 
                tmpText.ForceMeshUpdate(true);
                CreateUIVertexList(tmpText.mesh);

                //Tesselate and Curve the flat UIVertices stored in Vertex Helper
                crvdVE.ModifyTMPMesh(ref m_UIVerts);

                //fill curved vertices back to TMP mesh
                FillMeshWithUIVertexList(tmpText.mesh, m_UIVerts);

                //cleanup
                tmpText.renderMode = TextRenderFlags.DontRender;

                //save current data
                savedLocalScale = mySettings.transform.localScale;
                savedGlobalScale = mySettings.transform.lossyScale;
                savedSize = (transform as RectTransform).rect.size;
                savedUp = mySettings.transform.worldToLocalMatrix.MultiplyVector(transform.up);
                savedPos = mySettings.transform.worldToLocalMatrix.MultiplyPoint3x4(transform.position);
                savedText = tmpText.text;
                    
                //reset flags
                tesselationRequired = false;
                curvingRequired = false;
                Dirty = false;

                //prompt submeshes to update
                FindSubmeshes();
                foreach (var mesh in subMeshes)
                    mesh.UpdateSubmesh(true, false);
            }

            //Upload mesh to TMP Object's renderer
            if(tmpText.text.Length > 0)
                tmpText.canvasRenderer.SetMesh(tmpText.mesh);
            else 
                tmpText.canvasRenderer.Clear();
        }
        #endregion




        #region UIVERTEX MANAGEMENT

        private void CreateUIVertexList(Mesh mesh)
        {
            //trim if too long list
            if (mesh.vertexCount < m_UIVerts.Count)
                m_UIVerts.RemoveRange(mesh.vertexCount, m_UIVerts.Count - mesh.vertexCount);

            //extract mesh data
            vertices = mesh.vertices;
            //colors32 = mesh.colors32;
            //uv = mesh.uv;
            //uv2 = mesh.uv2;
            //uv3 = mesh.uv3;
            //uv4 = mesh.uv4;
            //normals = mesh.normals;
            //tangents = mesh.tangents;

            for (int i = 0; i < mesh.vertexCount; i++)
            {
                //add if list too short
                if (m_UIVerts.Count <= i)
                {
                    m_tempVertex = new UIVertex();
                    GetUIVertexFromMesh(ref m_tempVertex, i);
                    m_UIVerts.Add(m_tempVertex);
                }
                else //modify
                {
                    m_tempVertex = m_UIVerts[i];
                    GetUIVertexFromMesh(ref m_tempVertex, i);
                    m_UIVerts[i] = m_tempVertex;
                }
            }
            //indices = mesh.GetIndices(0);
        }

        private void GetUIVertexFromMesh(ref UIVertex vert, int i)
        {
            vert.position = vertices[i];
            //vert.color = colors32[i];
            //vert.uv0 = uv[i];
            //vert.uv1 = uv2.Length > i ? uv2[i] : Vector2.zero;
            //vert.uv2 = uv3.Length > i ? uv3[i] : Vector2.zero;
            //vert.uv3 = uv4.Length > i ? uv4[i] : Vector2.zero;
            //vert.normal = normals[i];
            //vert.tangent = tangents[i];
        }

        private void FillMeshWithUIVertexList(Mesh mesh, List<UIVertex> list)
        {
            if (list.Count >= 65536)
            {
                Debug.LogError("CURVEDUI: Unity UI Mesh can not have more than 65536 vertices. Remove some UI elements or lower quality.");
                return;
            }

            for (var i = 0; i < list.Count; i++)
            {
                vertices[i] = list[i].position;
                //colors32[i] = list[i].color;
                //uv[i] = list[i].uv0;
                //if (uv2.Length < i) uv2[i] = list[i].uv1;
                ////if (uv3.Length < i) uv3[i] = list[i].uv2;
                ////if (uv4.Length < i) uv4[i] = list[i].uv3;
                //normals[i] = list[i].normal;
                //tangents[i] = list[i].tangent;
            }
            
            //Fill mesh with data
            mesh.vertices = vertices;
            //mesh.colors32 = colors32;
            //mesh.uv = uv;
            //mesh.uv2 = uv2;
            ////mesh.uv3 = uv3;
            ////mesh.uv4 = uv4;
            //mesh.normals = normals;
            //mesh.tangents = tangents;
            //mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
        }
        #endregion



        #region PRIVATE
        private void FindTMP()
        {
            if (GetComponent<TextMeshProUGUI>() == null) return;
            
            tmpText = gameObject.GetComponent<TextMeshProUGUI>();
            crvdVE = gameObject.GetComponent<CurvedUIVertexEffect>();
            mySettings = GetComponentInParent<CurvedUISettings>();
            transform.hasChanged = false;

            FindSubmeshes();
        }


        private void FindSubmeshes()
        {
            foreach (var sub in GetComponentsInChildren<TMP_SubMeshUI>())
            {
                m_tempSubMsh = sub.gameObject.AddComponentIfMissing<CurvedUITMPSubmesh>();
                if (!subMeshes.Contains(m_tempSubMsh))
                    subMeshes.Add(m_tempSubMsh);
            }
        }

        private bool ShouldTesselate()
        {
            if (savedSize != ((RectTransform)transform).rect.size)
                return true;
            if (savedLocalScale != mySettings.transform.localScale)
                return true;
            if (savedGlobalScale != mySettings.transform.lossyScale)
                return true;
            if (!savedUp.AlmostEqual(mySettings.transform.worldToLocalMatrix.MultiplyVector(transform.up)))
                return true;

            var testedPos = mySettings.transform.worldToLocalMatrix.MultiplyPoint3x4(transform.position);
            
            if (!savedPos.AlmostEqual(testedPos))
            {
                if (mySettings.Shape != CurvedUISettings.CurvedUIShape.CYLINDER || Mathf.Pow(testedPos.x - savedPos.x, 2) > 0.00001 || Mathf.Pow(testedPos.z - savedPos.z, 2) > 0.00001)
                {
                    return true;
                }
            }

            return false;
        }
        #endregion




        #region EVENTS AND CALLBACKS
        private void TMPTextChangedCallback(object obj)
        {
            if (obj != (object)tmpText) return;

            tesselationRequired = true;
        }

        private void TesselationRequiredCallback()
        {
            tesselationRequired = true;
            curvingRequired = true;
        }
        #endregion

#endif
    }
}



