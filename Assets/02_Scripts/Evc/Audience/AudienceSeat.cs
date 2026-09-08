using UnityEngine;

public sealed class AudienceSeat : MonoBehaviour
{
    public string row;
    public string side;
    public Transform laptopAnchor;
    public AudienceSeat conversationPartner;
    [SerializeField] private AudienceSeatAssignment occupant;
    [SerializeField] private bool hasLaptop;
    public string SeatId => row + "_" + side;
    public AudienceSeatAssignment Occupant => occupant;
    public bool HasLaptop => hasLaptop;
    public void Assign(AudienceSeatAssignment value, bool laptop) { occupant = value; hasLaptop = laptop; }
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = row == "rear" ? Color.cyan : Color.green;
        Gizmos.DrawWireSphere(transform.position + Vector3.up * .5f, .12f);
        if (laptopAnchor) Gizmos.DrawWireCube(laptopAnchor.position, new Vector3(.32f,.03f,.23f));
#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * .7f, SeatId + (occupant ? " / " + occupant.name : ""));
#endif
    }
}
