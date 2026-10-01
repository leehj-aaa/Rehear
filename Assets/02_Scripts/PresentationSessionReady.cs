using Rehear.Evc.Presentation;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Confirms the cached PIN session before the separate presentation/recording start.</summary>
[DefaultExecutionOrder(-50)]
public sealed class PresentationSessionReady : MonoBehaviour
{
    public PresentationController controller;
    public TMP_Text presentationTitle;
    public TMP_Text sessionType, duration, questionCount, audienceCount, environment, expertise, interest;
    public TMP_Text questionDuration;
    public GameObject readyPanel, webReportGuide;
    public Button continueButton, returnButton;
    public Vector3 cameraLocalPosition = new Vector3(0f, 0f, 1.7f);
    public Vector3 cameraLocalEuler;
    bool leaving;
    Transform presentationRig;
    Transform xrCamera;
    Vector3 authoredRigPosition;
    float authoredRigYaw;
    readonly List<BaseRaycaster> suspendedRaycasters = new();
    public bool IsOpen => gameObject.activeInHierarchy && RuntimeSessionData.IsLoaded && RuntimeSessionData.Session != null;

    void Awake()
    {
        FindAndLockPresentationRig();
    }

    IEnumerator Start()
    {
        if (!RuntimeSessionData.IsLoaded || RuntimeSessionData.Session == null)
        {
            gameObject.SetActive(false);
            yield break;
        }

        IsolateModalRaycasts();
        Populate();
        if (continueButton) continueButton.interactable = true;

        // Wait until OpenXR has supplied the first stable HMD pose, then restore
        // the authored presentation origin before placing the confirmation UI.
        yield return null;
        yield return null;
        yield return null;
        StabilizePresentationRig();
        AlignToViewer();
    }

    void FindAndLockPresentationRig()
    {
        var camera = Camera.main;
        if (!camera)
            return;

        xrCamera = camera.transform;
        Transform candidate = xrCamera;
        while (candidate != null)
        {
            if (candidate.name.Contains("XR Origin"))
            {
                presentationRig = candidate;
                break;
            }

            candidate = candidate.parent;
        }

        if (!presentationRig)
        {
            Debug.LogWarning("[발표 씬] XR Origin을 찾지 못했습니다.", this);
            return;
        }

        authoredRigPosition = presentationRig.position;
        authoredRigYaw = presentationRig.eulerAngles.y;

        // The presentation is stationary. Locomotion and gravity can move the
        // origin before the first frame and make the viewer and UI appear low.
        var characterController = presentationRig.GetComponent<CharacterController>();
        if (characterController)
            characterController.enabled = false;

        foreach (MonoBehaviour behaviour in
                 presentationRig.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (!behaviour)
                continue;

            string typeName = behaviour.GetType().Name;
            if (typeName.Contains("MoveProvider") ||
                typeName.Contains("CharacterControllerDriver") ||
                typeName.Contains("GravityProvider"))
            {
                behaviour.enabled = false;
            }
        }
    }

    void StabilizePresentationRig()
    {
        if (!presentationRig || !xrCamera)
            return;

        float yawDelta = Mathf.DeltaAngle(xrCamera.eulerAngles.y, authoredRigYaw);
        presentationRig.RotateAround(xrCamera.position, Vector3.up, yawDelta);

        Vector3 planarOffset = xrCamera.position - presentationRig.position;
        planarOffset.y = 0f;
        presentationRig.position = new Vector3(
            authoredRigPosition.x - planarOffset.x,
            authoredRigPosition.y,
            authoredRigPosition.z - planarOffset.z);

        Debug.Log("[발표 씬] 플레이어 시작 위치와 방향을 자동 보정했습니다.");
    }

    public void AlignToViewer()
    {
        var camera = xrCamera ? xrCamera : Camera.main ? Camera.main.transform : null;
        if (!camera) return;
        var heading = Quaternion.Euler(0f, camera.eulerAngles.y, 0f);
        transform.SetPositionAndRotation(
            camera.position + heading * cameraLocalPosition,
            heading * Quaternion.Euler(cameraLocalEuler));
    }

    public void Populate()
    {
        SetText(presentationTitle, Value(RuntimeSessionData.PresentationTitle, "발표 제목 없음"));
        SetText(sessionType, Value(RuntimeSessionData.PresentationPurpose, "발표 모드"));
        SetText(duration, RuntimeSessionData.DurationMinutes + "분");
        SetText(questionCount, RuntimeSessionData.QaCount + "개");
        // The current server contract supplies qa_count, not a duration. Never
        // relabel a question count as minutes or ship the design's sample value.
        int qaMinutes = RuntimeSessionData.Session?.page_1?.qa_duration_minutes ?? 0;
        SetText(questionDuration, qaMinutes > 0 ? qaMinutes + "분" : "미설정");
        SetText(audienceCount, RuntimeSessionData.AudienceScale + "명");
        SetText(environment, Value(RuntimeSessionData.EnvironmentType, "미설정"));
        SetText(expertise, Value(RuntimeSessionData.AudienceExpertise, "미설정"));
        SetText(interest, Value(RuntimeSessionData.AudienceInterest, "미설정"));
    }

    static void SetText(TMP_Text target, string value)
    {
        if (target) target.text = value;
    }

    static string Value(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

    public void Confirm()
    {
        if (leaving || !RuntimeSessionData.IsLoaded || RuntimeSessionData.Session == null) return;
        RestoreSuspendedRaycasts();
        gameObject.SetActive(false);
        if (controller) controller.SetSessionConfirmationVisible(false);
    }

    public void ShowWebReportGuide()
    {
        if (!webReportGuide) return;
        if (readyPanel) readyPanel.SetActive(false);
        webReportGuide.SetActive(true);
    }

    public void ReturnToPin()
    {
        if (leaving || !Application.CanStreamedLevelBeLoaded("Scene_00")) return;
        leaving = true;
        if (continueButton) continueButton.interactable = false;
        if (returnButton) returnButton.interactable = false;
        RuntimeSessionData.Clear();
        PresentationSessionContext.Current.ClearAll();
        SceneManager.LoadSceneAsync("Scene_00");
    }

    void IsolateModalRaycasts()
    {
        RestoreSuspendedRaycasts();
        var ownRaycaster = GetComponent<BaseRaycaster>();

        foreach (var raycaster in FindObjectsByType<BaseRaycaster>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            if (!raycaster || raycaster == ownRaycaster ||
                !raycaster.enabled || !raycaster.gameObject.activeInHierarchy)
                continue;

            raycaster.enabled = false;
            suspendedRaycasters.Add(raycaster);
        }
    }

    void RestoreSuspendedRaycasts()
    {
        foreach (var raycaster in suspendedRaycasters)
        {
            if (raycaster)
                raycaster.enabled = true;
        }

        suspendedRaycasters.Clear();
    }

    void OnDestroy()
    {
        RestoreSuspendedRaycasts();
    }
}
