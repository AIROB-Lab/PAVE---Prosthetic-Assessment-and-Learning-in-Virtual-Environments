using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Sim;
using Sim.Tracking;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.XR;
using ViveSR.anipal.Eye;

/// <summary>Optional HTC SDK adapter. Only this component knows SRanipal's native API.
/// Callback data crosses a bounded queue; Unity, events and file writes stay on the main thread.</summary>
[DefaultExecutionOrder(-100)]
public sealed class PaveSranipalEyeSource : SimGazeSource
{
    [Tooltip("Existing SDK framework, configured with automatic eye startup disabled.")]
    [FormerlySerializedAs("framework")]
    public SRanipal_Eye_Framework Framework;
    [Tooltip("The tracked headset camera. SDK gaze is headset-local, not tracking-origin-local.")]
    [FormerlySerializedAs("headset")]
    public Transform Headset;
    public event Action<double, EyeData> RawSampleReceived;
    public int DroppedSamples { get; private set; }
    public bool CallbackRegistered => registered;

    const int Capacity = 512;
    readonly Queue<(double time, EyeData data)> pending = new Queue<(double, EyeData)>();
    readonly object gate = new object ();
    readonly List<InputDevice> heads = new List<InputDevice>();
    // SRanipal's callback has no context argument: allow one native owner and root its delegate.
    static PaveSranipalEyeSource owner;
    static readonly SRanipal_Eye.CallbackBasic callback = OnEyeData;
    bool registered;
    bool accepting;
    bool startedFramework;
    double nextAttempt;
    void Update()
    {
        if (!registered && SimClock.Now >= nextAttempt)
        {
            nextAttempt = SimClock.Now + 1;
            TryStart();
        }

        if (registered && SRanipal_Eye_Framework.Status != SRanipal_Eye_Framework.FrameworkStatus.WORKING)
        {
            StopSource();
            Status = "SRanipal stopped";
        }

        // Drain every captured sample; the generic Latest property is only a render-rate view.
        while (true)
        {
            (double time, EyeData data) item;
            lock (gate)
            {
                if (pending.Count == 0)
                {
                    break;
                }

                item = pending.Dequeue();
            }

            RawSampleReceived?.Invoke(item.time, item.data);
            var combined = item.data.verbose_data.combined.eye_data;
            bool valid = Headset != null && combined.GetValidity(SingleEyeDataValidity.SINGLE_EYE_DATA_GAZE_ORIGIN_VALIDITY) && combined.GetValidity(SingleEyeDataValidity.SINGLE_EYE_DATA_GAZE_DIRECTION_VALIDITY);
            bool left = item.data.verbose_data.left.GetValidity(SingleEyeDataValidity.SINGLE_EYE_DATA_GAZE_DIRECTION_VALIDITY);
            bool right = item.data.verbose_data.right.GetValidity(SingleEyeDataValidity.SINGLE_EYE_DATA_GAZE_DIRECTION_VALIDITY);
            // SRanipal is right-handed and uses millimeters. Convert once, preserving origin magnitude.
            Vector3 origin = combined.gaze_origin_mm * 0.001f;
            Vector3 direction = combined.gaze_direction_normalized;
            origin.x = -origin.x;
            direction.x = -direction.x;
            Latest = SimGazeSample.FromLocal(item.time, "SRanipal", "head_local", origin, direction, Headset, valid, left, right);
            Status = Latest.Availability.ToString();
        }
    }

    void TryStart()
    {
        if (owner != null && owner != this)
        {
            Status = "Another SRanipal source owns the callback";
            return;
        }

        if (Framework == null || Headset == null)
        {
            Status = "Missing Framework or Headset reference";
            return;
        }

        // Do not load an HTC native runtime just because a Meta Headset or no Headset is present.
        heads.Clear();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.HeadMounted, heads);
        if (heads.Count != 1 || heads[0].name.IndexOf("vive", StringComparison.OrdinalIgnoreCase) < 0)
        {
            Status = "No HTC Headset detected";
            return;
        }

        try
        {
            if (!SRanipal_Eye_API.IsViveProEye())
            {
                Status = "Headset is not supported by this SRanipal adapter";
                return;
            }

            if (Framework.EnableEyeVersion != SRanipal_Eye_Framework.SupportedEyeVersion.version1)
            {
                Status = "This adapter requires SRanipal EyeData v1";
                return;
            }

            Framework.EnableEye = true;
            Framework.EnableEyeDataCallback = true;
            Framework.StartFramework();
            startedFramework = SRanipal_Eye_Framework.Status == SRanipal_Eye_Framework.FrameworkStatus.WORKING;
            if (!startedFramework)
            {
                Status = "SRanipal runtime unavailable";
                return;
            }

            owner = this;
            lock (gate)
            {
                accepting = true;
            }

            SRanipal_Eye.WrapperRegisterEyeDataCallback(Marshal.GetFunctionPointerForDelegate(callback));
            registered = true;
            Status = "Waiting for eye samples";
        }
        catch (Exception error)when (error is DllNotFoundException || error is EntryPointNotFoundException || error is BadImageFormatException)
        {
            StopSource();
            Status = "SRanipal native runtime unavailable";
            // One explanatory message per enable cycle, rather than an error every frame.
            Debug.LogWarning(Status + ": " + error.Message, this);
            nextAttempt = double.PositiveInfinity;
        }
    }

    [AOT.MonoPInvokeCallback(typeof(SRanipal_Eye.CallbackBasic))]
    static void OnEyeData(ref EyeData data)
    {
        PaveSranipalEyeSource target = owner;
        if (ReferenceEquals(target, null))
        {
            return;
        }

        lock (target.gate)
        {
            if (!target.accepting)
            {
                return;
            }

            if (target.pending.Count == Capacity)
            {
                target.pending.Dequeue();
                target.DroppedSamples++;
            }

            target.pending.Enqueue((SimClock.Now, data));
        }
    }

    void StopSource()
    {
        // Never hold gate across an SDK call: unregister may wait for an in-flight callback.
        lock (gate)
        {
            accepting = false;
            pending.Clear();
        }

        try
        {
            if (registered)
            {
                SRanipal_Eye.WrapperUnRegisterEyeDataCallback(Marshal.GetFunctionPointerForDelegate(callback));
            }

            if (startedFramework)
            {
                Framework.StopFramework();
            }
        }
        finally
        {
            registered = false;
            startedFramework = false;
            if (owner == this)
            {
                owner = null;
            }

            if (Framework != null)
            {
                Framework.EnableEye = false;
            }

            Latest = default;
        }
    }

    protected override void OnDisable()
    {
        StopSource();
        nextAttempt = 0;
        base.OnDisable();
    }

    public bool Calibrate()
    {
        if (!registered)
        {
            return false;
        }

        SRanipal_Eye_v2.LaunchEyeCalibration();
        return true;
    }
}
