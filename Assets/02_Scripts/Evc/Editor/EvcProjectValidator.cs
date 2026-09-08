using System;
using System.Collections.Generic;
using System.IO;
using Rehear.Evc.Audience;
using Rehear.Evc.Audio;
using Rehear.Evc.Contracts;
using Rehear.Evc.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rehear.Evc.Editor
{
    public static class EvcProjectValidator
    {
        private const string PresentationScenePath = "Assets/01_Scene/Scene_02_Presentation.unity";

        [MenuItem("Rehear/EVC/Validate Project")]
        public static void ValidateProject()
        {
            var errors = ValidatePresentationScene();
            errors.AddRange(ValidateRegistries());
            if (errors.Count == 0)
                Debug.Log("EVC project validation passed.");
            else
                Debug.LogError("EVC project validation failed:\n" + string.Join("\n", errors));
        }

        [MenuItem("Rehear/EVC/Validate Selected Clip Pool")]
        public static void ValidateSelectedClipPool()
        {
            var textAsset = Selection.activeObject as TextAsset;
            if (textAsset == null)
            {
                Debug.LogError("clip_pool.json TextAsset을 선택해주세요.");
                return;
            }

            var ids = ClipPoolActionIdExtractor.Extract(textAsset.text);
            if (ids.Count == 0)
            {
                Debug.LogError("선택한 파일에서 action_id를 찾지 못했습니다.");
                return;
            }

            var registryGuids = AssetDatabase.FindAssets("t:AudienceActionRegistry");
            if (registryGuids.Length == 0)
            {
                Debug.LogError("AudienceActionRegistry 에셋이 없습니다.");
                return;
            }

            var registry = AssetDatabase.LoadAssetAtPath<AudienceActionRegistry>(
                AssetDatabase.GUIDToAssetPath(registryGuids[0]));
            var errors = registry.Validate(ids);
            if (errors.Count == 0)
                Debug.Log("clip_pool action mapping validation passed. action_count=" + ids.Count);
            else
                Debug.LogError("clip_pool action mapping validation failed:\n" + string.Join("\n", errors));
        }

        public static List<string> ValidatePresentationScene()
        {
            var errors = new List<string>();
            if (!File.Exists(PresentationScenePath))
            {
                errors.Add("Presentation scene is missing.");
                return errors;
            }

            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var scene = EditorSceneManager.OpenScene(PresentationScenePath, OpenSceneMode.Single);
                var ids = new HashSet<string>(StringComparer.Ordinal);
                var flows = new List<PresentationFlowController>();
                var captures = new List<AudioSegmentCapture>();
                var coordinators = new List<AudienceReactionCoordinator>();
                MonoBehaviour presentationController = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    var transforms = root.GetComponentsInChildren<Transform>(true);
                    for (var index = 0; index < transforms.Length; index++)
                    {
                        var missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transforms[index].gameObject);
                        if (missing > 0)
                            errors.Add("Missing Script: " + GetPath(transforms[index]));
                    }

                    var agents = root.GetComponentsInChildren<AudienceAgent>(true);
                    for (var index = 0; index < agents.Length; index++)
                    {
                        if (!ids.Add(agents[index].AgentId))
                            errors.Add("Duplicate audience id: " + agents[index].AgentId);
                    }

                    // Runtime-spawned audiences are validated from their serialized
                    // prefab pool rather than requiring duplicate scene characters.
                    foreach (var seating in root.GetComponentsInChildren<AudienceSeating>(true))
                    {
                        if (seating.audiencePrefabs == null || seating.audiencePrefabs.Length != 6 ||
                            seating.actionRegistries == null || seating.actionRegistries.Length != 6 ||
                            seating.seats == null || seating.seats.Length != 6)
                        { errors.Add("Audience seating requires six prefabs, registries and seats."); continue; }
                        var seatIds = new HashSet<string>();
                        for (int i = 0; i < 6; i++)
                        {
                            if (!seating.audiencePrefabs[i] || !seating.seats[i])
                            { errors.Add("Missing audience seating reference at " + i); continue; }
                            if (!seatIds.Add(seating.seats[i].SeatId)) errors.Add("Duplicate seat id.");
                            if (!ids.Add(EvcContractRules.RequiredAudienceIds[i])) errors.Add("Duplicate spawned audience id.");
                        }
                    }

                    flows.AddRange(root.GetComponentsInChildren<PresentationFlowController>(true));
                    captures.AddRange(root.GetComponentsInChildren<AudioSegmentCapture>(true));
                    coordinators.AddRange(root.GetComponentsInChildren<AudienceReactionCoordinator>(true));
                    var behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
                    for (var index = 0; index < behaviours.Length; index++)
                    {
                        if (behaviours[index] != null && behaviours[index].GetType().Name == "PresentationController")
                            presentationController = behaviours[index];
                    }
                }

                for (var index = 0; index < EvcContractRules.RequiredAudienceIds.Length; index++)
                {
                    var required = EvcContractRules.RequiredAudienceIds[index];
                    if (!ids.Contains(required))
                        errors.Add("Missing audience id: " + required);
                }
                if (ids.Count != EvcContractRules.AudienceCount)
                    errors.Add("Presentation scene must contain exactly six audience IDs.");

                if (flows.Count != 1)
                    errors.Add("Presentation scene must contain exactly one PresentationFlowController.");
                if (captures.Count != 1)
                    errors.Add("Presentation scene must contain exactly one AudioSegmentCapture.");
                if (coordinators.Count != 1)
                    errors.Add("Presentation scene must contain exactly one AudienceReactionCoordinator.");

                if (flows.Count == 1)
                {
                    var flow = new SerializedObject(flows[0]);
                    if (flow.FindProperty("audioCapture").objectReferenceValue == null)
                        errors.Add("PresentationFlowController.audioCapture is not connected.");
                    if (flow.FindProperty("audienceCoordinator").objectReferenceValue == null)
                        errors.Add("PresentationFlowController.audienceCoordinator is not connected.");
                    if (flow.FindProperty("autoStart").boolValue)
                        errors.Add("PresentationFlowController.autoStart must stay disabled; PresentationController owns startup.");
                }

                if (presentationController == null)
                {
                    errors.Add("PresentationController is missing.");
                }
                else
                {
                    var controller = new SerializedObject(presentationController);
                    var usePipeline = controller.FindProperty("useEvcPipeline");
                    if (usePipeline != null && usePipeline.boolValue && flows.Count == 1)
                    {
                        var flow = new SerializedObject(flows[0]);
                        if (flow.FindProperty("environmentConfig").objectReferenceValue == null)
                            errors.Add("EVC pipeline is enabled without an environment config.");
                        if (captures.Count == 1)
                        {
                            var capture = new SerializedObject(captures[0]);
                            if (capture.FindProperty("policy").objectReferenceValue == null)
                                errors.Add("EVC pipeline is enabled without an audio capture policy.");
                        }
                    }
                }
            }
            finally
            {
                // A headless test run can start without any loaded scene. Unity rejects
                // RestoreSceneManagerSetup in that state because it requires one active
                // scene, so leave a clean empty scene behind instead.
                var hasLoadedActiveScene = false;
                for (var index = 0; index < setup.Length; index++)
                {
                    if (setup[index].isLoaded && setup[index].isActive)
                    {
                        hasLoadedActiveScene = true;
                        break;
                    }
                }

                if (hasLoadedActiveScene)
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
                else
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            return errors;
        }

        private static IEnumerable<string> ValidateRegistries()
        {
            var errors = new List<string>();
            var guids = AssetDatabase.FindAssets("t:AudienceActionRegistry");
            if (guids.Length == 0)
            {
                errors.Add("AudienceActionRegistry asset is missing (blocked until clip_pool.json is supplied).\n");
                return errors;
            }

            for (var index = 0; index < guids.Length; index++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[index]);
                var registry = AssetDatabase.LoadAssetAtPath<AudienceActionRegistry>(path);
                foreach (var error in registry.Validate())
                    errors.Add(path + ": " + error);
            }
            return errors;
        }

        private static string GetPath(Transform transform)
        {
            var result = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                result = transform.name + "/" + result;
            }
            return result;
        }
    }
}
