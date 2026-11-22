using System;
using System.Collections.Generic;
using Mujoco;
using UnityEngine;

[Serializable]
public enum DOA
{
    HOC, // hand open-close
    WFE, // Wrist flexion-extension
    WPS, // Wrist pronation/supination
    WUD, // Wrist ulnar deviation

    th_flex,
    th_rot,
    index,
    middle,
    ring,
    little,
    wr_ulnar,
    wr_radial,
}

[Serializable]
public struct DOA_mj
{
    public DOA_struct General;
    public GameObject[] mj_actuations;
}


[Serializable]
public struct DOA_struct
{
    public DOA doa;
    public byte UDPSubCategory;
    [Tooltip("Legacy study metadata. Use SetAxisRestricted for an explicit actuation restriction.")]
    public bool active;
    public bool EMG_override;
    // Normalized command; not a measurement of physical joint configuration.
    public float current_value;
    // SIM input range and normalized output range; both must have nonzero width.
    public Vector2 mapping_in;
    public Vector2 mapping_out;
}


/// <summary>A legacy mocap axis is kinematic, not a force-driven MuJoCo joint.</summary>
[Serializable]
public struct DOA_mocap
{
    public DOA_struct General;
    public GameObject pointOfAttachGO;
    public Vector3 localAxis;
    public Vector2 rangeDeg;
}

/// <summary>Shared prosthesis actuation. Input and task rules are separate components.
/// Existing field names are retained to preserve serialized FE/LR scene configurations.</summary>
public class HandController : MonoBehaviour
{
    public string Name;
    public GameObject hand;
    public byte UDPCategory = 1;
    public DOA_mj[] DOA_mujoco = Array.Empty<DOA_mj>();
    public DOA_mocap[] DOA_mocap = Array.Empty<DOA_mocap>();
    [SerializeField, Min(0)] float DelayInSeconds;
    [Min(0)] public int DelayInSamples;
    [Tooltip("Optional manual/replay input. Empty uses the PAVE SIM adapter.")]
    public HandCommandSource commandSource;
    [Tooltip("Maximum receipt age in seconds. Stale input holds the last command.")]
    [Min(0.01f)] public float commandTimeoutSeconds = 0.5f;
    [NonSerialized] public double usedUdpTs = -1;
    public bool InputIsFresh { get; private set; }
    MjScene scene;
    readonly Dictionary<GameObject, MjActuator> actuators = new Dictionary<GameObject, MjActuator>();
    readonly HashSet<DOA> restrictedAxes = new HashSet<DOA>();

    protected virtual void OnEnable()
    {
        // Apply immediately before MuJoCo's step, regardless of rendering frame rate.
        scene = MjScene.Instance;
        scene.preUpdateEvent += BeforePhysics;
    }
    protected virtual void OnDisable()
    {
        if (scene != null) scene.preUpdateEvent -= BeforePhysics;
        restrictedAxes.Clear();
    }
    void BeforePhysics(object sender, MjStepArgs args)
    {
        InputIsFresh = true;
        usedUdpTs = -1;
        DOA_mujoco = GetUdpValues(DOA_mujoco, DelayInSamples, DelayInSeconds);
        for (int i = 0; i < DOA_mocap.Length; i++)
        {
            var binding = DOA_mocap[i];
            if (binding.General.EMG_override && TryRead(binding.General, out float value, out _))
                DOA_mocap[i].General.current_value = value;
        }
        foreach (var binding in DOA_mujoco)
        {
            float value = restrictedAxes.Contains(binding.General.doa) ? 0 : Mathf.Clamp(binding.General.current_value, -1, 1);
            foreach (var go in binding.mj_actuations ?? Array.Empty<GameObject>())
            {
                if (go == null) continue;
                if (!actuators.TryGetValue(go, out var actuator))
                {
                    actuator = go.GetComponent<MjActuator>();
                    actuators[go] = actuator;
                }
                if (actuator == null) continue;
                actuator.Control = value >= 0 ? value * actuator.CommonParams.CtrlRange.y : -value * actuator.CommonParams.CtrlRange.x;
            }
        }
        foreach (var binding in DOA_mocap)
        {
            if (binding.pointOfAttachGO == null) continue;
            float value = restrictedAxes.Contains(binding.General.doa) ? 0 : Mathf.Clamp(binding.General.current_value, -1, 1);
            float angleDeg = value >= 0 ? value * binding.rangeDeg.y : -value * binding.rangeDeg.x;
            binding.pointOfAttachGO.transform.localRotation = Quaternion.AngleAxis(angleDeg, binding.localAxis);
        }
    }

    bool TryRead(DOA_struct axis, out float value, out double timestamp, int samples = -1, float seconds = -1)
    {
        value = axis.current_value; timestamp = -1;
        bool fresh;
        if (commandSource != null)
            fresh = commandSource.TryRead(axis.doa, out value, out timestamp);
        else
        {
            var packet = StreamlinedInputManager.udpReceiver.History.Get(UDPCategory, axis.UDPSubCategory,
                samples < 0 ? DelayInSamples : samples, seconds < 0 ? DelayInSeconds : seconds);
            var newest = StreamlinedInputManager.udpReceiver.History.Get(UDPCategory, axis.UDPSubCategory);
            fresh = packet != null && newest != null && Sim.SimClock.Now - newest.ReceivedAtSeconds <= commandTimeoutSeconds;
            if (fresh) { value = Convert.ToSingle(packet.Values[0]); timestamp = packet.TimestampSeconds; }
        }
        if (!fresh || float.IsNaN(value) || float.IsInfinity(value)) { InputIsFresh = false; return false; }
        if (commandSource == null) value = RemapDOA(value, axis);
        value = Mathf.Clamp(value, -1, 1);
        return true;
    }

    /// <summary>Legacy entry point. Missing/stale data holds the preceding value.</summary>
    public DOA_mj[] GetUdpValues(DOA_mj[] axes, int samplesBack = 0, float timeBack = 0, bool ignoreEMGState = false)
    {
        for (int i = 0; i < axes.Length; i++)
        {
            if (!axes[i].General.EMG_override && !ignoreEMGState) continue;
            if (TryRead(axes[i].General, out float value, out double time, samplesBack, timeBack))
            { axes[i].General.current_value = value; usedUdpTs = Math.Max(usedUdpTs, time); }
        }
        return axes;
    }
    public void OverwriteCurrVal(DOA doa, float val, bool deactivateEMG = true)
    {
        if (float.IsNaN(val) || float.IsInfinity(val)) throw new ArgumentException("Command must be finite.");
        for (int i = 0; i < DOA_mujoco.Length; i++) if (DOA_mujoco[i].General.doa == doa)
        { DOA_mujoco[i].General.current_value = Mathf.Clamp(val, -1, 1); if (deactivateEMG) DOA_mujoco[i].General.EMG_override = false; }
        for (int i = 0; i < DOA_mocap.Length; i++) if (DOA_mocap[i].General.doa == doa)
        { DOA_mocap[i].General.current_value = Mathf.Clamp(val, -1, 1); if (deactivateEMG) DOA_mocap[i].General.EMG_override = false; }
    }
    public void ActivateEMGOverwrite(DOA doa, bool activate = true)
    {
        for (int i = 0; i < DOA_mujoco.Length; i++) if (DOA_mujoco[i].General.doa == doa) DOA_mujoco[i].General.EMG_override = activate;
        for (int i = 0; i < DOA_mocap.Length; i++) if (DOA_mocap[i].General.doa == doa) DOA_mocap[i].General.EMG_override = activate;
    }
    public void SetAxisRestricted(DOA axis, bool restricted)
    { if (restricted) restrictedAxes.Add(axis); else restrictedAxes.Remove(axis); }
    public void SetDelay(int DelayInSamples = 0, float DelayInSeconds = 0)
    {
        if (DelayInSamples < 0 || float.IsNaN(DelayInSeconds) || float.IsInfinity(DelayInSeconds) || DelayInSeconds < 0 || (DelayInSamples != 0 && DelayInSeconds != 0)) throw new ArgumentException("Choose one nonnegative delay.");
        this.DelayInSamples = DelayInSamples; this.DelayInSeconds = DelayInSeconds;
    }
    public void ResetDelay() => SetDelay();
    /// <summary>Command value, NOT measured joint angle. Retained for legacy feedback contracts.</summary>
    public float? GetValueForDOA(DOA key, bool RemappedToInRg = false)
    {
        foreach (var axis in DOA_mujoco) if (axis.General.doa == key)
            return RemappedToInRg ? RemapDOA(axis.General.current_value, axis.General, true) : axis.General.current_value;
        foreach (var axis in DOA_mocap) if (axis.General.doa == key)
            return RemappedToInRg ? RemapDOA(axis.General.current_value, axis.General, true) : axis.General.current_value;
        return null;
    }
    public float RemapDOA(float value, DOA_struct doaStruct, bool backward = false)
        => backward ? value.Remap(doaStruct.mapping_out.x, doaStruct.mapping_out.y, doaStruct.mapping_in.x, doaStruct.mapping_in.y)
                    : value.Remap(doaStruct.mapping_in.x, doaStruct.mapping_in.y, doaStruct.mapping_out.x, doaStruct.mapping_out.y);
}

public static class ExtensionMethods
{
    /// <summary>Linear mapping; a zero-width input range is invalid configuration.</summary>
    public static float Remap(this float value, float from1, float to1, float from2, float to2)
    {
        if (Mathf.Approximately(from1, to1)) throw new ArgumentException("Mapping range must have nonzero width.");
        return (value - from1) / (to1 - from1) * (to2 - from2) + from2;
    }
}
