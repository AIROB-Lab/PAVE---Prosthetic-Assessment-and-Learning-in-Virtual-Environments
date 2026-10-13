using System.Collections.Generic;
using UnityEngine;

/// Rotates the ghost hand's 3D arrow 45 degrees anticlockwise within the face of the
/// CylinderArrow disc it sits on, but only when the ghost is at a top shelf.
/// Put this on "3D RightArrow".
/// Shelf height is read from the live shelf objects, so it still works when
/// TierHeightController changes the heights for a different participant.
[DisallowMultipleComponent]
public class ArrowTiltAtTop : MonoBehaviour
{
    public bool tilt = true;

    [Tooltip("Degrees around the disc's own axis. Flip the sign if it turns the wrong way.")]
    public float tiltDegrees = -45f;

    Quaternion baseLocal;
    readonly List<Transform> shelves = new List<Transform>();

    void Start()
    {
        baseLocal = transform.localRotation;
        foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name.StartsWith("level_")) shelves.Add(t);
    }

    // LateUpdate, so this wins against whatever drives the forearm each frame.
    void LateUpdate()
    {
        // Pre-multiplying in local space turns the arrow about its PARENT's Y axis,
        // which is the disc's normal. So it stays flat on the face.
        transform.localRotation = (tilt && NearestShelfIsTop())
            ? Quaternion.Euler(0f, tiltDegrees, 0f) * baseLocal
            : baseLocal;
    }

    bool NearestShelfIsTop()
    {
        Transform nearest = null;
        float best = float.MaxValue;
        foreach (var s in shelves)
        {
            float d = (s.position - transform.position).sqrMagnitude;
            if (d < best) { best = d; nearest = s; }
        }
        return nearest != null && nearest.name == "level_top";
    }
}