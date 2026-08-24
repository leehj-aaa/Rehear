using System;
using System.Collections;
using UnityEngine;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class PresentationMicrophoneRecorder :
    MonoBehaviour
{
    [Header("녹음 설정")]

    [SerializeField]
    [Min(1)]
    private int chunkSeconds = 5;

    [SerializeField]
    private int sampleRate = 16000;

    [SerializeField]
    private bool startAutomatically = false;

    [Header("테스트")]

    [SerializeField]
    private bool printChunkLog = true;

    public event Action<byte[], float>
        ChunkRecorded;

    public bool IsRecording =>
        recordingCoroutine != null;

    private Coroutine recordingCoroutine;
    private string microphoneDevice;

    private IEnumerator Start()
    {
        if (!startAutomatically)
            yield break;

        yield return RequestMicrophonePermission();

        if (HasMicrophonePermission())
            StartRecording();
    }

    public void StartRecording()
    {
        if (IsRecording)
        {
            Debug.LogWarning(
                "[마이크] 이미 녹음 중입니다."
            );

            return;
        }

        if (!HasMicrophonePermission())
        {
            StartCoroutine(
                RequestPermissionAndStart()
            );

            return;
        }

        if (Microphone.devices == null ||
            Microphone.devices.Length == 0)
        {
            Debug.LogError(
                "[마이크] 사용할 수 있는 마이크가 없습니다."
            );

            return;
        }

        microphoneDevice =
            Microphone.devices[0];

        recordingCoroutine =
            StartCoroutine(RecordLoop());

        Debug.Log(
            "[마이크] 녹음 시작" +
            "\n장치: " + microphoneDevice +
            "\n조각 길이: " + chunkSeconds + "초" +
            "\n샘플레이트: " + sampleRate
        );
    }

    public void StopRecording()
    {
        bool wasRecording =
            recordingCoroutine != null ||
            (
                !string.IsNullOrEmpty(
                    microphoneDevice
                ) &&
                Microphone.IsRecording(
                    microphoneDevice
                )
            );

        if (recordingCoroutine != null)
        {
            StopCoroutine(recordingCoroutine);
            recordingCoroutine = null;
        }

        if (!string.IsNullOrEmpty(
                microphoneDevice) &&
            Microphone.IsRecording(
                microphoneDevice))
        {
            Microphone.End(microphoneDevice);
        }

        microphoneDevice = null;

        if (wasRecording)
            Debug.Log("[마이크] 녹음 종료");
    }

    private IEnumerator RequestPermissionAndStart()
    {
        yield return RequestMicrophonePermission();

        if (HasMicrophonePermission())
        {
            StartRecording();
        }
        else
        {
            Debug.LogError(
                "[마이크] 마이크 권한이 허용되지 않았습니다."
            );
        }
    }

    private IEnumerator RecordLoop()
    {
        while (true)
        {
            AudioClip recordedClip =
                Microphone.Start(
                    microphoneDevice,
                    true,
                    chunkSeconds,
                    sampleRate
                );

            if (recordedClip == null)
            {
                Debug.LogError(
                    "[마이크] 녹음을 시작하지 못했습니다."
                );

                recordingCoroutine = null;
                yield break;
            }

            float startTimeout =
                Time.realtimeSinceStartup + 3f;

            // 마이크가 실제 녹음을 시작할 때까지 대기한다.
            while (
                Microphone.GetPosition(
                    microphoneDevice
                ) <= 0)
            {
                if (Time.realtimeSinceStartup >
                    startTimeout)
                {
                    Debug.LogError(
                        "[마이크] 마이크 시작 대기시간을 초과했습니다."
                    );

                    Microphone.End(
                        microphoneDevice
                    );

                    Destroy(recordedClip);

                    recordingCoroutine = null;
                    yield break;
                }

                yield return null;
            }

            float recordingStartedAt =
                Time.realtimeSinceStartup;

            // GetPosition이 마지막 샘플에 도착하기를
            // 기다리지 않고 실제 시간으로 녹음한다.
            while (
                Time.realtimeSinceStartup -
                recordingStartedAt <
                chunkSeconds)
            {
                if (!Microphone.IsRecording(
                        microphoneDevice))
                {
                    Debug.LogError(
                        "[마이크] 녹음이 예기치 않게 중단되었습니다."
                    );

                    break;
                }

                yield return null;
            }

            if (Microphone.IsRecording(
                    microphoneDevice))
            {
                Microphone.End(
                    microphoneDevice
                );
            }

            byte[] wavData;

            try
            {
                wavData =
                    WavEncoder.FromAudioClip(
                        recordedClip
                    );
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[마이크] WAV 변환 실패: " +
                    exception.Message
                );

                Destroy(recordedClip);

                // 다음 녹음 시작 전 한 프레임 대기한다.
                
                continue;
            }

            float duration =
                (float)recordedClip.samples /
                recordedClip.frequency;

            Destroy(recordedClip);

            if (printChunkLog)
            {
                Debug.Log(
                    "[마이크] 음성 조각 생성 완료" +
                    "\n길이: " +
                    duration.ToString("F2") +
                    "초" +
                    "\nWAV 크기: " +
                    wavData.Length +
                    " bytes"
                );
            }

            ChunkRecorded?.Invoke(
                wavData,
                duration
            );

            // 이전 마이크 녹음이 완전히 해제된 후
            // 다음 녹음을 시작하도록 한 프레임 대기한다.
            yield return null;
        }
    }

    private bool HasMicrophonePermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return Permission.HasUserAuthorizedPermission(
            Permission.Microphone
        );
#else
        return true;
#endif
    }

    private IEnumerator RequestMicrophonePermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!HasMicrophonePermission())
        {
            Permission.RequestUserPermission(
                Permission.Microphone
            );

            while (!HasMicrophonePermission())
                yield return null;
        }
#else
        yield return null;
#endif
    }

    private void OnDisable()
    {
        StopRecording();
    }

    private void OnDestroy()
    {
        StopRecording();
    }
}