using TMPro;
using UnityEngine;

/// <summary>Displays the study-specific gaze regions evaluated by the acquisition adapter.</summary>
[RequireComponent(typeof(TextMeshProUGUI))]
public class ROITextUpdate : MonoBehaviour
{
    TextMeshProUGUI label;
    void Awake()
    {
        label = GetComponent<TextMeshProUGUI>();
    }

    void Update()
    {
        label.text = $"ROI: Hand - {PaveTracking.LookingAtHand}, Object - {PaveTracking.LookingAtObject}, Target - {PaveTracking.LookingAtTarget}";
    }
}
