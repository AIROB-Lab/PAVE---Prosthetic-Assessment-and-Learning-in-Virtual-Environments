using UnityEngine;

public class ShoulderCheatIndicator : MonoBehaviour
{
    [Header("References")]
    public Transform arrow;            // the arrow object
    public Renderer cylinderRenderer;  // the cylinder's renderer

    [Header("Settings")]
    public float toleranceDegrees = 15f;

    [Header("Debug")]
    public bool logDebug = true;

    void Update()
    {
        // Angle between the arrow's "up" direction and world up
        float deviationAngle = Vector3.Angle(arrow.up, Vector3.up);

        bool good = deviationAngle <= toleranceDegrees;

        if (cylinderRenderer != null)
            cylinderRenderer.material.color = good ? Color.green : Color.red;

        if (logDebug)
            Debug.Log($"deviationAngle={deviationAngle:F1}, good={good}");
    }
}