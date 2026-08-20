using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class GlobalButtonSound : MonoBehaviour
{
    public static GlobalButtonSound Instance { get; private set; }

    [SerializeField] private AudioClip clickSound;
    [SerializeField, Range(0f, 1f)] private float volume = 0.7f;

    private AudioSource audioSource;
    private readonly HashSet<int> registeredObjects = new();

    // Button과 XR Simple Interactable이 동시에 실행될 때 중복음을 방지
    private float lastPlayTime = -1f;
    private const float DuplicateBlockTime = 0.05f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 0f;

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Start()
    {
        RegisterAllButtons();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        registeredObjects.Clear();
        StartCoroutine(RegisterAfterSceneLoaded());
    }

    private IEnumerator RegisterAfterSceneLoaded()
    {
        // 씬의 UI가 모두 초기화될 때까지 한 프레임 기다림
        yield return null;
        RegisterAllButtons();
    }

    private void RegisterAllButtons()
    {
        Button[] buttons = FindObjectsByType<Button>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        foreach (Button button in buttons)
        {
            int id = button.GetInstanceID();

            if (registeredObjects.Add(id))
                button.onClick.AddListener(PlayClickSound);
        }

        XRSimpleInteractable[] xrButtons =
            FindObjectsByType<XRSimpleInteractable>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

        foreach (XRSimpleInteractable xrButton in xrButtons)
        {
            int id = xrButton.GetInstanceID();

            if (registeredObjects.Add(id))
                xrButton.selectEntered.AddListener(_ => PlayClickSound());
        }
    }

    public void PlayClickSound()
    {
        if (clickSound == null || audioSource == null)
            return;

        if (Time.unscaledTime - lastPlayTime < DuplicateBlockTime)
            return;

        lastPlayTime = Time.unscaledTime;
        audioSource.PlayOneShot(clickSound, volume);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            SceneManager.sceneLoaded -= OnSceneLoaded;
    }
}