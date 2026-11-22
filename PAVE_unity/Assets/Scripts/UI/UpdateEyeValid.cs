using TMPro;
using UnityEngine;

/// <summary>Reports missing gaze honestly; combined OpenXR gaze has no per-eye validity flags.</summary>
[RequireComponent(typeof(TextMeshProUGUI))]
public class UpdateEyeValid : MonoBehaviour
{
    TextMeshProUGUI label;
    PaveTracking tracking;
    void Awake()
    {
        label = GetComponent<TextMeshProUGUI>();
        tracking = FindFirstObjectByType<PaveTracking>();
    }
    void Update()
    {
        string left = tracking != null ? tracking.LeftEyeValidity?.ToString() ?? "n/a" : "n/a";
        string right = tracking != null ? tracking.RightEyeValidity?.ToString() ?? "n/a" : "n/a";
        label.text = $"Gaze: {PaveTracking.EyeStatus} ({PaveTracking.ActiveGazeSource}) | Left: {left}, Right: {right}";
    }
}
