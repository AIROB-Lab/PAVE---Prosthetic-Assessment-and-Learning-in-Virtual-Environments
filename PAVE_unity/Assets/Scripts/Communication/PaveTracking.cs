using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Sim;
using Sim.Tracking;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.Serialization;
using ViveSR.anipal.Eye;

/// <summary>PAVE research acquisition and recording; Unity XR components own movement.
/// Generic pose/gaze acquisition lives in SIM, HTC native lifecycle in its dedicated adapter.
/// Serialized fields migrate through FormerlySerializedAs. Task ROI labels and CSV schemas stay here.</summary>
public class PaveTracking : MonoBehaviour
{
    [System.Serializable]
    public class TrackerBinding
    {
        [FormerlySerializedAs("used")]
        public bool Enabled;
        [FormerlySerializedAs("pos")]
        public TrackerRole Role;
        public TrackerBinding(TrackerRole role)
        {
            Enabled = false;
            Role = role;
        }
    }

    public enum TrackerRole : uint
    {
        TrackerLeftFoot = 0x1000u,
        TrackerRightFoot = 0x2000u,
        TrackerLeftShoulder = 0x4000u,
        TrackerRightShoulder = 0x8000u,
        TrackerLeftElbow = 0x10000u,
        TrackerRightElbow = 0x20000u,
        TrackerLeftKnee = 0x40000u,
        TrackerRightKnee = 0x80000u,
        TrackerWaist = 0x100000u,
        TrackerChest = 0x200000u,
        TrackerCamera = 0x400000u,
        TrackerKeyboard = 0x800000u
    }

    const string EyeCsvHeader = // timestamps
 "time_stamp_s" + "," + // UTC-anchored host acquisition time
 "time_stamp_eyeData" + "," + // combined eye data
 "gaze_direction_combined_x" + "," + "gaze_direction_combined_y" + "," + "gaze_direction_combined_z" + "," + "gazeOriginCombinedX_mm" + "," + "gazeOriginCombinedY_mm" + "," + "gazeOriginCombinedZ_mm" + "," + "convergenceDistanceValidity" + "," + "convergenceDistance_mm" + "," + // right eye Data
 "eyeDataValidataBitMaskRight" + "," + "gazeDirectionRightX" + "," + "gazeDirectionRightY" + "," + "gazeDirectionRightZ" + "," + "pupilDiameterRight_mm" + "," + "eyeOpennessRight" + "," + "pupilPositionInSensorAreaRightX" + "," + "pupilPositionInSensorAreaRightY" + "," + // left eye Data
 "eyeDataValidataBitMaskLeft" + "," + "gazeDirectionLeftX" + "," + "gazeDirectionLeftY" + "," + "gazeDirectionLeftZ" + "," + "pupilDiameterLeft_mm" + "," + "eyeOpennessLeft" + "," + "pupilPositionInSensorAreaLeftX" + "," + "pupilPositionInSensorAreaLeftY" + "\n";
    const string RoiCsvHeader = "time_stamp_s,hand,object,target,hitProperties\n";
    [Tooltip("Record enabled acquisition channels into the current PAVE session.")]
    [FormerlySerializedAs("activateLogging")]
    public bool EnableLogging;
    [FormerlySerializedAs("trackEyes")]
    public bool TrackEyes;
    [FormerlySerializedAs("trackHead")]
    public bool TrackHead;
    [FormerlySerializedAs("trackLeftController")]
    public bool TrackLeftController;
    [FormerlySerializedAs("trackRightController")]
    public bool TrackRightController;
    [FormerlySerializedAs("usedTrackers")]
    public List<TrackerBinding> Trackers = new List<TrackerBinding>();
    [Tooltip("XR Origin camera-offset transform, the parent space of tracked camera/controllers.")]
    [FormerlySerializedAs("trackingOrigin")]
    public Transform TrackingOrigin;
    [Tooltip("Preferred eye provider. HTC raw data remains available through the separate SDK adapter.")]
    [FormerlySerializedAs("gazeSource")]
    public SimGazeSource GazeSource;
    [Tooltip("Optional combined-gaze provider when the preferred source has no fresh sample.")]
    [FormerlySerializedAs("fallbackGazeSource")]
    public SimGazeSource FallbackGazeSource;
    [FormerlySerializedAs("maximumGazeAgeSeconds")]
    [Min(0.01f)]
    public float MaximumGazeAgeSeconds = 0.25f;
    [FormerlySerializedAs("spherecastRadius")]
    [Min(0)]
    public float GazeCastRadius;
    [Tooltip("Optional gaze debug sphere; never assign the headset, controller or prosthesis.")]
    [FormerlySerializedAs("eyeBall")]
    public GameObject GazeDebugObject;
    [FormerlySerializedAs("main_camera")]
    public GameObject GazeCamera;
    /// <summary>Latest observations. Check IsValid before using a pose; missing data is not a zero measurement.</summary>
    public static SimPoseSample HeadPose { get; private set; }
    public static SimPoseSample LeftControllerPose { get; private set; }
    public static SimPoseSample RightControllerPose { get; private set; }

    static readonly Dictionary<TrackerRole, SimPoseSample> trackerPoses = new Dictionary<TrackerRole, SimPoseSample>();
    public static IReadOnlyDictionary<TrackerRole, SimPoseSample> TrackerPoses => trackerPoses;
    public static double Now => SimClock.Now;
    public static bool LookingAtHand { get; private set; }
    public static bool LookingAtObject { get; private set; }
    public static bool LookingAtTarget { get; private set; }
    public static bool LeftEyeValid { get; private set; }
    public static bool RightEyeValid { get; private set; }
    public static bool GazeValid { get; private set; }
    public static string EyeStatus { get; private set; } = "Disabled";
    public static string ActiveGazeSource { get; private set; } = "None";
    public bool? LeftEyeValidity { get; private set; }
    public bool? RightEyeValidity { get; private set; }

    readonly SimXrPoseReader head = new SimXrPoseReader(InputDeviceCharacteristics.HeadMounted);
    readonly SimXrPoseReader left = new SimXrPoseReader(InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Left);
    readonly SimXrPoseReader right = new SimXrPoseReader(InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Right);
    readonly Dictionary<TrackerRole, SimXrPoseReader> trackers = new Dictionary<TrackerRole, SimXrPoseReader>();
    PaveSranipalEyeSource rawEyeSource;
    static PaveTracking owner;
    bool warnedMissingLogger;
    const string PoseHeader = "host_time_s,availability,local_x_m,local_y_m,local_z_m,local_qw,local_qx,local_qy,local_qz,world_x_m,world_y_m,world_z_m,world_qw,world_qx,world_qy,world_qz\n";
    const string GazeHeader = "host_time_s,source,local_frame,availability,local_x_m,local_y_m,local_z_m,local_dx,local_dy,local_dz,world_x_m,world_y_m,world_z_m,world_dx,world_dy,world_dz,left_valid,right_valid\n";
    protected virtual void OnEnable()
    {
        if (owner != null && owner != this)
        {
            Debug.LogWarning("Only one PAVE acquisition adapter may own the legacy static study state.", this);
            enabled = false;
            return;
        }

        owner = this;
        rawEyeSource = GazeSource as PaveSranipalEyeSource ?? FallbackGazeSource as PaveSranipalEyeSource;
        if (rawEyeSource != null)
        {
            rawEyeSource.RawSampleReceived += RecordRawEyes;
        }
    }

    protected virtual void Start()
    {
        if (TrackingOrigin == null && GazeCamera != null)
        {
            TrackingOrigin = GazeCamera.transform.parent;
        }

        if (TrackingOrigin == null)
        {
            Debug.LogWarning("Tracking origin is unassigned; pose world coordinates use identity tracking space.", this);
        }
    }

    protected virtual void Update()
    {
        if (GazeSource != null)
        {
            GazeSource.enabled = TrackEyes;
        }

        if (FallbackGazeSource != null)
        {
            FallbackGazeSource.enabled = TrackEyes;
        }

        if (!TrackHead)
        {
            HeadPose = default;
        }

        if (!TrackLeftController)
        {
            LeftControllerPose = default;
        }

        if (!TrackRightController)
        {
            RightControllerPose = default;
        }

        trackerPoses.Clear();
        if (TrackHead)
        {
            SimPoseSample sample = head.Read(TrackingOrigin);
            HeadPose = sample;
            RecordPose("HMD_Tracking", sample);
        }

        if (TrackLeftController)
        {
            SimPoseSample sample = left.Read(TrackingOrigin);
            LeftControllerPose = sample;
            RecordPose("LeftController_Tracking", sample);
        }

        if (TrackRightController)
        {
            SimPoseSample sample = right.Read(TrackingOrigin);
            RightControllerPose = sample;
            RecordPose("RightController_Tracking", sample);
        }

        foreach (TrackerBinding binding in Trackers)
        {
            if (!binding.Enabled)
            {
                continue;
            }

            if (!trackers.TryGetValue(binding.Role, out SimXrPoseReader reader))
            {
                reader = new SimXrPoseReader((InputDeviceCharacteristics)binding.Role);
                trackers.Add(binding.Role, reader);
            }

            SimPoseSample sample = reader.Read(TrackingOrigin);
            trackerPoses[binding.Role] = sample;
            RecordPose(binding.Role.ToString(), sample);
        }
    }

    protected virtual void LateUpdate()
    {
        ResetGaze();
        if (!TrackEyes)
        {
            EyeStatus = "Disabled";
            if (GazeDebugObject != null)
            {
                GazeDebugObject.SetActive(false);
            }

            return;
        }

        SimGazeSample sample = default;
        bool valid = GazeSource != null && GazeSource.TryGetLatest(MaximumGazeAgeSeconds, out sample);
        if (!valid && FallbackGazeSource != null)
        {
            valid = FallbackGazeSource.TryGetLatest(MaximumGazeAgeSeconds, out sample);
        }

        GazeValid = valid;
        ActiveGazeSource = valid ? sample.Source : "None";
        EyeStatus = valid ? "Valid" : "Unavailable or stale";
        LeftEyeValidity = valid ? sample.LeftEyeValid : null;
        RightEyeValidity = valid ? sample.RightEyeValid : null;
        // Unknown per-eye validity must not be presented as a measured true value.
        LeftEyeValid = LeftEyeValidity == true;
        RightEyeValid = RightEyeValidity == true;
        if (valid)
        {
            TrackingROI(sample);
            if (GazeDebugObject != null)
            {
                GazeDebugObject.transform.SetPositionAndRotation(sample.WorldRay.GetPoint(0.5f), Quaternion.LookRotation(sample.WorldRay.direction));
                GazeDebugObject.SetActive(true);
            }
        }

        if (!valid && GazeDebugObject != null)
        {
            GazeDebugObject.SetActive(false);
        }

        RecordGaze(sample, valid);
    }

    bool CanLog()
    {
        if (!EnableLogging)
        {
            return false;
        }

        if (LoggingManager.OutputDirectory != null)
        {
            return true;
        }

        if (!warnedMissingLogger)
        {
            Debug.LogWarning("Acquisition is running without a session logger; samples are not being recorded.", this);
            warnedMissingLogger = true;
        }

        return false;
    }

    void RecordPose(string name, SimPoseSample sample)
    {
        if (!CanLog())
        {
            return;
        }

        LoggingManager.CreateNewLog(name, PoseHeader);
        string values = sample.IsValid ? PoseValues(sample.LocalPose) + "," + PoseValues(sample.WorldPose) : new string (',', 13);
        LoggingManager.AddToBuffer(name, FormattableString.Invariant($"{sample.TimestampSeconds},{sample.Availability},{values}\n"));
    }

    static string PoseValues(Pose pose) => FormattableString.Invariant($"{pose.position.x},{pose.position.y},{pose.position.z},{pose.rotation.w},{pose.rotation.x},{pose.rotation.y},{pose.rotation.z}");
    void RecordGaze(SimGazeSample sample, bool valid)
    {
        if (!CanLog())
        {
            return;
        }

        LoggingManager.CreateNewLog("Gaze_Tracking", GazeHeader);
        string values = valid ? RayValues(sample.LocalRay) + "," + RayValues(sample.WorldRay) + "," + sample.LeftEyeValid?.ToString() + "," + sample.RightEyeValid?.ToString() : new string (',', 13);
        LoggingManager.AddToBuffer("Gaze_Tracking", FormattableString.Invariant($"{(valid ? sample.TimestampSeconds : Now)},{(valid ? sample.Source : "None")},{(valid ? sample.LocalFrame : "")},{(valid ? "Valid" : "UnavailableOrStale")},{values}\n"));
    }

    static string RayValues(Ray ray) => FormattableString.Invariant($"{ray.origin.x},{ray.origin.y},{ray.origin.z},{ray.direction.x},{ray.direction.y},{ray.direction.z}");
    void RecordRawEyes(double time, EyeData data)
    {
        if (!CanLog())
        {
            return;
        }

        LoggingManager.CreateNewLog("Eye_Tracking", EyeCsvHeader);
        var combined = data.verbose_data.combined;
        var l = data.verbose_data.left;
        var r = data.verbose_data.right;
        // Preserve the SDK's native raw axes/mm and device timestamp in this legacy CSV only.
        object[] values =
        {
            time,
            data.timestamp,
            combined.eye_data.gaze_direction_normalized.x,
            combined.eye_data.gaze_direction_normalized.y,
            combined.eye_data.gaze_direction_normalized.z,
            combined.eye_data.gaze_origin_mm.x,
            combined.eye_data.gaze_origin_mm.y,
            combined.eye_data.gaze_origin_mm.z,
            combined.convergence_distance_validity,
            combined.convergence_distance_mm,
            r.eye_data_validata_bit_mask,
            r.gaze_direction_normalized.x,
            r.gaze_direction_normalized.y,
            r.gaze_direction_normalized.z,
            r.pupil_diameter_mm,
            r.eye_openness,
            r.pupil_position_in_sensor_area.x,
            r.pupil_position_in_sensor_area.y,
            l.eye_data_validata_bit_mask,
            l.gaze_direction_normalized.x,
            l.gaze_direction_normalized.y,
            l.gaze_direction_normalized.z,
            l.pupil_diameter_mm,
            l.eye_openness,
            l.pupil_position_in_sensor_area.x,
            l.pupil_position_in_sensor_area.y
        };
        LoggingManager.AddToBuffer("Eye_Tracking", string.Join(",", Array.ConvertAll(values, value => Convert.ToString(value, CultureInfo.InvariantCulture))) + "\n");
    }

    void TrackingROI(SimGazeSample sample)
    {
        Ray ray = sample.WorldRay;
        RaycastHit[] hits = Physics.SphereCastAll(ray, GazeCastRadius, 10f, 1 << 10);
        LookingAtHand = hits.Any(hit => hit.collider.CompareTag("hand_collider"));
        LookingAtObject = hits.Any(hit => hit.collider.CompareTag("object_collider"));
        LookingAtTarget = hits.Any(hit => hit.collider.CompareTag("target_collider"));
        if (!CanLog())
        {
            return;
        }

        LoggingManager.CreateNewLog("ROI", RoiCsvHeader);
        string properties = string.Join(";", hits.Select(hit => hit.collider.gameObject.name.Replace("\"", "\"\"") + ":" + hit.distance.ToString(CultureInfo.InvariantCulture)));
        LoggingManager.AddToBuffer("ROI", FormattableString.Invariant($"{sample.TimestampSeconds},{LookingAtHand},{LookingAtObject},{LookingAtTarget},\"{properties}\"\n"));
    }

    void ResetGaze()
    {
        GazeValid = LeftEyeValid = RightEyeValid = LookingAtHand = LookingAtObject = LookingAtTarget = false;
        LeftEyeValidity = RightEyeValidity = null;
    }

    protected virtual void OnDisable()
    {
        if (rawEyeSource != null)
        {
            rawEyeSource.RawSampleReceived -= RecordRawEyes;
        }

        if (owner != this)
        {
            return;
        }

        if (GazeSource != null)
        {
            GazeSource.enabled = false;
        }

        if (FallbackGazeSource != null)
        {
            FallbackGazeSource.enabled = false;
        }

        ResetGaze();
        if (GazeDebugObject != null)
        {
            GazeDebugObject.SetActive(false);
        }

        EyeStatus = "Disabled";
        ActiveGazeSource = "None";
        HeadPose = LeftControllerPose = RightControllerPose = default;
        trackerPoses.Clear();
        owner = null;
    }

    public void CalibrateEyes()
    {
        if (rawEyeSource == null || !rawEyeSource.Calibrate())
        {
            Debug.LogWarning("Eye calibration requires an active supported SRanipal source. For OpenXR, calibrate through the headset runtime.", this);
        }
    }
}
