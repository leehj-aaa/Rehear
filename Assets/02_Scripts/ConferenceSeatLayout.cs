using System;
using System.Linq;
using UnityEngine;

/// <summary>Physical conference table, chairs and seat anchors. Audience spawning is separate.</summary>
[DefaultExecutionOrder(-300)]
public sealed class ConferenceSeatLayout : MonoBehaviour
{
    public const int MinimumSeats = 6, MaximumSeats = 10;
    [SerializeField, Range(MinimumSeats, MaximumSeats)] private int seatCount = MinimumSeats;
    [SerializeField, Min(.6f)] private float chairSpacing = .85f;
    [SerializeField, Min(.25f)] private float tableEndMargin = .5f;
    [SerializeField] private Transform table;
    [SerializeField] private Transform[] chairs = Array.Empty<Transform>();
    [SerializeField] private AudienceSeat[] seatPool = Array.Empty<AudienceSeat>();
    [SerializeField] private Transform[] existingActors = Array.Empty<Transform>();
    [SerializeField] private AudienceSeating audienceSeating;
    [SerializeField] private GameObject[] audiencePrefabPool = Array.Empty<GameObject>();
    [SerializeField, HideInInspector] private Vector3[] originalChairs, originalSeats, actorOffsets;
    [SerializeField, HideInInspector] private Quaternion[] chairRotations, seatRotations, actorRotations;
    [SerializeField, HideInInspector] private Vector3 originalTableScale, tableCenter, fixedTableCenter;
    [SerializeField, HideInInspector] private float originalLength;
    [SerializeField, HideInInspector] private int lengthAxis;
    public int SeatCount => seatCount;
    public int ChairCapacity => seatCount == 6 ? 6 : seatCount <= 8 ? 8 : 10;
    public Transform Table => table;
    public Transform[] Chairs => chairs;
    public AudienceSeat[] SeatPool => seatPool;
    public GameObject[] AudiencePrefabPool => audiencePrefabPool;
    public float TableLength => originalLength * Mathf.Abs(table.localScale[lengthAxis] / originalTableScale[lengthAxis]);
    public AudienceSeat[] ActiveSeats => seatPool.Take(seatCount).ToArray();
    public event Action<int> LayoutChanged;

    private void Awake()
    {
        if (chairs.Length != MaximumSeats) return;
        SetSeatCount(seatCount);
        if (audienceSeating) audienceSeating.ConfigureLayout(ActiveSeats, audiencePrefabPool);
    }

    public void ConfigureAudience(AudienceSeating seating, GameObject[] prefabs)
    {
        if (prefabs.Length != MaximumSeats || prefabs.Any(p => !p))
            throw new ArgumentException("Ten audience prefabs are required.");
        audienceSeating = seating; audiencePrefabPool = prefabs;
    }

    public void Configure(Transform tableTransform, Transform[] chairPool, AudienceSeat[] anchors, Transform[] actors)
    {
        if (!tableTransform || chairPool.Length != MaximumSeats || anchors.Length != MaximumSeats ||
            chairPool.Any(c => !c) || anchors.Any(s => !s))
            throw new ArgumentException("A table and ten valid chairs and anchors are required.");
        table = tableTransform; chairs = chairPool; seatPool = anchors; existingActors = actors;
        originalTableScale = table.localScale;
        var bounds = table.GetComponent<Renderer>().bounds;
        fixedTableCenter = bounds.center;
        tableCenter = table.InverseTransformPoint(bounds.center);
        var mesh = table.GetComponent<MeshFilter>().sharedMesh.bounds;
        lengthAxis = Mathf.Abs(mesh.size.x * table.lossyScale.x) >= Mathf.Abs(mesh.size.z * table.lossyScale.z) ? 0 : 2;
        originalLength = mesh.size[lengthAxis] * Mathf.Abs(table.lossyScale[lengthAxis]);
        originalChairs = chairs.Select(c => c.position).ToArray(); chairRotations = chairs.Select(c => c.rotation).ToArray();
        originalSeats = seatPool.Select(s => s.transform.position).ToArray(); seatRotations = seatPool.Select(s => s.transform.rotation).ToArray();
        actorOffsets = new Vector3[MinimumSeats]; actorRotations = new Quaternion[MinimumSeats];
        for (int i = 0; i < MinimumSeats; i++) if (i < actors.Length && actors[i])
        {
            actorOffsets[i] = Quaternion.Inverse(seatRotations[i]) * (actors[i].position - originalSeats[i]);
            actorRotations[i] = Quaternion.Inverse(seatRotations[i]) * actors[i].rotation;
        }
    }

    public void SetSeatCount(int count)
    {
        if (count < MinimumSeats || count > MaximumSeats) throw new ArgumentOutOfRangeException(nameof(count), "Choose 6–10 seats.");
        if (!table || chairs.Length != MaximumSeats || seatPool.Length != MaximumSeats || chairs.Any(c => !c) || seatPool.Any(s => !s))
            throw new InvalidOperationException("Create the conference seat pool in the editor first.");
        seatCount = count;
        int perSide = (count + 1) / 2;
        float length = count == MinimumSeats ? originalLength : Mathf.Max(originalLength, (perSide - 1) * chairSpacing + 2 * tableEndMargin);
        var scale = originalTableScale; scale[lengthAxis] *= length / originalLength; table.localScale = scale;
        // Preserve the tabletop's center even if the imported mesh has an offset pivot.
        Vector3 beforeCenter = fixedTableCenter;
        Vector3 currentCenter = table.TransformPoint(tableCenter);
        table.position += beforeCenter - currentCenter;
        Vector3 center = table.TransformPoint(tableCenter);
        Vector3 axis = (lengthAxis == 0 ? table.right : table.forward); axis.y = 0; axis.Normalize();
        Vector3 across = Vector3.Cross(Vector3.up, axis);
        int[] left = SideOrder(ChairCapacity, true), right = SideOrder(ChairCapacity, false);
        for (int i = 0; i < MaximumSeats; i++)
        {
            // The final seat on each side is always the only conversation pair.
            seatPool[i].conversationPartner = null;
            bool active = i < count; chairs[i].gameObject.SetActive(i < ChairCapacity); seatPool[i].gameObject.SetActive(active);
            if (!active) seatPool[i].Assign(null, false);
            if (i >= ChairCapacity) continue;
            if (count == MinimumSeats)
            {
                chairs[i].SetPositionAndRotation(originalChairs[i], chairRotations[i]);
                seatPool[i].transform.SetPositionAndRotation(originalSeats[i], seatRotations[i]);
            }
            else
            {
                bool rear = i == 2 || i == 3;
                bool onLeft = Array.IndexOf(left, i) >= 0;
                var order = (onLeft ? left : right).Where(index => index != 2 && index != 3).ToArray();
                int slot = Array.IndexOf(order, i), source = i < MinimumSeats ? i : (onLeft ? 0 : 5);
                if (rear) source = i;
                float lateral = Vector3.Dot(originalChairs[source] - beforeCenter, across);
                float along = (slot - (order.Length - 1) * .5f) * chairSpacing;
                // Keep front-to-back ordering consistent with the authored six-seat layout.
                float direction = Vector3.Dot(originalChairs[2] - originalChairs[0], axis) >= 0 ? 1 : -1;
                if (rear) along = length * .5f + .45f;
                var position = center + across * lateral + axis * (along * direction); position.y = originalChairs[source].y;
                // Preserve the authored presentation-facing angle on each side.
                var facing = seatRotations[source];
                var chairRotation = facing * Quaternion.Inverse(seatRotations[source]) * chairRotations[source];
                chairs[i].SetPositionAndRotation(position, chairRotation);
                var offset = Quaternion.Inverse(chairRotations[source]) * (originalSeats[source] - originalChairs[source]);
                seatPool[i].transform.SetPositionAndRotation(position + chairRotation * offset, facing);
            }
            if (!Application.isPlaying && i < MinimumSeats && i < existingActors.Length && existingActors[i])
                existingActors[i].SetPositionAndRotation(seatPool[i].transform.position + seatPool[i].transform.rotation * actorOffsets[i], seatPool[i].transform.rotation * actorRotations[i]);
        }
        seatPool[2].conversationPartner = seatPool[3];
        seatPool[3].conversationPartner = seatPool[2];
        LayoutChanged?.Invoke(count);
    }

    public static int[] SideOrder(int count, bool left)
    {
        if (count < MinimumSeats || count > MaximumSeats) throw new ArgumentOutOfRangeException(nameof(count));
        return (left ? new[] { 0, 6, 1, 8, 2 } : new[] { 5, 7, 4, 9, 3 }).Where(i => i < count).ToArray();
    }
}
