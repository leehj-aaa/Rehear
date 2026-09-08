using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(XRSimpleInteractable))]
public sealed class Scene03ReturnButton : MonoBehaviour
{
    [SerializeField]
    private Scene03Manager manager;

    private XRSimpleInteractable interactable;

    private void OnEnable()
    {
        interactable = GetComponent<XRSimpleInteractable>();
        interactable.selectEntered.AddListener(ReturnToStart);
    }

    private void OnDisable()
    {
        if (interactable != null)
            interactable.selectEntered.RemoveListener(ReturnToStart);
    }

    private void ReturnToStart(SelectEnterEventArgs _)
    {
        if (manager != null)
            manager.ReturnToStart();
    }
}
