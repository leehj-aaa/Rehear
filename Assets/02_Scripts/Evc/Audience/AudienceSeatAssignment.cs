using System;
using UnityEngine;

public sealed class AudienceSeatAssignment : MonoBehaviour
{
    [SerializeField] private AudienceSeat seat;
    public AudienceSeat Seat => seat;
    public void Assign(AudienceSeat value) => seat = value;
    public static bool IsSideConversation(string action)
    {
        if (string.IsNullOrWhiteSpace(action)) return false;
        string id = action.Trim().ToLowerInvariant().Replace("_", "").Replace(" ", "").Replace("-", "");
        return id.StartsWith("act08", StringComparison.Ordinal) || id.Contains("sideconversation");
    }

    public bool TryGetConversationVariation(out string variation)
    {
        variation = null;
        if (!seat || seat.row != "rear" || seat.Occupant != this) return false;
        var partner = seat.conversationPartner;
        if (!partner || partner == seat || partner.row != "rear" || partner.side == seat.side ||
            partner.conversationPartner != seat || !partner.Occupant ||
            partner.Occupant.Seat != partner || !partner.Occupant.gameObject.activeInHierarchy) return false;
        // Seat facing is stable even when the character is currently turning to a laptop.
        float direction = Vector3.Dot(partner.transform.position - seat.transform.position, seat.transform.right);
        if (Mathf.Abs(direction) < .01f) return false;
        variation = direction > 0 ? "ACT_08.side_conversation_r" : "ACT_08.side_conversation_l";
        return true;
    }

    public bool Allows(string action)
    {
        if (string.IsNullOrWhiteSpace(action)) return true;
        string id = action.ToLowerInvariant();
        if (IsSideConversation(action))
            return TryGetConversationVariation(out _);
        if (id.StartsWith("act_01", StringComparison.Ordinal) || id.Contains("laptoptyping"))
            return seat && seat.HasLaptop;
        return true;
    }
}
