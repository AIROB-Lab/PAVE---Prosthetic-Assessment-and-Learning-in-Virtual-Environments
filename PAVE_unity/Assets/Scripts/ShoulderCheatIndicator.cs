using UnityEngine;

public class ShoulderCheatIndicator : MonoBehaviour
{
    [Header("References")]
    public Transform actualArrow;      // arrow on the real hand (hannes_..._MG)
    public Transform ghostArrow;       // arrow on the ghost/target hand
    public Renderer cylinderRenderer;  // this cylinder's renderer

    [Header("Settings")]
    public float toleranceDegrees = 15f;

    [Header("Debug")]
    public bool logDebug = true;

    void Update()
    {
        if (actualArrow == null || ghostArrow == null)
            return;

        // Green only when the real hand's local X axis aligns with the ghost's local X axis
        float deviationAngle = Vector3.Angle(actualArrow.right, ghostArrow.right);
        bool good = deviationAngle <= toleranceDegrees;

        if (cylinderRenderer != null)
            cylinderRenderer.material.color = good ? Color.green : Color.red;

        if (logDebug)
            Debug.Log($"[{name}] deviationAngle={deviationAngle:F1}, good={good}");
    }
}