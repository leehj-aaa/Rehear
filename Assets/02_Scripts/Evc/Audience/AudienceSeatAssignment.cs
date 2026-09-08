using System;
using UnityEngine;

public sealed class AudienceSeatAssignment : MonoBehaviour
{
    [SerializeField] private AudienceSeat seat;
    public AudienceSeat Seat => seat;
    public void Assign(AudienceSeat value) => seat = value;
    public bool Allows(string action)
    {
        if (string.IsNullOrWhiteSpace(action)) return true;
        string id = action.ToLowerInvariant();
        if (id.StartsWith("act_08", StringComparison.Ordinal) || id.Contains("side_conversation"))
            return seat && seat.row == "rear" && seat.conversationPartner && seat.conversationPartner.Occupant;
        if (id.StartsWith("act_01", StringComparison.Ordinal) || id.Contains("laptoptyping"))
            return seat && seat.HasLaptop;
        return true;
    }
}
