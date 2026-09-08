using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
[InitializeOnLoad] static class RehearQAButtonSetup
{
 static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
 static RehearQAButtonSetup(){EditorApplication.update+=Poll;}
 static void Poll(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists("Temp/RehearQAButton.request"))return;
 File.Delete("Temp/RehearQAButton.request");try{Run();}catch(Exception e){File.WriteAllText("Temp/RehearQAButtonTests.txt",e.ToString());}}
 static void Run(){var scene=EditorSceneManager.GetActiveScene();if(scene.path!="Assets/01_Scene/Scene_02_Presentation.unity")throw new Exception("Open presentation scene first");
 var qa=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<QuestionAnswerManager>(true)).Single();
 var so=new SerializedObject(qa);so.FindProperty("questionOutlineSprite").objectReferenceValue=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/04_Images/Textures/UI/Button_WhiteLine.png");so.FindProperty("answerFillSprite").objectReferenceValue=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/UI/PresentationButton.png");so.ApplyModifiedPropertiesWithoutUndo();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
 var go=new GameObject("QA test",typeof(RectTransform),typeof(Image),typeof(Button));var manager=go.AddComponent<QuestionAnswerManager>();var text=new GameObject("label",typeof(RectTransform),typeof(TextMeshProUGUI));text.transform.SetParent(go.transform);
 try{var button=go.GetComponent<Button>();button.targetGraphic=go.GetComponent<Image>();manager.Prepare(button);
 foreach(var name in new[]{"questionOutlineSprite","answerFillSprite"})typeof(QuestionAnswerManager).GetField(name,Flags).SetValue(manager,typeof(QuestionAnswerManager).GetField(name,Flags).GetValue(qa));
 var state=typeof(QuestionAnswerManager).GetField("state",Flags);var finish=typeof(QuestionAnswerManager).GetMethod("FinishQuestionAudio",Flags);var setButton=typeof(QuestionAnswerManager).GetMethod("SetButtonState",Flags);
 state.SetValue(manager,Enum.Parse(state.FieldType,"PlayingQuestion"));setButton.Invoke(manager,new object[]{"질문하는 중...",false});
 if(button.interactable||go.GetComponent<Image>().color!=Color.white)throw new Exception("Speaking style failed");
 manager.OnActionButtonClick();if(state.GetValue(manager).ToString()!="PlayingQuestion")throw new Exception("Speaking click advanced");
 finish.Invoke(manager,null);if(state.GetValue(manager).ToString()!="ReadyToAnswer"||!button.interactable||text.GetComponent<TMP_Text>().text!="응답 시작하기")throw new Exception("Ready failed");
 manager.OnActionButtonClick();if(button.interactable||text.GetComponent<TMP_Text>().text!="응답 완료하기")throw new Exception("Begin answer failed");manager.OnActionButtonClick();if(state.GetValue(manager).ToString()!="Answering")throw new Exception("Duplicate click passed");
  typeof(QuestionAnswerManager).GetField("answerUnlockTime",Flags).SetValue(manager,Time.unscaledTime-1);
 typeof(QuestionAnswerManager).GetMethod("Update",Flags).Invoke(manager,null);if(!button.interactable)throw new Exception("Cooldown did not unlock");
 manager.questionContents=new[]{"First","Second"};typeof(QuestionAnswerManager).GetField("questionCount",Flags).SetValue(manager,2);
 manager.OnActionButtonClick();if((int)typeof(QuestionAnswerManager).GetField("currentIdx",Flags).GetValue(manager)!=1||state.GetValue(manager).ToString()!="QuestionAudioFailed")throw new Exception("Answer completion did not advance to next question");
 typeof(QuestionAnswerManager).GetField("<IsQAPhaseActive>k__BackingField",Flags).SetValue(manager,true);
 foreach(int index in new[]{0,1}){
 typeof(QuestionAnswerManager).GetField("currentIdx",Flags).SetValue(manager,index);
 foreach(string phase in new[]{"PlayingQuestion","ReadyToAnswer","Answering"}){
 state.SetValue(manager,Enum.Parse(state.FieldType,phase));setButton.Invoke(manager,new object[]{"test",true});
 var progress=(TMP_Text)typeof(QuestionAnswerManager).GetField("questionProgressText",Flags).GetValue(manager);
 if(!progress.gameObject.activeSelf||progress.text!=$"질문 {index+1} / 2 · 남은 질문 {1-index}개"||progress.raycastTarget)throw new Exception("Progress mismatch");
 }}
 state.SetValue(manager,Enum.Parse(state.FieldType,"ReadyToFinish"));setButton.Invoke(manager,new object[]{"test",false});
 if(((TMP_Text)typeof(QuestionAnswerManager).GetField("questionProgressText",Flags).GetValue(manager)).gameObject.activeSelf)throw new Exception("Progress still visible after QA");
 File.WriteAllText("Temp/RehearQAProgressTests.txt","PASS first/last question, remaining count excluding current question, persists through speaking/ready/answering, hidden after QA; no raycast interception.");
 File.WriteAllText("Temp/RehearQAButtonTests.txt","PASS begin answer label and lock, cooldown unlock, completion advances next question; PASS speaking white outline/disabled; speech end blue answer start; completion cooldown rejects direct repeated click. Scene sprite references saved. Live TTS not connected.");
 }finally{UnityEngine.Object.DestroyImmediate(go);}
 }
}



