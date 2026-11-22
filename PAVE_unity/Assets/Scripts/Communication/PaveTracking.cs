using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Sim;
using Sim.Tracking;
using UnityEngine;
using UnityEngine.XR;
using ViveSR.anipal.Eye;

/// <summary>PAVE research acquisition and recording; Unity XR components own movement.
/// Generic pose/gaze acquisition lives in SIM, HTC native lifecycle in its dedicated adapter.
/// Legacy public fields retain their scene identities. Task ROI labels and CSV schemas stay here.</summary>
public class PaveTracking : MonoBehaviour
{
    public class HeadTracking
    {
        public double headTimestamp;
        public Vector3 headPosition;
        public Quaternion headRotation;
        Vector3 Position
        {
            get { return headPosition;  }
        }
        Quaternion Rotation
        {
            get { return headRotation; }
        }
        double Timestamp
        {
            get { return headTimestamp; }
        }
    }

    public class LeftControllerTracking
    {
        public double leftControllerTimestamp;
        public Vector3 leftControllerPosition;
        public Quaternion leftControllerRotation;
        Vector3 Position
        {
            get { return leftControllerPosition; }
        }
        Quaternion Rotation
        {
            get { return leftControllerRotation; }
        }
        double Timestamp
        {
            get { return leftControllerTimestamp; }
        }
    }

    public class RightControllerTracking
    {
        public double rightControllerTimestamp;
        public Vector3 rightControllerPosition;
        public Quaternion rightControllerRotation;
        Vector3 Position
        {
            get { return rightControllerPosition; }
        }
        Quaternion Rotation
        {
            get { return rightControllerRotation; }
        }
        double Timestamp
        {
            get { return rightControllerTimestamp; }
        }
    }

    public class EyeTracking
    {
        internal Vector3 debugPos;
        internal Quaternion debugOri;


        // unix timestamp, eyedata
        private (double, EyeData) current_eyeData;
        private List<(double, EyeData)> last_eyeDatas = new List<(double, EyeData)>();
        public (double, EyeData) EyeData
        {
            get { lock (eyeLock) { return current_eyeData; } }
            set { lock (eyeLock) {
                    current_eyeData = value;
                    last_eyeDatas.Add(value);

                    while (last_eyeDatas.Count() > last_eyeData_size)
                    {
                        last_eyeDatas.RemoveAt(0);
                    }
                }
            }
        }
        public (double, EyeData)[] Last_eyeDatas
        {
            get {lock (eyeLock)
                {
                    // last eye datas to array
                    (double, EyeData)[] arrOut = last_eyeDatas.ToArray();
                    // clear eye data buffer => Important can only get it once!
                    last_eyeDatas.Clear();
                    // return
                    return arrOut;
                }}
        }

        //{
        //    get { lock (eyeLock) { return eyeTimestamp; } }
        //    set { lock (eyeLock) { eyeTimestamp = value; } }
        //}
    }

    public class TrackerTracking
    {
        public double trackerTimestamp;
        public Vector3 trackerPosition;
        public Quaternion trackerRotation;
        Vector3 Position
        {
            get { return trackerPosition; }
        }
        Quaternion Rotation
        {
            get { return trackerRotation; }
        }
        double Timestamp
        {
            get { return trackerTimestamp; }
        }
    }

    [System.Serializable]
    public class TrackerLog
    {
        public bool used;
        public position pos;
        public TrackerLog(position pos)
        {
            this.used = false;
            this.pos = pos;
        }
    }

    public enum position : uint
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

    partial class SIMHeader
    {
        static public string eyeHeader =

        // timestamps
        "time_stamp_s" + "," + // from stopwatch
        "time_stamp_eyeData" + "," +

        // combined eye data
        "gaze_direction_combined_x" + "," +
        "gaze_direction_combined_y" + "," +
        "gaze_direction_combined_z" + "," +
        "gazeOriginCombinedX_mm" + "," +
        "gazeOriginCombinedY_mm" + "," +
        "gazeOriginCombinedZ_mm" + "," +
        "convergenceDistanceValidity" + "," +
        "convergenceDistance_mm" + "," +

        // right eye Data
        "eyeDataValidataBitMaskRight" + "," +
        "gazeDirectionRightX" + "," +
        "gazeDirectionRightY" + "," +
        "gazeDirectionRightZ" + "," +
        "pupilDiameterRight_mm" + "," +
        "eyeOpennessRight" + "," +
        "pupilPositionInSensorAreaRightX" + "," +
        "pupilPositionInSensorAreaRightY" + "," +

        // left eye Data
        "eyeDataValidataBitMaskLeft" + "," +
        "gazeDirectionLeftX" + "," +
        "gazeDirectionLeftY" + "," +
        "gazeDirectionLeftZ" + "," +
        "pupilDiameterLeft_mm" + "," +
        "eyeOpennessLeft" + "," +
        "pupilPositionInSensorAreaLeftX" + "," +
        "pupilPositionInSensorAreaLeftY" +

        Environment.NewLine;

        static public string headHeader =
        "time_stamp_s" + "," +
        "position_x" + "," +
        "position_y" + "," +
        "position_z" + "," +
        "rotation_w" + "," +
        "rotation_x" + "," +
        "rotation_y" + "," +
        "rotation_z" +
        Environment.NewLine;

        static public string leftControllerHeader =
        "time_stamp_s" + "," +
        "position_x" + "," +
        "position_y" + "," +
        "position_z" + "," +
        "rotation_w" + "," +
        "rotation_x" + "," +
        "rotation_y" + "," +
        "rotation_z" +
        Environment.NewLine;

        static public string rightControllerHeader =
        "time_stamp_s" + "," +
        "position_x" + "," +
        "position_y" + "," +
        "position_z" + "," +
        "rotation_w" + "," +
        "rotation_x" + "," +
        "rotation_y" + "," +
        "rotation_z" +
        Environment.NewLine;


        static public string trackerHeader =
        "time_stamp(ms)" +
        Environment.NewLine;

        static public string udpHeader =
        "time_stamp_s" + "," +
        "time_stamp(imBlocks)" + "," +
        "category" + "," +
        "subcategory" + "," +
        "data" + "," +
        "counter" +
        Environment.NewLine;

        static public string roiHeader = "time_stamp_s" + "," + "hand" + "," + "object" + "," + "target" + ","+ "hitProperties" + Environment.NewLine;

    }


    [Tooltip("Record enabled acquisition channels into the current PAVE session.")]
    public bool activateLogging;
    public bool trackEyes;
    public bool trackHead;
    public bool trackLeftController;
    public bool trackRightController;
    public List<TrackerLog> usedTrackers = new List<TrackerLog>();
    [Tooltip("XR Origin camera-offset transform, the parent space of tracked camera/controllers.")]
    public Transform trackingOrigin;
    [Tooltip("Preferred eye provider. HTC raw data remains available through the separate SDK adapter.")]
    public SimGazeSource gazeSource;
    [Tooltip("Optional combined-gaze provider when the preferred source has no fresh sample.")]
    public SimGazeSource fallbackGazeSource;
    [Min(0.01f)] public float maximumGazeAgeSeconds = 0.25f;
    [Min(0)] public float spherecastRadius;
    [Tooltip("Optional gaze debug sphere; never assign the headset, controller or prosthesis.")]
    public GameObject eyeBall;
    public GameObject main_camera;

    // Compatibility properties for existing study displays. Measurements are observations only.
    public static bool S_activateLogging;
    public static bool isEyeCallbackWorking;
    public static bool trackEyesCallback;
    public static int last_eyeData_size = 100;
    static readonly object eyeLock = new object();
    public static HeadTracking headTracker = new HeadTracking();
    public static LeftControllerTracking leftControllerTracker = new LeftControllerTracking();
    public static RightControllerTracking rightControllerTracker = new RightControllerTracking();
    public static EyeTracking eyeTracker = new EyeTracking();
    public static Dictionary<string, TrackerTracking> trackerTrackers = new Dictionary<string, TrackerTracking>();
    public static double Now => SimClock.Now;
    public static bool lookingAtHand { get; private set; }
    public static bool lookingAtObject { get; private set; }
    public static bool lookingAtTarget { get; private set; }
    public static bool eyeValid { get; private set; }
    public static bool eyeValid2 { get; private set; }
    public static bool gazeValid { get; private set; }
    public static string EyeStatus { get; private set; } = "Disabled";
    public static string ActiveGazeSource { get; private set; } = "None";
    public bool? LeftEyeValidity { get; private set; }
    public bool? RightEyeValidity { get; private set; }

    readonly SimXrPoseReader head = new SimXrPoseReader(InputDeviceCharacteristics.HeadMounted);
    readonly SimXrPoseReader left = new SimXrPoseReader(InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Left);
    readonly SimXrPoseReader right = new SimXrPoseReader(InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Right);
    readonly Dictionary<position, SimXrPoseReader> trackers = new Dictionary<position, SimXrPoseReader>();
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
        rawEyeSource = gazeSource as PaveSranipalEyeSource ?? fallbackGazeSource as PaveSranipalEyeSource;
        if (rawEyeSource != null) rawEyeSource.RawSampleReceived += RecordRawEyes;
    }

    protected virtual void Start()
    {
        if (trackingOrigin == null && main_camera != null) trackingOrigin = main_camera.transform.parent;
        if (trackingOrigin == null)
            Debug.LogWarning("Tracking origin is unassigned; pose world coordinates use identity tracking space.", this);
    }

    protected virtual void Update()
    {
        S_activateLogging = activateLogging;
        trackEyesCallback = trackEyes;
        if (gazeSource != null) gazeSource.enabled = trackEyes;
        if (fallbackGazeSource != null) fallbackGazeSource.enabled = trackEyes;

        if (trackHead)
        {
            SimPoseSample sample = head.Read(trackingOrigin);
            headTracker.headTimestamp = sample.IsValid ? sample.TimestampSeconds : -1;
            headTracker.headPosition = sample.WorldPose.position;
            headTracker.headRotation = sample.WorldPose.rotation;
            RecordPose("HMD_Tracking", sample);
        }
        if (trackLeftController)
        {
            SimPoseSample sample = left.Read(trackingOrigin);
            leftControllerTracker.leftControllerTimestamp = sample.IsValid ? sample.TimestampSeconds : -1;
            leftControllerTracker.leftControllerPosition = sample.WorldPose.position;
            leftControllerTracker.leftControllerRotation = sample.WorldPose.rotation;
            RecordPose("LeftController_Tracking", sample);
        }
        if (trackRightController)
        {
            SimPoseSample sample = right.Read(trackingOrigin);
            rightControllerTracker.rightControllerTimestamp = sample.IsValid ? sample.TimestampSeconds : -1;
            rightControllerTracker.rightControllerPosition = sample.WorldPose.position;
            rightControllerTracker.rightControllerRotation = sample.WorldPose.rotation;
            RecordPose("RightController_Tracking", sample);
        }
        foreach (TrackerLog binding in usedTrackers)
        {
            if (!binding.used) continue;
            if (!trackers.TryGetValue(binding.pos, out SimXrPoseReader reader))
            {
                reader = new SimXrPoseReader((InputDeviceCharacteristics)binding.pos);
                trackers.Add(binding.pos, reader);
            }
            SimPoseSample sample = reader.Read(trackingOrigin);
            if (!trackerTrackers.TryGetValue(binding.pos.ToString(), out TrackerTracking state))
                trackerTrackers.Add(binding.pos.ToString(), state = new TrackerTracking());
            state.trackerTimestamp = sample.IsValid ? sample.TimestampSeconds : -1;
            state.trackerPosition = sample.WorldPose.position;
            state.trackerRotation = sample.WorldPose.rotation;
            RecordPose(binding.pos.ToString(), sample);
        }
    }

    protected virtual void LateUpdate()
    {
        ResetGaze();
        if (!trackEyes) { EyeStatus = "Disabled"; return; }
        SimGazeSample sample = default;
        bool valid = gazeSource != null && gazeSource.TryGetLatest(maximumGazeAgeSeconds, out sample);
        if (!valid && fallbackGazeSource != null)
            valid = fallbackGazeSource.TryGetLatest(maximumGazeAgeSeconds, out sample);
        gazeValid = valid;
        ActiveGazeSource = valid ? sample.Source : "None";
        EyeStatus = valid ? "Valid" : "Unavailable or stale";
        LeftEyeValidity = valid ? sample.LeftEyeValid : null;
        RightEyeValidity = valid ? sample.RightEyeValid : null;
        // Unknown per-eye validity must not be presented as a measured true value.
        eyeValid = LeftEyeValidity == true;
        eyeValid2 = RightEyeValidity == true;
        if (valid)
        {
            TrackingROI(sample);
            if (eyeBall != null)
            {
                eyeBall.transform.SetPositionAndRotation(sample.WorldRay.GetPoint(0.5f), Quaternion.LookRotation(sample.WorldRay.direction));
                eyeBall.SetActive(true);
            }
        }
        RecordGaze(sample, valid);
    }

    bool CanLog()
    {
        if (!activateLogging) return false;
        if (LoggingManager.OutputDirectory != null) return true;
        if (!warnedMissingLogger)
        {
            Debug.LogWarning("Acquisition is running without a session logger; samples are not being recorded.", this);
            warnedMissingLogger = true;
        }
        return false;
    }

    void RecordPose(string name, SimPoseSample sample)
    {
        if (!CanLog()) return;
        LoggingManager.CreateNewLog(name, PoseHeader);
        string values = sample.IsValid ? PoseValues(sample.LocalPose) + "," + PoseValues(sample.WorldPose) : new string(',', 13);
        LoggingManager.AddToBuffer(name, FormattableString.Invariant($"{sample.TimestampSeconds},{sample.Availability},{values}\n"));
    }

    static string PoseValues(Pose pose)
        => FormattableString.Invariant($"{pose.position.x},{pose.position.y},{pose.position.z},{pose.rotation.w},{pose.rotation.x},{pose.rotation.y},{pose.rotation.z}");

    void RecordGaze(SimGazeSample sample, bool valid)
    {
        if (!CanLog()) return;
        LoggingManager.CreateNewLog("Gaze_Tracking", GazeHeader);
        string values = valid ? RayValues(sample.LocalRay) + "," + RayValues(sample.WorldRay) + "," +
            sample.LeftEyeValid?.ToString() + "," + sample.RightEyeValid?.ToString() : new string(',', 13);
        LoggingManager.AddToBuffer("Gaze_Tracking", FormattableString.Invariant($"{(valid ? sample.TimestampSeconds : Now)},{(valid ? sample.Source : "None")},{(valid ? sample.LocalFrame : "")},{(valid ? "Valid" : "UnavailableOrStale")},{values}\n"));
    }

    static string RayValues(Ray ray)
        => FormattableString.Invariant($"{ray.origin.x},{ray.origin.y},{ray.origin.z},{ray.direction.x},{ray.direction.y},{ray.direction.z}");

    void RecordRawEyes(double time, EyeData data)
    {
        eyeTracker.EyeData = (time, data);
        isEyeCallbackWorking = true;
        if (!CanLog()) return;
        LoggingManager.CreateNewLog("Eye_Tracking", SIMHeader.eyeHeader);
        var combined = data.verbose_data.combined;
        var l = data.verbose_data.left;
        var r = data.verbose_data.right;
        // Preserve the SDK's native raw axes/mm and device timestamp in this legacy CSV only.
        object[] values = { time, data.timestamp,
            combined.eye_data.gaze_direction_normalized.x, combined.eye_data.gaze_direction_normalized.y, combined.eye_data.gaze_direction_normalized.z,
            combined.eye_data.gaze_origin_mm.x, combined.eye_data.gaze_origin_mm.y, combined.eye_data.gaze_origin_mm.z,
            combined.convergence_distance_validity, combined.convergence_distance_mm,
            r.eye_data_validata_bit_mask, r.gaze_direction_normalized.x, r.gaze_direction_normalized.y, r.gaze_direction_normalized.z,
            r.pupil_diameter_mm, r.eye_openness, r.pupil_position_in_sensor_area.x, r.pupil_position_in_sensor_area.y,
            l.eye_data_validata_bit_mask, l.gaze_direction_normalized.x, l.gaze_direction_normalized.y, l.gaze_direction_normalized.z,
            l.pupil_diameter_mm, l.eye_openness, l.pupil_position_in_sensor_area.x, l.pupil_position_in_sensor_area.y };
        LoggingManager.AddToBuffer("Eye_Tracking", string.Join(",", Array.ConvertAll(values, value => Convert.ToString(value, CultureInfo.InvariantCulture))) + "\n");
    }

    void TrackingROI(SimGazeSample sample)
    {
        Ray ray = sample.WorldRay;
        RaycastHit[] hits = Physics.SphereCastAll(ray, spherecastRadius, 10f, 1 << 10);
        lookingAtHand = hits.Any(hit => hit.collider.CompareTag("hand_collider"));
        lookingAtObject = hits.Any(hit => hit.collider.CompareTag("object_collider"));
        lookingAtTarget = hits.Any(hit => hit.collider.CompareTag("target_collider"));
        if (!CanLog()) return;
        LoggingManager.CreateNewLog("ROI", SIMHeader.roiHeader);
        string properties = string.Join(";", hits.Select(hit => hit.collider.gameObject.name.Replace("\"", "\"\"") + ":" + hit.distance.ToString(CultureInfo.InvariantCulture)));
        LoggingManager.AddToBuffer("ROI", FormattableString.Invariant($"{sample.TimestampSeconds},{lookingAtHand},{lookingAtObject},{lookingAtTarget},\"{properties}\"\n"));
    }

    void ResetGaze()
    {
        gazeValid = eyeValid = eyeValid2 = lookingAtHand = lookingAtObject = lookingAtTarget = false;
        LeftEyeValidity = RightEyeValidity = null;
        if (eyeBall != null) eyeBall.SetActive(false);
    }

    protected virtual void OnDisable()
    {
        if (rawEyeSource != null) rawEyeSource.RawSampleReceived -= RecordRawEyes;
        if (owner != this) return;
        if (gazeSource != null) gazeSource.enabled = false;
        if (fallbackGazeSource != null) fallbackGazeSource.enabled = false;
        ResetGaze();
        EyeStatus = "Disabled";
        ActiveGazeSource = "None";
        isEyeCallbackWorking = trackEyesCallback = S_activateLogging = false;
        owner = null;
    }

    public void CalibrateEyes()
    {
        if (rawEyeSource == null || !rawEyeSource.Calibrate())
            Debug.LogWarning("Eye calibration requires an active supported SRanipal source. For OpenXR, calibrate through the headset runtime.", this);
    }

}
