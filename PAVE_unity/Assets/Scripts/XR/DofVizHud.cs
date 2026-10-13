using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Development aid. Puts the three DofViz bars (HOC / WFE / WPS) into a head-locked
/// HUD in front of the headset instead of floating above the prosthetic palm, and
/// lets you toggle them on and off without taking the headset off.
///
/// Drop this on the DofViz parent object. It does not modify the scene: it writes
/// into the children's FollowTracker components at runtime only, and caches their
/// serialised values on Awake so HandMounted mode restores exactly what the scene
/// has. Remove the component and everything is back to how it was.
///
/// F8  show / hide
/// F9  head-locked <-> hand-mounted
/// </summary>
[DisallowMultipleComponent]
public class DofVizHud : MonoBehaviour
{
    public enum VizMode
    {
        HeadLocked,   // bars follow the camera, fixed spot in your view
        HandMounted,  // the scene's original behaviour: bars follow the palm
    }

    [Header("Mode")]
    public VizMode mode = VizMode.HeadLocked;
    public bool visible = true;

    [Header("Head-locked layout")]
    [Tooltip("The headset camera. Left empty, Camera.main is used.")]
    public Transform head;

    [Tooltip("Metres in front of the eyes.")]
    public float distance = 0.6f;

    [Tooltip("Height of the topmost bar (HOC) relative to the eye line. Negative is below.")]
    public float topOffset = -0.10f;

    [Tooltip("Vertical gap between bars, in metres.")]
    public float spacing = 0.06f;

    [Header("Hotkeys")]
    public KeyCode toggleVisibilityKey = KeyCode.F8;
    public KeyCode toggleModeKey = KeyCode.F9;

    // the three children, ordered HOC, WFE, WPS
    FollowTracker[] _trackers;
    Canvas[] _canvases;

    // their values as the scene serialised them, so HandMounted is lossless
    GameObject[] _origLeading;
    Vector3[] _origOffsetPos;
    bool[] _origOffsetInGlobal;
    bool[] _origFaceCamera;

    // so we only write when something actually changes
    VizMode _appliedMode;
    bool _appliedVisible;
    float _appliedDistance, _appliedTopOffset, _appliedSpacing;
    bool _applied;

    void Awake()
    {
        // order by DOA rather than sibling index, so the stack is always HOC/WFE/WPS
        var vizzes = GetComponentsInChildren<DofDiffViz>(true)
            .Where(v => v.GetComponent<FollowTracker>() != null)
            .OrderBy(v => (int)v.doa)
            .ToArray();

        _trackers = vizzes.Select(v => v.GetComponent<FollowTracker>()).ToArray();
        _canvases = vizzes.Select(v => v.GetComponentInChildren<Canvas>(true)).ToArray();

        int n = _trackers.Length;
        _origLeading = new GameObject[n];
        _origOffsetPos = new Vector3[n];
        _origOffsetInGlobal = new bool[n];
        _origFaceCamera = new bool[n];

        for (int i = 0; i < n; i++)
        {
            _origLeading[i] = _trackers[i].leadingObject;
            _origOffsetPos[i] = _trackers[i].offsetPos_loc;
            _origOffsetInGlobal[i] = _trackers[i].offsetInGlobal;
            _origFaceCamera[i] = _trackers[i].faceCamera;
        }

        if (head == null && Camera.main != null)
            head = Camera.main.transform;

        if (n == 0)
            Debug.LogWarning("[DofVizHud] No DofDiffViz children with a FollowTracker found.", this);
    }

    void Start()
    {
        Apply(force: true);
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleVisibilityKey))
            visible = !visible;

        if (Input.GetKeyDown(toggleModeKey))
            mode = mode == VizMode.HeadLocked ? VizMode.HandMounted : VizMode.HeadLocked;

        Apply();
    }

    /// <summary>Pushes the current settings into the children, if anything changed.</summary>
    void Apply(bool force = false)
    {
        bool layoutChanged = !_applied
            || mode != _appliedMode
            || !Mathf.Approximately(distance, _appliedDistance)
            || !Mathf.Approximately(topOffset, _appliedTopOffset)
            || !Mathf.Approximately(spacing, _appliedSpacing);

        if (force || layoutChanged)
        {
            for (int i = 0; i < _trackers.Length; i++)
            {
                var ft = _trackers[i];
                if (ft == null) continue;

                if (mode == VizMode.HeadLocked)
                {
                    // offsetPos_loc is applied in the leader's local space when
                    // offsetInGlobal is false, so +Z is straight ahead of the head.
                    ft.leadingObject = head != null ? head.gameObject : null;
                    ft.offsetPos_loc = new Vector3(0f, topOffset - i * spacing, distance);
                    ft.offsetInGlobal = false;

                    // Must be false. FollowTracker computes
                    //   direction = Camera.main.position - leadingObject.position
                    // and if the leader IS the camera that is the zero vector, which
                    // makes Quaternion.LookRotation undefined. The rotation already
                    // tracks the head via leadingObject.rotation, so it isn't needed.
                    ft.faceCamera = false;
                }
                else
                {
                    ft.leadingObject = _origLeading[i];
                    ft.offsetPos_loc = _origOffsetPos[i];
                    ft.offsetInGlobal = _origOffsetInGlobal[i];
                    ft.faceCamera = _origFaceCamera[i];
                }
            }

            _appliedMode = mode;
            _appliedDistance = distance;
            _appliedTopOffset = topOffset;
            _appliedSpacing = spacing;
        }

        if (force || visible != _appliedVisible)
        {
            // Disable the Canvas, not the GameObject, so FollowTracker keeps updating
            // while hidden. Otherwise the bars visibly fly in from wherever they were
            // parked when you switch them back on.
            foreach (var c in _canvases)
                if (c != null) c.enabled = visible;

            _appliedVisible = visible;
        }

        _applied = true;
    }
}
