using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Mujoco;
using Newtonsoft.Json;
using UnityEngine;

namespace Pave.Assessments
{
    /// <summary>Assessment-only session: fixed protocol, explicit start, no rewards, adaptation,
    /// ghost targets, assistance or automatic retries. MuJoCo supplies physical observations.
    /// The automated virtual criteria are versioned adaptations, not validated clinical norms.</summary>
    [DefaultExecutionOrder(-50)]
    public sealed class AssessmentSession : MonoBehaviour
    {
        public AssessmentKind Kind;
        public Transform ApparatusFrame;
        public HandController Hand;
        public FollowTracker HandFollower;
        public Transform[] Fingertips;
        public AssessmentObject[] Objects;
        public MjGeom[] SourceSupports;
        public MjGeom[] TargetSupports;
        public bool SourceIsRight = true;
        [Tooltip("Assessment native step, seconds. Does not change existing research scenes.")]
        public float PhysicsStepSeconds = 0.005f;
        public float PartitionHeightMeters = 0.152f;
        public float BoxDepthMeters = 0.254f;
        public float BlockEdgeMeters = 0.025f;
        public float TargetHalfWidthMeters = 0.018f;
        public float TargetHalfDepthMeters = 0.018f;
        [Tooltip("Confirmation interval only; targeted completion time retains the last contact time.")]
        public float ReleaseGraceSeconds = 0.1f;
        public float PinSupportDwellSeconds = 0.25f;
        [Tooltip("Administrative limit for untimed-by-protocol tasks. BBT is always 60 seconds.")]
        public float AdministrativeLimitSeconds = 300;
        public string ParticipantCode = "";
        public string FrozenControllerId = "";
        public bool FrozenControllerConfirmed;
        public bool ApparatusCalibrationConfirmed;
        [Tooltip("Technical checks only. Always recorded as diagnostics, excluded from assessment results.")]
        public bool DiagnosticsOnly;
        public string ProtocolId => "pave-virtual-" + Kind + "-v1";
        public TrialState State => clock == null ? TrialState.Ready : clock.State;
        public string LastOutcome { get; private set; } = "Awaiting operator start";
        public string TrialId { get; private set; }
        public int Score => Kind == AssessmentKind.BoxAndBlock ? blockScore.Score : Kind == AssessmentKind.TargetedBoxAndBlock ? targetedScore.ReleasedCount : items.Count(item => item.Evaluated);
        public int UnsuccessfulTransports => targetedScore.UnsuccessfulTransports;
        public double ElapsedSeconds => clock == null ? 0 : clock.Elapsed(Now);
        public bool IsPinTask => Kind == AssessmentKind.ClothespinUp || Kind == AssessmentKind.ClothespinDown;

        sealed class ItemState
        {
            public ReleaseObservation Release = new ReleaseObservation();
            public bool Evaluated;
            public bool Crossed;
            public bool LeftSource;
            public bool PreviousContact;
            public Vector3 PreviousPosition;
            public int Trip;
            public double SupportedSince = -1;
        }

        MjScene scene;
        AssessmentClock clock;
        BoxAndBlockScore blockScore = new BoxAndBlockScore();
        TargetedBlockScore targetedScore = new TargetedBlockScore();
        ItemState[] items = Array.Empty<ItemState>();
        AssessmentContact[] contacts;
        readonly HashSet<int> handGeoms = new HashSet<int>();
        readonly Dictionary<int, int> objectGeoms = new Dictionary<int, int>();
        readonly HashSet<int> sourceGeoms = new HashSet<int>();
        readonly HashSet<int> targetGeoms = new HashSet<int>();
        readonly HashSet<int> crossedTrips = new HashSet<int>();
        int[] rootBodies;
        Vector3[] previousFingertips;
        bool cacheReady, sourceVisit;
        int trip;
        double nextObservation, lastNativeTime, lastStepHostTime;
        float previousFixedStep;
        bool previousRunInBackground;
        string configuration;
        static double Now => UnityEngine.Time.realtimeSinceStartupAsDouble;

        float Direction => SourceIsRight ? 1 : -1;

        void Awake()
        {
            previousFixedStep = UnityEngine.Time.fixedDeltaTime;
            previousRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            UnityEngine.Time.fixedDeltaTime = PhysicsStepSeconds;
        }

        void Start()
        {
            scene = MjScene.Instance;
            scene.postUpdateEvent += AfterPhysics;
            LoggingManager.CreateNewLog("AssessmentEvents", "trial_id,host_time_s,native_time_s,event,object_index,detail\n");
            LoggingManager.CreateNewLog("AssessmentResults", "trial_id,purpose,protocol,state,duration_s,score,unsuccessful_transports,outcome\n");
            LoggingManager.CreateNewLog("AssessmentObjects", "trial_id,host_time_s,native_time_s,elapsed_s,object_index,x_m,y_m,z_m,qx,qy,qz,qw,vx_m_s,vy_m_s,vz_m_s,hand_contact,source_contact,target_contact,hand_contact_normal_sum_N,jaw_deg\n");
            LoggingManager.CreateNewLog("AssessmentHand", "trial_id,host_time_s,native_time_s,x_m,y_m,z_m,hoc_command,wfe_command,wps_command,input_fresh,used_source_time_s\n");
        }

        void CacheNativeIds()
        {
            foreach (MjGeom geom in Hand.transform.root.GetComponentsInChildren<MjGeom>())
            {
                handGeoms.Add(geom.MujocoId);
            }

            for (int index = 0; index < Objects.Length; index++)
            {
                foreach (MjGeom geom in Objects[index].Geoms)
                {
                    objectGeoms.Add(geom.MujocoId, index);
                }
            }

            foreach (MjGeom geom in SourceSupports)
            {
                sourceGeoms.Add(geom.MujocoId);
            }

            foreach (MjGeom geom in TargetSupports)
            {
                targetGeoms.Add(geom.MujocoId);
            }

            rootBodies = Objects.Select(item => item.Body.MujocoId).ToArray();
            contacts = new AssessmentContact[Objects.Length];
            cacheReady = true;
        }

        public string StartBlockingReason()
        {
            if (Hand == null || Hand.hand == null || HandFollower == null || ApparatusFrame == null)
            {
                return "Assign the native palm, controller, follower and apparatus references";
            }
            if (!AssessmentPhysics.Ready(scene) || !cacheReady)
            {
                return "Waiting for native physics";
            }

            if (State != TrialState.Ready)
            {
                return "Reset the apparatus before another trial";
            }

            if (Objects.Length != (IsPinTask ? 3 : Kind == AssessmentKind.BoxAndBlock ? 150 : 9))
            {
                return "Unexpected apparatus object count";
            }

            if (Fingertips == null || Fingertips.Length == 0)
            {
                return "Assign calibrated fingertip references";
            }

            if (Objects.Any(item => Vector3.Distance(item.transform.position, item.InitialPosition) > 0.025f))
            {
                return "Reset props to their starting positions";
            }

            if (DiagnosticsOnly)
            {
                return "";
            }

            if (string.IsNullOrWhiteSpace(ParticipantCode) || string.IsNullOrWhiteSpace(FrozenControllerId))
            {
                return "Enter participant pseudonym and controller/model identifier";
            }

            if (!FrozenControllerConfirmed)
            {
                return "Confirm that external controller learning is disabled";
            }

            if (!ApparatusCalibrationConfirmed)
            {
                return "Confirm apparatus and fingertip calibration before an assessment";
            }

            return InputBlockingReason();
        }

        string InputBlockingReason()
        {
            if (!Hand.InputIsFresh)
            {
                return "Hand input is unavailable or stale";
            }

            if (!HandFollower.HandFollowValid)
            {
                return "Hand follower is not aligned";
            }

            if (!PaveTracking.HeadPose.IsValid || Sim.SimClock.Now - PaveTracking.HeadPose.TimestampSeconds > 0.25)
            {
                return "Head tracking is unavailable or stale";
            }

            if (!PaveTracking.TrackerPoses.TryGetValue(PaveTracking.TrackerRole.TrackerRightElbow, out var pose) || !pose.IsValid || Sim.SimClock.Now - pose.TimestampSeconds > 0.25)
            {
                return "Right-elbow tracking is unavailable or stale";
            }

            return "";
        }

        string ConfigurationStamp() => FormattableString.Invariant($"{Kind}|{SourceIsRight}|{PhysicsStepSeconds}|{PartitionHeightMeters}|{BoxDepthMeters}|{BlockEdgeMeters}|{TargetHalfWidthMeters}|{TargetHalfDepthMeters}|{ReleaseGraceSeconds}|{PinSupportDwellSeconds}|{AdministrativeLimitSeconds}|{DiagnosticsOnly}|{ParticipantCode}|{FrozenControllerId}|{FrozenControllerConfirmed}|{ApparatusCalibrationConfirmed}") + string.Join(";", Hand.DOA_mujoco.Select(axis => FormattableString.Invariant($"{axis.General.doa}:{axis.General.UDPSubCategory}:{axis.General.EMG_override}:{axis.General.mapping_in.x}:{axis.General.mapping_in.y}:{axis.General.mapping_out.x}:{axis.General.mapping_out.y}"))) + Hand.UDPCategory + Hand.CommandTimeoutSeconds.ToString(CultureInfo.InvariantCulture) + Hand.DelayInSamples + (Hand.CommandSource == null ? "UDP" : Hand.CommandSource.GetInstanceID().ToString());
        public bool BeginTrial()
        {
            string reason = StartBlockingReason();
            if (reason.Length > 0)
            {
                LastOutcome = reason;
                return false;
            }

            items = Objects.Select(item => new ItemState { PreviousPosition = ApparatusFrame.InverseTransformPoint(item.transform.position) }).ToArray();
            previousFingertips = Fingertips.Select(tip => ApparatusFrame.InverseTransformPoint(tip.position)).ToArray();
            blockScore = new BoxAndBlockScore();
            targetedScore = new TargetedBlockScore();
            crossedTrips.Clear();
            trip = 1;
            sourceVisit = true;
            TrialId = Guid.NewGuid().ToString("N");
            clock = new AssessmentClock(Kind == AssessmentKind.BoxAndBlock ? 60 : AdministrativeLimitSeconds);
            configuration = ConfigurationStamp();
            WriteManifest(); // If provenance cannot be written, do not start an unrecorded assessment.
            clock.Start(Now);
            nextObservation = 0;
            lastStepHostTime = Now;
            lastNativeTime = AssessmentPhysics.Time(scene);
            LastOutcome = "Running";
            Event("trial_started", -1, DiagnosticsOnly ? "diagnostics_excluded" : "assessment_frozen_controller");
            return true;
        }

        void WriteManifest()
        {
            var manifest = new
            {
                trialId = TrialId,
                purpose = DiagnosticsOnly ? "diagnostics_excluded" : "assessment",
                protocol = ProtocolId,
                participantCode = ParticipantCode,
                controllerId = FrozenControllerId,
                frozenControllerConfirmed = FrozenControllerConfirmed,
                apparatusCalibrationConfirmed = ApparatusCalibrationConfirmed,
                unity = Application.unityVersion,
                buildGuid = Application.buildGUID,
                scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path,
                configuration,
                nativeStepSeconds = AssessmentPhysics.Step(scene),
                frictionCone = MjGlobalSettings.Instance.GlobalOptions.Cone.ToString(),
                frictionImpedanceRatio = MjGlobalSettings.Instance.GlobalOptions.ImpRatio,
                timing = "Operator start; host monotonic elapsed; native post-step observations; no automatic practice or learning",
                limits = "Virtual adaptation. Apparatus and scoring require human/physical validation; no clinical normative conversion.",
                objects = Objects.Select(item => new { item.Index, massKg = AssessmentPhysics.Mass(scene, item.Body), initialPosition = Values(item.InitialPosition), initialRotation = new[] { item.InitialRotation.x, item.InitialRotation.y, item.InitialRotation.z, item.InitialRotation.w }, target = item.Target == null ? null : Values(item.Target.position), geoms = item.Geoms.Select(g => new { g.name, g.Mass, g.Density, contactDimensions = g.Settings.Solver.ConDim, contactTimeConstant = g.Settings.Solver.SolRef.TimeConst, sliding = g.Settings.Friction.Sliding, torsional = g.Settings.Friction.Torsional, rolling = g.Settings.Friction.Rolling }).ToArray(), springStiffness = item.Jaw == null ? 0 : item.Jaw.Settings.Spring.Stiffness }).ToArray()
            };
            File.WriteAllText(Path.Combine(LoggingManager.OutputDirectory, "assessment_" + TrialId + ".json"), JsonConvert.SerializeObject(manifest, Formatting.Indented));
        }

        static float[] Values(Vector3 value) => new[]
        {
            value.x,
            value.y,
            value.z
        };
        void AfterPhysics(object sender, MjStepArgs args)
        {
            if (!AssessmentPhysics.Ready(scene))
            {
                return;
            }

            if (!cacheReady)
            {
                CacheNativeIds();
            }

            if (State != TrialState.Running)
            {
                return;
            }

            double now = Now;
            // Check before overwriting the timestamp, including a stall followed by catch-up.
            if (!DiagnosticsOnly && now - lastStepHostTime > 0.5)
            {
                Finish(TrialState.Invalid, "physics_step_gap", now);
                return;
            }
            lastStepHostTime = now;
            double nativeTime = AssessmentPhysics.Time(scene);
            if (nativeTime <= lastNativeTime)
            {
                Finish(TrialState.Invalid, "native_time_not_advancing", now);
                return;
            }

            lastNativeTime = nativeTime;
            if (!clock.Accepts(now))
            {
                Finish(Kind == AssessmentKind.BoxAndBlock ? TrialState.Completed : TrialState.Failed, "time_limit", now);
                return;
            }

            if (configuration != ConfigurationStamp() || Mathf.Abs(UnityEngine.Time.fixedDeltaTime - PhysicsStepSeconds) > 0.00001f)
            {
                Finish(TrialState.Invalid, "configuration_changed", now);
                return;
            }

            if (!DiagnosticsOnly)
            {
                string reason = InputBlockingReason();
                if (reason.Length > 0)
                {
                    Finish(TrialState.Invalid, reason, now);
                    return;
                }
            }

            AssessmentPhysics.ReadContacts(scene, handGeoms, objectGeoms, rootBodies, sourceGeoms, targetGeoms, contacts);
            if (!IsPinTask)
            {
                ObserveHandCrossing();
            }

            for (int index = 0; index < Objects.Length && State == TrialState.Running; index++)
            {
                ObserveObject(index, now);
            }

            if (now >= nextObservation)
            {
                RecordObservations(now);
                nextObservation = now + 0.05; // 20 Hz export; scoring still observes every native step.
            }
        }

        // A destination-zone hit alone is insufficient. The actual trajectory must cross
        // the divider plane above the divider and within its front/back extent.
        bool CrossesPartition(Vector3 previous, Vector3 current, float clearance)
        {
            float from = Direction * previous.x, to = Direction * current.x;
            if (from <= 0 || to > 0)
            {
                return false;
            }

            Vector3 crossing = Vector3.Lerp(previous, current, from / (from - to));
            return crossing.y >= PartitionHeightMeters + clearance && Mathf.Abs(crossing.z) <= BoxDepthMeters * 0.5f;
        }

        void ObserveHandCrossing()
        {
            Vector3[] current = Fingertips.Select(tip => ApparatusFrame.InverseTransformPoint(tip.position)).ToArray();
            bool backAtSource = current.All(point => Direction * point.x > 0.025f);
            if (backAtSource && !sourceVisit)
            {
                trip++;
                sourceVisit = true;
            }

            for (int index = 0; index < current.Length; index++)
            {
                if (!CrossesPartition(previousFingertips[index], current[index], 0))
                {
                    continue;
                }

                crossedTrips.Add(trip);
                sourceVisit = false;
                Event("fingertip_crossed_partition", index, "trip=" + trip);
            }

            previousFingertips = current;
        }

        void ObserveObject(int index, double now)
        {
            AssessmentObject prop = Objects[index];
            ItemState item = items[index];
            AssessmentContact contact = contacts[index];
            item.Release.Observe(contact.Hand, now);
            if (contact.Hand && !item.PreviousContact)
            {
                Event("hand_contact_started", index, "native_contact");
            }

            if (!contact.Hand && item.PreviousContact)
            {
                Event("hand_contact_ended", index, "native_contact");
            }

            item.PreviousContact = contact.Hand;
            if (item.Evaluated)
            {
                return;
            }

            Vector3 position = ApparatusFrame.InverseTransformPoint(prop.transform.position);
            if (contact.Hand && Direction * position.x > 0.02f)
            {
                item.Trip = trip;
            }

            if (item.Release.WasTouched && (IsPinTask ? !contact.SourceSupport : position.y > BlockEdgeMeters * 0.8f))
            {
                item.LeftSource = true;
            }

            if (!IsPinTask && item.Release.WasTouched && CrossesPartition(item.PreviousPosition, position, BlockEdgeMeters * 0.5f - 0.002f))
            {
                item.Crossed = true;
                Event("object_crossed_partition", index, "trip=" + item.Trip);
            }

            item.PreviousPosition = position;
            bool released = item.Release.IsReleased(contact.Hand, now, ReleaseGraceSeconds);
            if (Kind == AssessmentKind.BoxAndBlock)
            {
                if (!item.Crossed || !released || !crossedTrips.Contains(item.Trip))
                {
                    return;
                }

                bool credited = blockScore.RegisterTransfer(index, item.Trip, true);
                item.Evaluated = true;
                Event("block_transferred", index, credited ? "point" : "same_trip_no_extra_point");
            }
            else if (Kind == AssessmentKind.TargetedBoxAndBlock)
            {
                bool dropped = item.LeftSource && position.y < -0.025f;
                if (!released || (!item.Crossed && !dropped))
                {
                    return;
                }

                if (index != targetedScore.ReleasedCount)
                {
                    Finish(TrialState.Invalid, "out_of_order_transport", now);
                    return;
                }

                bool placed = !dropped && FullyInsideTarget(prop);
                if (!dropped && !placed && now - item.Release.LastContactAt < 0.5)
                {
                    return; // Observe landing; retain release time for completion.
                }

                targetedScore.RegisterRelease(index, placed);
                item.Evaluated = true;
                Event("targeted_transport", index, placed ? "within_target" : "unsuccessful_transport");
                if (targetedScore.Complete)
                {
                    Finish(TrialState.Completed, "nine_blocks_released", item.Release.LastContactAt);
                }
            }
            else
            {
                if (item.LeftSource && released && position.y < 0.06f)
                {
                    Finish(TrialState.Failed, "pin_dropped_restart_requires_new_trial", now);
                    return;
                }

                bool clamped = item.LeftSource && released && contact.LowerJawOnTarget && contact.UpperJawOnTarget && !contact.SourceSupport && AssessmentPhysics.Velocity(scene, prop.FreeJoint).magnitude < 0.03f;
                if (!clamped)
                {
                    item.SupportedSince = -1;
                    return;
                }

                if (item.SupportedSince < 0)
                {
                    item.SupportedSince = now;
                }

                if (now - item.SupportedSince < PinSupportDwellSeconds)
                {
                    return;
                }

                item.Evaluated = true;
                Event("pin_physically_supported", index, "both_jaws_on_target_no_hand_contact");
                if (items.All(state => state.Evaluated))
                {
                    Finish(TrialState.Completed, "three_pins_supported", items.Max(state => state.Release.LastContactAt));
                }
            }
        }

        bool FullyInsideTarget(AssessmentObject prop)
        {
            if (AssessmentPhysics.Velocity(scene, prop.FreeJoint).magnitude > 0.15f)
            {
                return false;
            }

            float half = BlockEdgeMeters * 0.5f;
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 corner = prop.Target.InverseTransformPoint(prop.transform.TransformPoint(new Vector3(x, y, z) * half));
                        if (Mathf.Abs(corner.x) > TargetHalfWidthMeters || Mathf.Abs(corner.z) > TargetHalfDepthMeters || corner.y < -0.002f || corner.y > BlockEdgeMeters + 0.004f)
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        void RecordObservations(double now)
        {
            double host = Sim.SimClock.Now, nativeTime = AssessmentPhysics.Time(scene);
            for (int index = 0; index < Objects.Length; index++)
            {
                AssessmentObject prop = Objects[index];
                Vector3 p = prop.transform.position, v = AssessmentPhysics.Velocity(scene, prop.FreeJoint);
                Quaternion q = prop.transform.rotation;
                AssessmentContact c = contacts[index];
                LoggingManager.AddToBuffer("AssessmentObjects", Csv(TrialId, host, nativeTime, clock.Elapsed(now), index, p.x, p.y, p.z, q.x, q.y, q.z, q.w, v.x, v.y, v.z, c.Hand, c.SourceSupport, c.TargetSupport, c.HandNormalForce, prop.Jaw == null ? 0 : prop.Jaw.Configuration));
            }

            float Command(DOA axis) => Hand.DOA_mujoco.Where(binding => binding.General.doa == axis).Select(binding => binding.General.current_value).FirstOrDefault();
            Vector3 hand = Hand.hand.transform.position;
            LoggingManager.AddToBuffer("AssessmentHand", Csv(TrialId, host, nativeTime, hand.x, hand.y, hand.z, Command(DOA.HOC), Command(DOA.WFE), Command(DOA.WPS), Hand.InputIsFresh, Hand.UsedUdpTimestampSeconds));
        }

        void Update()
        {
            if (State != TrialState.Running)
            {
                return;
            }

            if (Now - lastStepHostTime > 0.5)
            {
                Finish(TrialState.Invalid, "physics_step_gap", Now);
            }
            else if (!clock.Accepts(Now))
            {
                Finish(Kind == AssessmentKind.BoxAndBlock ? TrialState.Completed : TrialState.Failed, "time_limit", Now);
            }
        }

        public void AbortTrial() => Finish(TrialState.Aborted, "operator_abort", Now);
        void OnApplicationFocus(bool focused)
        {
            if (!focused && !DiagnosticsOnly)
            {
                Finish(TrialState.Invalid, "application_focus_lost", Now);
            }
        }

        void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                Finish(TrialState.Invalid, "application_paused", Now);
            }
        }

        void Finish(TrialState state, string outcome, double endedAt)
        {
            if (State != TrialState.Running)
            {
                return;
            }

            clock.Finish(state, endedAt);
            LastOutcome = outcome;
            Event("trial_ended", -1, state + ":" + outcome);
            LoggingManager.AddToBuffer("AssessmentResults", Csv(TrialId, DiagnosticsOnly ? "diagnostics_excluded" : "assessment", ProtocolId, State, clock.Elapsed(Now), Score, UnsuccessfulTransports, outcome));
            LoggingManager.Flush();
        }

        public void ResetForNextTrial()
        {
            if (State == TrialState.Running || !AssessmentPhysics.Ready(scene))
            {
                return;
            }

            foreach (AssessmentObject prop in Objects)
            {
                AssessmentPhysics.ResetProp(scene, prop.FreeJoint, prop.InitialPosition, prop.InitialRotation, prop.Jaw, prop.InitialJawDegrees);
            }

            AssessmentPhysics.Refresh(scene);
            clock = null;
            LastOutcome = "Apparatus reset; awaiting operator start";
        }

        void Event(string name, int index, string detail) => LoggingManager.AddToBuffer("AssessmentEvents", Csv(TrialId, Sim.SimClock.Now, AssessmentPhysics.Time(scene), name, index, detail));
        static string Csv(params object[] values) => string.Join(",", values.Select(value => "\"" + Convert.ToString(value, CultureInfo.InvariantCulture).Replace("\"", "\"\"") + "\"")) + "\n";
        void OnDisable()
        {
            Finish(TrialState.Aborted, "scene_or_component_disabled", Now);
            if (scene != null)
            {
                scene.postUpdateEvent -= AfterPhysics;
            }
        }

        void OnDestroy()
        {
            UnityEngine.Time.fixedDeltaTime = previousFixedStep;
            Application.runInBackground = previousRunInBackground;
        }
    }
}
