using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;
[InitializeOnLoad] static class RehearPhoneSetup {
 const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
 static RehearPhoneSetup(){EditorApplication.update+=Poll;}
 static void Poll(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists("Temp/RehearPhone.request"))return;
 var command=File.ReadAllText("Temp/RehearPhone.request").Trim();File.Delete("Temp/RehearPhone.request");try{Setup(command=="setup");}catch(Exception e){File.WriteAllText("Temp/RehearPhoneReport.txt",e.ToString());}}
 static void Capture(GameObject root,GameObject phone){
 var go=new GameObject("Phone preview camera");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,root.scene);var camera=go.AddComponent<Camera>();camera.scene=root.scene;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.gray;
 camera.transform.position=phone.transform.position+new Vector3(.18f,.10f,.5f);camera.transform.LookAt(phone.transform.position);camera.orthographic=true;camera.orthographicSize=.19f;camera.nearClipPlane=.01f;
 var light=go.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;
 var rt=new RenderTexture(800,800,24);camera.targetTexture=rt;var old=RenderTexture.active;
 try{camera.Render();RenderTexture.active=rt;var tex=new Texture2D(800,800,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,800,800),0,0);tex.Apply();File.WriteAllBytes("Temp/RehearPhonePreview.png",tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);}finally{RenderTexture.active=old;camera.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(go);}
 }
 static void Setup(bool save){var s=new StringBuilder();var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/03_Prefabs/phone.prefab");
 var cat=AssetDatabase.LoadAssetAtPath<AudienceAnimationCatalog>("Assets/Settings/AudienceAnimationCatalog.asset");
 foreach(var path in Directory.GetFiles("Assets/03_Prefabs/Audience/Presentation","Aud_*.prefab").Select(p=>p.Replace('\\','/'))){
 var root=PrefabUtility.LoadPrefabContents(path);try{
 var player=root.GetComponentInChildren<AudienceAnimationPlayer>(true);var a=root.GetComponentInChildren<Animator>();
 var bones=root.GetComponentsInChildren<Transform>(true);var positions=bones.Select(t=>t.localPosition).ToArray();var rotations=bones.Select(t=>t.localRotation).ToArray();var scales=bones.Select(t=>t.localScale).ToArray();
 cat.TryGetClip("ACT_02.photoslide",player.Gender,out var clip);
 var field=typeof(AudienceAnimationPlayer).GetField("photoPhone",Flags);
 GameObject phone=(GameObject)field.GetValue(player);
 if(save){
root.name=System.IO.Path.GetFileNameWithoutExtension(path);
 if(phone)UnityEngine.Object.DestroyImmediate(phone);
 clip.SampleAnimation(a.gameObject,clip.length*.5f);
 var hand=bones.Single(t=>t && t.name=="hand_l");
 var left=bones.Single(t=>t && t.name=="middle_02_l");var right=bones.Single(t=>t && t.name=="middle_02_r");
 phone=(GameObject)PrefabUtility.InstantiatePrefab(source,hand);
 phone.name="Photo Phone";
 phone.transform.position=(left.position+right.position)*.5f+root.transform.forward*.005f;
 phone.transform.rotation=Quaternion.LookRotation(root.transform.forward,root.transform.up)*Quaternion.Euler(180,0,0);
 // Native phone is already metre-scaled. Match a 7 cm wide handset.
 var width=source.GetComponent<MeshFilter>().sharedMesh.bounds.size.x;
 phone.transform.localScale=Vector3.one*(.07f/width)/hand.lossyScale.x;
 phone.SetActive(false);PrefabUtility.RecordPrefabInstancePropertyModifications(phone.transform);PrefabUtility.RecordPrefabInstancePropertyModifications(phone);
 for(int i=0;i<bones.Length;i++){if(!bones[i])continue;bones[i].localPosition=positions[i];bones[i].localRotation=rotations[i];bones[i].localScale=scales[i];}
 field.SetValue(player,phone);EditorUtility.SetDirty(player);
 PrefabUtility.SaveAsPrefabAsset(root,path);
 }
 if(!phone||phone.activeSelf||phone.transform.parent.name!="hand_l")throw new Exception(path+" missing/inactive hand attachment");
 typeof(AudienceAnimationPlayer).GetMethod("CreateAnimationGraph",Flags).Invoke(player,null);
 var advance=typeof(AudienceAnimationPlayer).GetMethod("Advance",Flags);
 if(!player.PlayServerVariation("ACT_02.photoslide",3,1))throw new Exception("Photo rejected");
 advance.Invoke(player,new object[]{.7f});if(!phone.activeSelf)throw new Exception("Photo missing");
 player.StopAction();advance.Invoke(player,new object[]{1f});if(phone.activeSelf)throw new Exception("Stop left phone visible");
 typeof(AudienceAnimationPlayer).GetMethod("OnDisable",Flags).Invoke(player,null); if(path.EndsWith("Aud_M_01.prefab")){clip.SampleAnimation(a.gameObject,clip.length*.5f);phone.SetActive(true);Capture(root,phone);phone.SetActive(false);}
 s.AppendLine("PASS "+root.name+": nested source phone, left hand, hidden idle; photo start/completion/stop/disable/other action. local="+phone.transform.localPosition.ToString("F4"));
 }finally{PrefabUtility.UnloadPrefabContents(root);}}
 File.WriteAllText("Temp/RehearPhoneReport.txt",s.ToString());}
}






