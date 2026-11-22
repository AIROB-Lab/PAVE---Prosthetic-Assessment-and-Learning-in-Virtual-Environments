using UnityEngine;

/// <summary>Inspector-driven input for checking existing scenes without EMG or networking.</summary>
public class ManualHandCommandSource : HandCommandSource
{
    [Range(-1, 1)]
    public float handClosing;
    [Range(-1, 1)]
    public float wristFlexion;
    [Range(-1, 1)]
    public float wristPronation;
    public override bool TryRead(DOA axis, out float value, out double timestampSeconds)
    {
        timestampSeconds = Sim.SimClock.Now;
        switch (axis)
        {
            case DOA.HOC:
                value = handClosing;
                return true;
            case DOA.WFE:
                value = wristFlexion;
                return true;
            case DOA.WPS:
                value = wristPronation;
                return true;
            default:
                value = 0;
                return false;
        }
    }
}
