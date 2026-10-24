using System;
using System.Collections.Generic;
using System.Linq;
using Rehear.Evc.Audience;
using Rehear.Evc.Contracts;
using UnityEngine;

[DefaultExecutionOrder(-200)]
public sealed class AudienceSeating : MonoBehaviour
{
    public AudienceAgent[] members;
    public GameObject[] audiencePrefabs;
    public AudienceActionRegistry[] actionRegistries;
    public float[] seatedHeightOffsets;
    public AudienceSeat[] seats;
    public GameObject[] laptops;
    public GameObject[] laptopPrefabs = Array.Empty<GameObject>();
    [Range(0, 10)] public int initialLaptopCount = 2;
    private System.Random laptopRandom;
    private readonly List<GameObject> laptopPool = new List<GameObject>();
    public IReadOnlyList<GameObject> SpawnedLaptops => laptopPool;
    private readonly Dictionary<AudienceAgent, float> heightOffsets = new Dictionary<AudienceAgent, float>();
    private bool initialized;

#if UNITY_EDITOR
    public void RefreshEditorPreview(AudienceSeat[] activeSeats, GameObject[] prefabPool, int seed)
    {
        if (Application.isPlaying) return;
        UnityEditor.Undo.RecordObject(this, "Preview seated audience");
        foreach (var child in transform.Cast<Transform>().ToArray())
            if ((laptopPrefabs ?? Array.Empty<GameObject>()).Any(p => p && child.name == p.name))
                UnityEditor.Undo.DestroyObjectImmediate(child.gameObject);
        laptopPool.Clear(); heightOffsets.Clear(); initialized = false; laptopRandom = null;
        ConfigureLayout(activeSeats, prefabPool); Initialize(seed);
        foreach (var actor in members) actor.gameObject.SendMessage("SampleEditorSeatedPose", SendMessageOptions.DontRequireReceiver);
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif

    private GameObject SpawnPrefab(GameObject prefab)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            var preview = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, transform);
            UnityEditor.Undo.RegisterCreatedObjectUndo(preview, "Place audience preview");
            return preview;
        }
#endif
        return Instantiate(prefab, transform);
    }

    public void ConfigureLayout(AudienceSeat[] activeSeats, GameObject[] prefabPool)
    {
        if (initialized) throw new InvalidOperationException("Choose the room capacity before initializing the audience.");
        seats = activeSeats;
        audiencePrefabs = prefabPool.Take(activeSeats.Length).ToArray();
    }

    private void Awake() => Initialize(Environment.TickCount);
    public void Initialize(int seed)
    {
        if (initialized && laptopRandom != null) return;
        int count = seats?.Length ?? 0;
        if (count < 6 || count > 10) throw new InvalidOperationException("Configure six to ten audience seats.");
        if(audiencePrefabs != null && audiencePrefabs.Length == count)
        {
            members=new AudienceAgent[count];
            for(int i=0;i<count;i++)
            {
                // Reuse an already seated actor, including after an Editor domain reload.
                var existing = GetComponentsInChildren<AudienceAgent>(true)
                    .FirstOrDefault(a => a.gameObject.name == audiencePrefabs[i].name);
                var instance=existing ? existing.gameObject : SpawnPrefab(audiencePrefabs[i]);
                instance.name=audiencePrefabs[i].name;
                var agent=instance.GetComponent<AudienceAgent>();
                if(!agent) agent=instance.AddComponent<AudienceAgent>();
                agent.Configure("audience_" + (i + 1).ToString("00"), actionRegistries != null && i < actionRegistries.Length ? actionRegistries[i] : agent.ActionRegistry);
                members[i]=agent;
            }
            foreach (var actor in GetComponentsInChildren<AudienceAgent>(true)) actor.gameObject.SetActive(members.Contains(actor));
        }
        if (members == null || members.Length != count ||
            members.Any(m=>!m) || seats.Any(s=>!s) || seats.Select(s=>s.SeatId).Distinct().Count()!=count)
            throw new InvalidOperationException("Unique audience seats and members must be configured.");
        initialized = true;
        laptopRandom = new System.Random(seed ^ 0x4c4150);
        if (laptopPrefabs != null && laptopPrefabs.Length > 0)
        {
            if (laptopPrefabs.Any(p => !p)) throw new InvalidOperationException("Missing laptop prefab.");
            foreach (var legacy in laptops ?? Array.Empty<GameObject>()) if (legacy) legacy.SetActive(false);
            for (int i = 0; i < Mathf.Clamp(initialLaptopCount, 0, count); i++) laptopPool.Add(CreateLaptop());
        }
        else laptopPool.AddRange((laptops ?? Array.Empty<GameObject>()).Where(l=>l));
        float floor = seats.Min(s=>s.transform.position.y);
        for(int i=0;i<members.Length;i++) heightOffsets[members[i]] = seatedHeightOffsets != null && i < seatedHeightOffsets.Length ? seatedHeightOffsets[i] : members[i].transform.position.y-floor;
        var order = members.ToArray();
        var random = new System.Random(seed);
        for(int i=order.Length-1;i>0;i--) { int j=random.Next(i+1); (order[i],order[j])=(order[j],order[i]); }
        // IDs belong to session participants, not a particular visual prefab.
        // Shuffle their visual identities before command coordinators initialize.
        var ids=members.Select(m=>m.AgentId).OrderBy(id=>id,StringComparer.Ordinal).ToArray();
        if(ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct().Count()!=count)
            throw new InvalidOperationException("Unique audience IDs are required.");
        for(int i=0;i<order.Length;i++) order[i].Configure(ids[i],order[i].ActionRegistry);
        var laptopSeats = Enumerable.Range(0,count).ToArray();
        for(int i=count-1;i>0;i--) { int j=random.Next(i+1); (laptopSeats[i],laptopSeats[j])=(laptopSeats[j],laptopSeats[i]); }
        var equipped = new HashSet<int>(laptopSeats.Take(Math.Min(laptopPool.Count,count)));
        Apply(order, Enumerable.Range(0,count).Select(i=>equipped.Contains(i)).ToArray());
        foreach(var coordinator in FindObjectsByType<AudienceReactionCoordinator>(FindObjectsSortMode.None))
            if(coordinator.gameObject.scene==gameObject.scene) coordinator.ConfigureAgents(members);
    }

    public void ApplyServerProfiles(AudienceDto[] audiences)
    {
        // Older responses without profiles keep the provisional local arrangement.
        if (audiences == null || audiences.All(a=>a?.profile == null)) return;
        var order = new AudienceAgent[seats.Length];
        var equipped = new bool[seats.Length];
        foreach(var a in audiences)
        {
            if(a?.profile == null) throw new InvalidOperationException("Incomplete audience seating profiles.");
            int index=Array.FindIndex(seats,s=>s.row==a.profile.row && s.side==a.profile.seat);
            var actor=members.SingleOrDefault(m=>m.AgentId==a.AgentId);
            if(index<0 || order[index] || !actor || order.Contains(actor)) throw new InvalidOperationException("Invalid or duplicate audience seat.");
            order[index]=actor; equipped[index]=a.profile.has_laptop;
        }
        // A legacy six-person response may rearrange its six participants while
        // additional local participants retain their seats wherever possible.
        if (audiences.Length != seats.Length && !(seats.Length > 6 && audiences.Length == 6))
            throw new InvalidOperationException("Missing audience seat assignment.");
        var remaining = members.Where(m => !order.Contains(m)).ToList();
        for (int i = 0; i < order.Length; i++) if (!order[i])
        {
            var current = seats[i].Occupant ? seats[i].Occupant.GetComponent<AudienceAgent>() : null;
            var actor = current && remaining.Contains(current) ? current : remaining.First();
            order[i] = actor; remaining.Remove(actor); equipped[i] = seats[i].HasLaptop;
        }
        Apply(order,equipped);
    }

    private void Apply(AudienceAgent[] order, bool[] equipped)
    {
        int needed=equipped.Count(v=>v);
        if(needed>0 && laptopPool.Count==0 && (laptopPrefabs == null || laptopPrefabs.Length == 0)) throw new InvalidOperationException("No laptop model configured.");
        while(laptopPool.Count<needed) laptopPool.Add(CreateLaptop());
        foreach(var laptop in laptopPool) laptop.SetActive(false);
        int laptopIndex=0;
        for(int i=0;i<seats.Length;i++)
        {
            var seat=seats[i]; var actor=order[i];
            var assignment=actor.GetComponent<AudienceSeatAssignment>();
            if(!assignment) assignment=actor.gameObject.AddComponent<AudienceSeatAssignment>();
            assignment.Assign(seat); seat.Assign(assignment,equipped[i]);
            var pose=actor.GetComponent<AudienceSeatedPose>();
            var rotation=seat.transform.rotation * (pose ? pose.facingCorrection : Quaternion.identity);
            var position=seat.transform.position;
            if(pose) position-=rotation * Vector3.Scale(pose.localHip,actor.transform.localScale);
            else position.y+=heightOffsets[actor];
            actor.transform.SetPositionAndRotation(position,rotation);
            if(equipped[i])
            {
                var laptop=laptopPool[laptopIndex++];
                laptop.transform.SetPositionAndRotation(seat.laptopAnchor.position,seat.laptopAnchor.rotation);
                laptop.SetActive(true);
            }
        }
    }

    private GameObject CreateLaptop()
    {
        var prefab = laptopPrefabs != null && laptopPrefabs.Length > 0
            ? laptopPrefabs[laptopRandom.Next(laptopPrefabs.Length)]
            : laptopPool[0];
        var instance = SpawnPrefab(prefab);
        instance.name = prefab.name;
        instance.SetActive(false);
        return instance;
    }
}
