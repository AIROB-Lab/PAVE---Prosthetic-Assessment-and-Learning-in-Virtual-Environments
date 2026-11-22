using UnityEngine;
/// <summary>Normalized hand input in [-1,1]. False means hold the preceding command.</summary>
public abstract class HandCommandSource : MonoBehaviour
{
    public abstract bool TryRead(DOA axis, out float value, out double timestampSeconds);
}
