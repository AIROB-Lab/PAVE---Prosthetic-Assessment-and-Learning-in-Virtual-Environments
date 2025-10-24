using System;
using System.Collections;
using System.Collections.Generic;
using Mujoco;
using Unity.Collections;
using UnityEngine;

//public enum HandSelection
//{
//    None,
//    ShadowHand, // to control the shadow hand 
//    MPL // to control the modular prosthetic limb hand
//}

public enum DOA_LR
{
    HOC, // hand open-close
    WFE, // Wrist flexion-extension
    WPS, // Wrist pronation supination => tbc
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
public struct DOA_mj_LR
{
    public DOA_struct General;
    public GameObject[] mj_actuations;
}

[Serializable]
/// <summary>
/// For simulated mocap DOAs (mostly WPS)
/// </summary>
public struct DOA_mocap
{
    public DOA_struct General;

    public GameObject pointOfAttachGO;
    public Vector3 localAxis;
    public Vector2 rangeDeg;
}
[Serializable]
public struct DOA_struct_LR
{
    public DOA doa;
    public byte UDPSubCategory;
    public bool active;
    public bool EMG_override;
    public float current_value;
    public Vector2 mapping_in;
    public Vector2 mapping_out;
}


public class HandController_LR : MonoBehaviour
{
    /// <summary>
    /// Gameobject Hand => Irrelevant, just for keeping an overview
    /// </summary>
    public GameObject hand;

    /// <summary>
    /// Set the UDP category for this HandController => Especially useful for Bimanual Manipulation
    /// </summary>
    public byte UDPCategory = 1;

    /// <summary>
    // array of degrees of actuation, depending on control, all based on mujoco actuation
    /// </summary>
    public DOA_mj[] DOA_mujoco;


    /// <summary>
    /// Array of simulated degrees of actuation which are not supported by the mujoco model itself (only works on mocap components)
    /// </summary>
    public DOA_mocap[] DOA_mocap;

    [SerializeField]
    private float DelayInSeconds = 0;
    [SerializeField]
    public int DelayInSamples = 0;

    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame => TESTING
    void Update()
    {

    }

    private void FixedUpdate()
    {
        DOA_mj[] _doa_mujoco = new DOA_mj[DOA_mujoco.Length];
        DOA_mocap[] _doa_mocap = new DOA_mocap[DOA_mocap.Length];

        Array.Copy(DOA_mujoco, _doa_mujoco, DOA_mujoco.Length);
        Array.Copy(DOA_mocap, _doa_mocap, DOA_mocap.Length);

        DOA_mujoco = GetUdpValues(_doa_mujoco, DelayInSamples, DelayInSeconds);
        DOA_mocap = GetUdpValues(_doa_mocap, DelayInSamples, DelayInSeconds);

        // Perform actuation
        SetActuation();


        // ensure that just the pronation and supination is active, when wpsTask is performed
        if (TaskManager.wpsTask)
        {

            OverwriteCurrVal(DOA.WFE, 0);
            OverwriteCurrVal(DOA.HOC, 0);
        }
        else
        {
            ActivateEMGOverwrite(DOA.WFE);
            ActivateEMGOverwrite(DOA.HOC);
        }
    }

    public DOA_mj[] GetUdpValues(DOA_mj[] DOA_array, int samplesBack = 0, float timeBack = 0, bool ignoreEMGState = false)
    {
        for (int i = 0; i < DOA_array.Length; i++)
        {
            // next if emg override is disabled
            if (!DOA_array[i].General.EMG_override && !ignoreEMGState) continue;

            DOA_array[i].General.current_value = GetCurrentValue(DOA_array[i].General, samplesBack, timeBack);
        }
        return DOA_array;
    }


    private DOA_mocap[] GetUdpValues(DOA_mocap[] DOA_array, int samplesBack = 0, float timeBack = 0, bool ignoreEMGState = false)
    {
        for (int i = 0; i < DOA_array.Length; i++)
        {
            // next if emg override is disabled
            if (!DOA_array[i].General.EMG_override && !ignoreEMGState) continue;

            DOA_array[i].General.current_value = GetCurrentValue(DOA_array[i].General, samplesBack, timeBack);
        }

        return DOA_array;
    }

    private float GetCurrentValue(DOA_struct doa_struct, int samplesBack = 0, float timeBack = 0)
    {
        // object test = StreamlinedInputManager.udpReceiver.getUdpObjects(0, 1, true);
        // object test2 = StreamlinedInputManager.udpReceiver.getUdpObjects(0, 1);
        object[] data = null;
        float currentValue = 0;

        // if nothing was received data is null
        /// <summary>
        /// values set in inspector:
        /// HOC, finger flexions: in(0,100); out(-0.1,1)
        /// WFE, WUD: in(0,100); out(1,-1)
        /// WPS: in(50,100); out(-1,1)
        /// </summary>
        data = StreamlinedInputManager.udpReceiver.getData(UDPCategory, doa_struct.UDPSubCategory, samplesBack, timeBack);
        if (data != null) currentValue = Convert.ToSingle(data[0]).Remap(doa_struct.mapping_in.x, doa_struct.mapping_in.y, doa_struct.mapping_out.x, doa_struct.mapping_out.y);


        return currentValue;
    }


    private void SetActuation()
    {
        foreach (var doa in DOA_mujoco)
        {
            foreach (GameObject actGO in doa.mj_actuations)
            {
                if (actGO == null) continue;
                MjActuator mjActuator = actGO.GetComponent<MjActuator>();
                if (doa.General.current_value >= 0)
                {
                    mjActuator.Control = doa.General.current_value.Remap(0, 1, 0, mjActuator.CommonParams.CtrlRange.y);
                }
                else
                {
                    mjActuator.Control = doa.General.current_value.Remap(-1, 0, mjActuator.CommonParams.CtrlRange.x, 0);
                }
            }
        }

        foreach (var doa in DOA_mocap)
        {
            // remap values to Range
            float val = 0;
            if (doa.General.current_value >= 0)
            {
                val = doa.General.current_value.Remap(0, 1, 0, doa.rangeDeg.y);
            }
            else
            {
                val = doa.General.current_value.Remap(-1, 0, doa.rangeDeg.x, 0);
            }

            Quaternion local_rot = Quaternion.AngleAxis(val, doa.localAxis);
            if (!local_rot.Equals(doa.pointOfAttachGO.transform.localRotation))
            {
                doa.pointOfAttachGO.transform.localRotation = local_rot;
            }
        }
    }

    public void OverwriteCurrVal(DOA doa, float val, bool deactivateEMG = true)
    {
        // get corresponding struct / mj struct
        for (int i = 0; i < DOA_mujoco.Length; i++)
        {
            if (DOA_mujoco[i].General.doa == doa)
            {
                if (deactivateEMG) DOA_mujoco[i].General.EMG_override = false;

                DOA_mujoco[i].General.current_value = val;
                break;
            }
        }
    }

    public void ActivateEMGOverwrite(DOA doa, bool activate = true)
    {
        // get corresponding struct / mj struct
        for (int i = 0; i < DOA_mujoco.Length; i++)
        {
            if (DOA_mujoco[i].General.doa == doa)
            {
                DOA_mujoco[i].General.EMG_override = activate;
                break;
            }
        }
    }

    public void SetDelay(int DelayInSamples = 0, float DelayInSeconds = 0)
    {
        if (DelayInSamples != 0 && DelayInSeconds != 0) throw new Exception("only one delay can be set - either in samples or in seconds");

        else if (DelayInSamples != 0) this.DelayInSamples = DelayInSamples;
        else if (DelayInSeconds != 0) this.DelayInSeconds = DelayInSeconds;
    }

    public void ResetDelay()
    {
        DelayInSeconds = 0; DelayInSamples = 0;
    }
}

// From https://forum.unity.com/threads/re-map-a-number-from-one-range-to-another.119437/
//public static class ExtensionMethods_LR
//{
//    public static float Remap(this float value, float from1, float to1, float from2, float to2)
//    {
//        return (value - from1) / (to1 - from1) * (to2 - from2) + from2;
//    }
//}