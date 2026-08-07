using Mujoco;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Unity.VisualScripting;
using Unity.XR.CoreUtils;
using UnityEngine;
using static HIL_Manager.Stats;

/// <summary>
/// Central controller for the "Human-In-the-Loop" (HIL) training/test scenes.
///
/// This script drives a state machine (see <see cref="HIL_phases"/>) that runs a
/// pick-and-place / target-reaching task for a prosthetic hand ("Prsth") that is
/// compared against a "Ghost" hand (the ideal/target pose, driven either by a human
/// operator or by randomly generated targets). Every frame it:
///  1. Computes comparison statistics between the prosthetic hand and the ghost hand
///     (<see cref="UpdateStats"/>).
///  2. Optionally sends "pseudo labels" (feedback) over UDP to an external ML system
///     (LibEMG) so it can be used as a training/reward signal (<see cref="CheckSendPseudoLabelToLibEMG"/>).
///  3. Advances the phase state machine for whichever sub-environment is active
///     (TAC / LimbPos / Interact).
///
/// Three sub-environments (<see cref="HIL_env"/>) share this one manager:
///  - TAC: pure "reach a target DOA value" task (no object, no hand movement in space).
///  - LimbPos: reach task where the ghost hand also has to be positioned in space (limb position).
///  - Interact: full pick-and-place task with a transfer box (TB) that must be moved
///    from a start shelf to a target shelf, going through Reach -> Grasp -> Transport -> Release.
/// </summary>
public class HIL_Manager : MonoBehaviour
{
    [Serializable]
    /// <summary>
    /// Definition similar to PastaBoxManager.cs
    /// </summary>

    /// <summary>Whether the current session is collecting training data or is a graded test run.</summary>
    public enum HIL_mode
    {
        Train,
        Test
    }

    /// <summary>Which of the three sub-tasks/environments this manager is currently running.</summary>
    public enum HIL_env
    {
        TAC,        // "Target Acquisition Control" - just move a DOA value to a random target, no spatial movement
        LimbPos,    // Like TAC, but also requires moving the whole limb/hand to a target position
        Interact    // Full pick-and-place task with a physical transfer box (TB)
    }

    /// <summary>
    /// Stages of the pick-and-place task state machine. Not every environment uses every phase
    /// (TAC only uses None/Reach, LimbPos uses None/Reach/Grasp, Interact uses all of them).
    /// </summary>
    public enum HIL_phases
    {
        None,       // idle / between tasks - a new random task is generated when entering this phase
        Reach,      // hand is moving towards the start position/target
        Grasp,      // hand should be closing around the transfer box
        Transport,  // box is being carried from the start shelf towards the target shelf
        Release     // hand should be opening to place the box on the target shelf
    }

    /// <summary>
    /// Holds all the randomly generated configuration for one "Interact" trial:
    /// which shelf to start from/deliver to, and what orientation the transfer box (TB)
    /// should have at each of those shelves.
    /// </summary>
    public class InteractTaskConfig
    {
        /// <summary>Orientation the transfer box can be picked up / placed in.</summary>
        public enum TbOris
        {
            straight,   // box sits flat, aligned with the shelf
            sideways    // box is rotated 90 degrees (tests wrist pronation/supination, WFE/WPS)
        }

        // whats the start orientation
        public GameObject startShelf;      // shelf the transfer box starts on
        public TbOris startPose;           // orientation of the box at the start shelf
        public float tbYRotationStart;     // extra yaw (Y-axis) rotation applied at the start shelf

        // what is the target orientation
        public GameObject targetShelf;     // shelf the transfer box must be delivered to
        public TbOris targetPose;          // orientation of the box at the target shelf
        public float tbYRotationTarget;    // extra yaw (Y-axis) rotation applied at the target shelf

        public bool tbTouched;             // whether the hand has made contact with the box at least once during Grasp

        /// <summary>Bundles the randomly-picked start/target shelves, poses, and yaw rotations into one config object.</summary>
        public InteractTaskConfig(GameObject startShelf, TbOris startPose, float tbYRotationStart, GameObject targetShelf, TbOris targetPose, float tbYRotationTarget)
        {
            this.startShelf = startShelf;
            this.startPose = startPose;
            this.tbYRotationStart = tbYRotationStart;

            this.targetShelf = targetShelf;
            this.targetPose = targetPose;
            this.tbYRotationTarget = tbYRotationTarget;
            this.tbTouched = false;
        }
    }

    [Serializable]
    /// <summary>
    /// Snapshot of all the "how well is the prosthetic doing" measurements for the current frame:
    /// distances between the prosthetic hand and the ghost hand/targets, path completion ratio,
    /// orientation deviation of the transfer box, and per-DOA (degree of adjustment) differences
    /// between what the ghost hand is doing (should) and what the prosthetic hand is doing (actual).
    /// Recomputed every frame in <see cref="UpdateStats"/>.
    /// </summary>
    public class Stats
    {
        public bool RemapToIncomingRange;              // whether DOA values should be remapped to the incoming (raw) input range before comparing
        public Vector3 hdToTarget = new();             // vector from prosthetic hand (palm) to the target shelf/ghost hand
        public Vector3 hdToStart = new();               // vector from prosthetic hand (palm) to the start shelf/ghost hand
        public Vector3 TBToStart = new();               // vector from the transfer box's current position to where it should start
        public Vector3 TBToTarget = new();              // vector from the transfer box's current position to where it should end up
        public float pathCompletionRatio = new();       // 0..1 measure of how far along the start->target path the box currently is
        public Quaternion TBOrientation = new();        // current orientation of the real transfer box
        public Quaternion ghstTBOrientation = new();    // orientation of the "ghost"/target transfer box (the ideal placement)
        public float orientationalDeviation = new();    // how far TBOrientation is from ghstTBOrientation (0 = perfect match)

        // Per-degree-of-adjustment (DOA, e.g. hand open/close, wrist flex/extend, wrist pro/supination)
        // comparison between the ghost hand's value ("should") and the prosthetic hand's value ("actual").
        public Dictionary<DOA, diffDOA> activeDiffDOAs = new();

        /// <summary>Initializes the tracked DOA difference entries for the given set of degrees of adjustment.</summary>
        public Stats(DOA[] doas)
        {
            foreach (DOA doa in doas)
            {
                activeDiffDOAs.Add(doa, new diffDOA(doa, -1, -1));
            }
        }

        /// <summary>Human-readable dump of the current stats, used for debug logging/inspector display.</summary>
        public override string ToString()
        {
            string output = string.Empty;

            output += $"Dist HdToStart: {Math.Round(hdToStart.magnitude, 2)} (x: {Math.Round(hdToStart.x, 2)}, y: {Math.Round(hdToStart.y, 2)}, z: {Math.Round(hdToStart.z, 2)}) [m]" +
                $"\r\nDist HdToTargt: {Math.Round(hdToTarget.magnitude, 2)} (x: {Math.Round(hdToTarget.x, 2)}, y: {Math.Round(hdToTarget.y, 2)}, z: {Math.Round(hdToTarget.z, 2)}) [m]\r\n\r\n";

            foreach (var doadiff in activeDiffDOAs)
            {
                output += $"Diff {doadiff.Value.doa}: {Math.Round(doadiff.Value.diff, 2)} (a: {Math.Round(doadiff.Value.actual, 2)} / s: {Math.Round(doadiff.Value.should, 2)})\r\n";
            }
            return output;
        }

        [Serializable]
        /// <summary>
        /// Class for the comparison of degrees of freedom
        /// </summary>
        public class diffDOA
        {
            public DOA doa;        // which degree of adjustment this entry is about (e.g. Hand Open/Close, Wrist Flex/Extend, Wrist Pro/Supination)
            public float should;   // the "target"/ghost hand value for this DOA
            public float actual;   // the prosthetic hand's current value for this DOA
            public float diff;     // should - actual (signed error; 0 means the prosthetic matches the target exactly)

            /// <summary>Stores should/actual for one DOA and pre-computes the signed difference.</summary>
            public diffDOA(DOA doa, float should, float actual)
            {
                this.doa = doa;
                this.should = should;
                this.actual = actual;
                this.diff = should - actual;
            }
        }
    }

    [Serializable]
    /// <summary>
    /// Configures, per phase, whether pseudo-label feedback should be sent out over UDP for a
    /// given set of DOAs. Populated in the Inspector as an array so different phases can enable/
    /// disable feedback and choose which DOAs to report.
    /// </summary>
    public struct PseudoFeedbackConfig
    {
        //public bool RemapToIncomingRange;
        public HIL_phases phase;   // which phase this config applies to
        public bool feedback;      // whether feedback should actually be sent while in this phase
        public DOA[] doas;         // which DOAs to include in the feedback message for this phase
    }

    /// <summary>Vertical shelf level within a cupboard.</summary>
    public enum Shelf
    {
        bot,
        mid,
        top
    }

    public HIL_mode currentMode;   // Train vs Test - set in the Inspector/by the scene setup
    public HIL_env currentEnv;     // which sub-environment (TAC/LimbPos/Interact) is active

    public InteractTaskConfig currentInteractTaskConfig;  // the randomly generated start/target shelves+poses for the current Interact trial
    public HIL_phases currentPhase;                        // phase the state machine is currently in (applied at the end of Update via update_currentphase)
    public Stats currentStats;                             // this frame's computed comparison stats (see UpdateStats)

    public GameObject shdwTb_weld;     // "shadow"/ghost transfer box's kinematic weld root - moved to set where the ghost box should be
    public GameObject shdwTb_geom;     // "shadow"/ghost transfer box's geometry (used to read its size/orientation)

    //[SerializeField]
    private GameObject[] cupboards;            // all GameObjects tagged "cupboard" in the scene, sorted by name, used to randomly pick shelves from
    [SerializeField]
    private GameObject GhstHandWTb;            // ghost hand variant that comes already rigged with the transfer box (used in Interact env)
    [SerializeField]
    private GameObject GhstHand;               // the ghost hand GameObject actually used this session (swapped to GhstHandWTb for Interact)
    private HandController GhstHandController; // controller script driving the ghost hand's DOAs
    private GameObject GhstHandForearmMocap;   // ghost hand's forearm mocap tracker child object (only used in LimbPos env)
    [SerializeField]
    private GameObject PrsthHand;              // the prosthetic hand GameObject being evaluated
    private HandController PrsthHandController;// controller script driving/reading the prosthetic hand's DOAs
    private GameObject palm;                   // "palm" child object of the prosthetic hand, used as its reference point
    private GameObject palmGhst;               // "palm" child object of the ghost hand, used as its reference point
    [SerializeField]
    private GameObject TB;                     // the real, physically simulated transfer box
    private Transform TB_OrientAligned;         // child transform of TB used purely to read a "clean"/aligned orientation for comparisons
    [SerializeField]
    private float GRASP_DISTANCE;               // distance threshold (hand-to-start) below which the state machine switches Reach -> Grasp
    [SerializeField]
    private float RELEASE_DISTANCE;             // distance threshold (hand-to-target) below which the state machine switches Transport -> Release
    [SerializeField]
    private float PCR_THRESHOLD;                // minimum path completion ratio required (together with orientation) to count Release as a success
    [SerializeField]
    private float ORIENTDEVIATION_THRESHOLD;    // maximum orientation deviation allowed (together with PCR) to count Release as a success
    [SerializeField]
    bool RemapStatsToIncomingRange;             // Inspector toggle copied into currentStats.RemapToIncomingRange at Awake
    [SerializeField]
    private float WAIT_IN_TELEPORT;             // seconds to wait, with mesh renderers hidden, between the two-step teleport of the transfer box (avoids visual popping)
    [SerializeField]
    public PseudoFeedbackConfig[] pseudoFeedbackConfig;  // per-phase feedback configuration, set in the Inspector
    public byte FB_Category;       // UDP message "category" tag used when sending feedback (identifies message type to the receiver)
    public byte FB_Subcategory;    // UDP message "subcategory" tag used when sending feedback
    [SerializeField]
    bool ReinforcementLearning;    // if true, feedback messages are extended with terminal-state/path-completion/orientation info for RL reward shaping
    private HIL_phases next_currPhase;  // phase to switch to at the end of this frame (double-buffered so all phase logic this frame sees a consistent currentPhase)

    [SerializeField]
    private int randomSeed = 42;   // seed for Unity's RNG so trial sequences are reproducible across runs


    private void Awake()
    {
        // Lock the simulation/rendering to 90 fps (typical VR headset refresh rate)
        Application.targetFrameRate = 90;

        // Make all subsequent UnityEngine.Random calls reproducible for this run
        UnityEngine.Random.InitState(randomSeed);

        // create stats obj
        // Track differences for these 3 DOAs: Hand Open/Close, Wrist Flex/Extend, Wrist Pro/Supination
        currentStats = new Stats(new DOA[] { DOA.HOC, DOA.WFE, DOA.WPS });
        currentStats.RemapToIncomingRange = RemapStatsToIncomingRange;

    }
    void Start()
    {

        // get the correct Ghost Hand
        // Interact env needs the ghost hand variant that already has a transfer box attached
        if (currentEnv == HIL_env.Interact)
        {
            GhstHand = GhstHandWTb;
        }
        // Find the single enabled HandController among the ghost hand's children (there may be several, only one active)
        GhstHandController = GhstHand.GetComponentsInChildren<HandController>(includeInactive: false)
            .Where(c => c.enabled)
            .ToArray()[0];
        // get the Prsth hand
        // Same lookup, but for the prosthetic hand being evaluated
        PrsthHandController = PrsthHand.GetComponentsInChildren<HandController>(includeInactive: false)
            .Where(c => c.enabled)
            .ToArray()[0];

        // LimbPos needs to move the ghost hand's forearm/mocap anchor around in space, so cache it
        if (currentEnv == HIL_env.LimbPos)
        {
            GhstHandForearmMocap = Utils.RecursiveFindChild(GhstHand, "MocapTracker");
        }

        // get palm of Prsth
        palm = Utils.RecursiveFindChild(PrsthHand, "palm");

        // get palm of GhstHand
        palmGhst = Utils.RecursiveFindChild(GhstHand, "palm");


        if (currentEnv == HIL_env.Interact || currentEnv == HIL_env.LimbPos)
        {
            // get all cupboards
            // Collect every cupboard in the scene (tagged "cupboard") and sort by name so
            // random selection/exclusion logic behaves deterministically given the same seed
            cupboards = GameObject.FindGameObjectsWithTag("cupboard");
            cupboards = cupboards.OrderBy(go => go.name).ToArray();
        }

        // Cache the child transform used to read a clean/aligned orientation of the real transfer box
        TB_OrientAligned = TB.transform.Find("OrientAligned");

        // Start idle; the first Update() call will kick off a new random task
        currentPhase = HIL_phases.None;
        next_currPhase = HIL_phases.None;
    }


    // Update is called once per frame
    void Update()
    {
        // it is still null the first time
        // Guard against running stats/feedback before the very first Interact task config exists
        if ((currentEnv == HIL_env.Interact && currentInteractTaskConfig != null) || (currentEnv != HIL_env.Interact))
        {
            // update current stats
            UpdateStats();

            // send out pseudo labels based on stats and activated phase config
            CheckSendPseudoLabelToLibEMG();
        }

        update_currentphase();  // updates currentPhase after sending PseudolabelToLibEMG -> terminal state is sent right away -> used to wait until phase with feedback

        // Run the phase state machine for whichever sub-environment is active this session
        switch (currentEnv)
        {
            case HIL_env.TAC:
                PerformTacLoop();
                break;
            case HIL_env.LimbPos:
                PerformLimbPosLoop();
                break;
            case HIL_env.Interact:
                PerformInteractLoop();
                break;
            default:
                break;
        }




    }

    /// <summary>
    /// State machine for the full pick-and-place ("Interact") task:
    /// None -> Reach -> Grasp -> Transport -> Release -> (None again).
    /// Reads collisions and distances computed elsewhere (<see cref="currentStats"/>,
    /// <see cref="CollisionManager"/>) to decide when to advance, and sets
    /// <see cref="terminal_state"/> to signal success (1) or failure (2) back to LibEMG.
    /// </summary>
    private void PerformInteractLoop()
    {
        // ----- START OF HIL PHASES ----- // ToDo?: Event-based instead of checking every cycle
        switch (currentPhase)
        {
            case HIL_phases.None:

                // Start new random task config
                // Picks new random start/target shelves + box orientations and starts moving the ghost box/hand accordingly
                this.NewRandomInteractTaskConf();


                // move to next phase
                next_currPhase = HIL_phases.Reach;

                // add other things that need to be set up for Reach...

                break;

            case HIL_phases.Reach:

                // check if distance to start is smaller then threshold
                // Prosthetic hand has arrived close enough to the box's start position
                if (currentStats.hdToStart.magnitude <= GRASP_DISTANCE)
                {
                    next_currPhase = HIL_phases.Grasp;

                    // add other things that need to be set up for Grasp...
                    // - Send ghost hand to grasp
                    // Move the ghost hand into the "grasping the box at start" pose, to show the user the target grip
                    BtnSetGhostToTB();
                }
                break;

            case HIL_phases.Grasp:

                // Check if TB does not touch plate anymore => Switch to transport
                // Once the box has been lifted off the start shelf (no more collision with it), move to Transport
                if (CollisionManager.FindCollisionByNames("Grasp_collider_box", currentInteractTaskConfig.startShelf.name, contains: true).Count == 0)
                {
                    next_currPhase = HIL_phases.Transport;

                    // add other things that need to be set up for Grasp...
                    // - Set ghost to target
                    // Move the ghost hand/box preview ahead to the target shelf, showing where to deliver it
                    BtnSetGhostToTarget();
                }

                // check if object was touched already
                // Track whether the hand has ever made contact with the box, used below to distinguish "never touched" from "dropped after touching"
                if (CollisionManager.FindCollisionBetweenTagAndObj("hand_collider", "ROI_collider_box").Count > 0)
                {
                    currentInteractTaskConfig.tbTouched = true;
                }

                // TODO find out if new new target should be set when user fails to grasp it correctly
                // Check for fails (when Hand leaves Grasp_collider_box after initial contact e.g. the object falls over etc)
                // If the hand had touched the box but is no longer connected to it, the grasp attempt failed
                if (currentInteractTaskConfig.tbTouched && CollisionManager.FindCollisionBetweenTagAndObj("hand_collider", "Grasp_collider_box").Count == 0)
                {
                    print("phase: Object is not connected to hand anymore in Grasp phase");
                    // Go to None
                    next_currPhase = HIL_phases.None;
                    // terminal state indicating failure
                    this.terminal_state = 2;
                    break;
                }
                break;

            case HIL_phases.Transport:
                // as soon as the hand (with box) comes into close proximity of the target switch to Release
                if (currentStats.hdToTarget.magnitude <= RELEASE_DISTANCE)
                {
                    // switch to next: Release
                    next_currPhase = HIL_phases.Release;
                    break;
                }

                // If hand is not connected (check roi or grasp collidere) => Fail => Phase.None
                // Box was dropped mid-transport -> trial fails
                if (CollisionManager.FindCollisionBetweenTagAndObj("hand_collider", "Grasp_collider_box").Count == 0)
                {
                    print("phase: FAIL - Object is not connected to hand anymore in transport phase");

                    // Switch to phase none
                    next_currPhase = HIL_phases.None;
                    // terminal state indicating failure
                    this.terminal_state = 2;
                    break;
                }

                break;

            case HIL_phases.Release:
                // If hand is not connected (check roi or grasp collidere) => Fail => Phase.None
                // Box has left the hand (either placed correctly, or dropped) - figure out which
                if (CollisionManager.FindCollisionBetweenTagAndObj("hand_collider", "Grasp_collider_box").Count == 0)
                {
                    // ToDo: successfull task completion should be:
                    // - box is released -> no colission
                    // - box is within certain threshold (pos. and orientation)

                    // Check collision shelve - box
                    // Box must actually be resting on/touching the intended target shelf
                    if (CollisionManager.FindCollisionByNames("Grasp_collider_box", currentInteractTaskConfig.targetShelf.name, contains: true).Count > 0)
                    {
                        // Check box within certain threshold (position and orientation)
                        // ... and be close enough along the path + correctly oriented to count as a clean placement
                        if (currentStats.pathCompletionRatio > PCR_THRESHOLD && currentStats.orientationalDeviation < ORIENTDEVIATION_THRESHOLD)
                        {
                            // Switch to phase none
                            next_currPhase = HIL_phases.None;
                            // Successfull task completion
                            this.terminal_state = 1;
                            break;
                        }
                    }

                    print("phase: FAIL - Object is not connected to hand anymore in transport phase");

                    // Switch to phase none
                    // Box left the hand but wasn't validly placed (e.g. dropped, wrong shelf, wrong orientation) -> failure
                    next_currPhase = HIL_phases.None;
                    // terminal state indicating failure
                    this.terminal_state = 2;
                    break;
                }

                // ToDo see if we need to differantiate between success and failures => Maybe for rewards or user feedback



                break;

            default:

                break;
        }
    }

    /// <summary>
    /// State machine for the "LimbPos" task: reach a randomly placed target position/DOA
    /// combination, then either time out (failure handling not yet distinguished) or succeed.
    /// Only uses the None/Reach/Grasp phases.
    /// </summary>
    private void PerformLimbPosLoop()
    {
        switch (currentPhase)
        {
            case HIL_phases.None:

                // Start new random task config
                // "2" = randomize up to 2 DOAs as part of this task, in addition to the spatial target
                this.NewRandomLimbPosTask(2);

                // move to next phase
                next_currPhase = HIL_phases.Reach;

                break;

            case HIL_phases.Reach:

                // check if distance to start is smaller then threshold
                // Once close enough to the target position, move to Grasp and start a timeout timer
                if (currentStats.hdToStart.magnitude <= GRASP_DISTANCE)
                {
                    next_currPhase = HIL_phases.Grasp;
                    timeoutStart = StreamlinedInputManager.Now;

                }
                break;

            case HIL_phases.Grasp:
                // if timout or target reach generate new one => just timout for now
                // After holding position for timeoutTime seconds, consider the trial done and reset to None
                if (StreamlinedInputManager.Now - timeoutStart > timeoutTime)
                {
                    next_currPhase = HIL_phases.None;
                    this.terminal_state = 1;                                    // added teminal state
                }

                break;

        }
    }
    public double timeoutTime;     // seconds a Reach attempt has before it's considered timed out (TAC/LimbPos)
    public double dwellTime;       // seconds the target must be held within tolerance before TAC counts it as success
    double timeoutStart;           // timestamp (StreamlinedInputManager.Now) when the current timeout window started
    double dwellTimeStart;         // timestamp when the current in-target "dwell" window started
    /// <summary>
    /// State machine for the "TAC" task: move DOA values to a randomly generated target and
    /// hold them within tolerance for <see cref="dwellTime"/> seconds before the timeout expires.
    /// Only uses the None/Reach phases.
    /// </summary>
    private void PerformTacLoop()
    {
        // ----- START OF HIL PHASES ----- Only None and Reach
        switch (currentPhase)
        {
            case HIL_phases.None:

                // Start new random task config
                // "2" = randomize up to 2 DOAs as the target for this trial
                this.NewRandomTacTask(2);

                // move to next phase
                next_currPhase = HIL_phases.Reach;

                // add other things that need to be set up for Reach...
                timeoutStart = StreamlinedInputManager.Now;

                break;

            case HIL_phases.Reach:

                // if timout or target reach generate new one => just timout for now
                // could add terminal signal here (within if statement)
                // As soon as the DOAs fall outside tolerance, restart the "how long have we been in target" dwell timer
                if (!curr_in_tgt(0.25))
                {
                    // reset dwell time
                    dwellTimeStart = StreamlinedInputManager.Now;
                }

                if (StreamlinedInputManager.Now - timeoutStart > timeoutTime)
                {
                    // Ran out of time without holding the target long enough -> failure
                    next_currPhase = HIL_phases.None;
                    this.terminal_state = 2;                                 // added teminal state (failure)
                }
                else if(StreamlinedInputManager.Now - dwellTimeStart >= dwellTime)
                {
                    // Held within tolerance long enough -> success
                    next_currPhase = HIL_phases.None;
                    this.terminal_state = 1;                                 // added teminal state (success)
                }

                break;

        }
    }

    /// <summary>Finds the child object representing a specific shelf level ("level_bot/mid/top") on a cupboard.</summary>
    private GameObject GetShelfOfCupboard(GameObject cupboard, Shelf shelf)
    {
       return cupboard.GetNamedChild("level_" + shelf.ToString());
    }

    /// <summary>
    /// Recomputes <see cref="currentStats"/> for the current frame: hand-to-target distances,
    /// transfer-box path completion / orientation deviation (Interact only), hand-to-ghost
    /// distance (LimbPos only), and the per-DOA should/actual/diff values (all envs).
    /// </summary>
    private void UpdateStats()
    {
        if ((currentEnv == HIL_env.Interact) || (currentEnv == HIL_env.LimbPos))
        {
            // update stats
            if (currentEnv == HIL_env.Interact)
            {
                // the interact task has a separat config to keep track of the more complex structure, use start and target shelfs
                // Vector from the prosthetic palm to the start/target shelf positions
                currentStats.hdToStart = currentInteractTaskConfig.startShelf.transform.position - palm.transform.position;
                currentStats.hdToTarget = currentInteractTaskConfig.targetShelf.transform.position - palm.transform.position;

                // Compute path completion ratio
                // Top surface of the start/target shelf (shelf center + half its height)
                Vector3 startShelveTopPos = currentInteractTaskConfig.startShelf.transform.position + new Vector3(0, currentInteractTaskConfig.startShelf.GetComponent<MjGeom>().Box.Extents.y, 0);
                Vector3 targetShelveTopPos = currentInteractTaskConfig.targetShelf.transform.position + new Vector3(0, currentInteractTaskConfig.targetShelf.GetComponent<MjGeom>().Box.Extents.y, 0);
                // Bottom of the transfer box, accounting for its current rotation
                Vector3 TBBottomPos = this.TB.transform.position - this.TB.transform.rotation * new Vector3(0, this.TB.GetComponentInChildren<MjGeom>().Box.Extents.y, 0);
                currentStats.TBToStart = startShelveTopPos - TBBottomPos;
                currentStats.TBToTarget = targetShelveTopPos - TBBottomPos;
                // 0..1 ratio: how far along the straight-line start->target path the box currently sits
                currentStats.pathCompletionRatio = computePathCompletionRatio();

                // Compute Orientational devaiation
                // ToDo: ghstTB orient is not correct
                currentStats.TBOrientation = TB_OrientAligned.rotation;
                currentStats.ghstTBOrientation = shdwTb_geom.transform.rotation;
                currentStats.orientationalDeviation = calc_abs_orientation_difference(currentStats.TBOrientation, currentStats.ghstTBOrientation);
            }
            if (currentEnv == HIL_env.LimbPos)
            {
                // only have "start" target to switch to "Grasp" phase for sending feedback
                // Distance between the prosthetic hand's palm and the ghost hand's palm (the spatial target)
                currentStats.hdToStart = palm.transform.position - palmGhst.transform.position;
            }
        }

        // Recompute the should/actual/diff values for every tracked DOA (Hand Open/Close, Wrist Flex/Extend, Wrist Pro/Supination)
        var keys = currentStats.activeDiffDOAs.Keys.ToArray();
        foreach (var key in keys)
        {
            // "should" = what the ghost hand (target) is currently set to for this DOA
            float? should = GhstHandController.GetValueForDOA(key, currentStats.RemapToIncomingRange);

            // "actual" = what the prosthetic hand is currently reading for this DOA
            float? actual = PrsthHandController.GetValueForDOA(key, currentStats.RemapToIncomingRange);

            if (should != null)
            {
                // get should and actual // ToDo? Change to update instead of always creating new object
                currentStats.activeDiffDOAs[key] = new Stats.diffDOA(key, should.Value, actual.Value);
            }
        }
    }

    double terminal_state;  // 0 = ongoing, 1 = trial succeeded, 2 = trial failed; consumed and reset to 0 once sent in CheckSendPseudoLabelToLibEMG
    /// <summary>
    /// Builds and sends the UDP "pseudo label" feedback message to LibEMG for the current phase,
    /// if that phase is configured (in <see cref="pseudoFeedbackConfig"/>) to send feedback.
    /// The message layout is: [actual, should] pairs for each configured DOA, optionally followed
    /// by [terminal_state, path_completion_ratio, orientation_deviation] when
    /// <see cref="ReinforcementLearning"/> is enabled.
    /// </summary>
    private void CheckSendPseudoLabelToLibEMG()
    {
        // find current phase and send out feedback
        foreach (var phaseConfig in this.pseudoFeedbackConfig)
        {
            if (currentPhase == phaseConfig.phase && phaseConfig.feedback)
            {
                // check how many doas phaseconfig has
                int doaCount = phaseConfig.doas != null ? phaseConfig.doas.Length : 0;

                // Theres no difference btw. FB elements -> always returns same hardcoded stuff
                // Current stuff is okay for grasp phase (and partially release phase)
                // create message array of correct size
                // 2 values (actual, should) per DOA, plus 3 extra values (terminal_state, path completion, orientation) if RL is on
                double[] message = new double[doaCount * 2 + (ReinforcementLearning ? 3 : 0)];

                int i = 0;
                if (doaCount > 0) {
                    foreach (DOA doa in phaseConfig.doas)
                    {
                        message[i++] = currentStats.activeDiffDOAs[doa].actual;
                        message[i++] = currentStats.activeDiffDOAs[doa].should;
                    }
                }

                if (ReinforcementLearning) {
                    // Append terminal state (0=ongoing, 1=success, 2=failure) then reset it so it's only reported once
                    message[i++] = this.terminal_state;
                    this.terminal_state = 0;                                            // reset terminal state

                    if (currentPhase == HIL_phases.Transport || currentPhase == HIL_phases.Release)
                    {
                        // add feedback for box position
                        // Only meaningful once the box is actually being carried
                        message[i++] = currentStats.pathCompletionRatio; //path_completion_ratio(startBoxPosition, actualBoxPosition, targetBoxPosition);
                    } else
                    {
                        message[i++] = 0;
                    }

                    if (currentPhase == HIL_phases.Release) {

                        // add feedback for box pose
                        Quaternion actualBoxPose = TB.transform.rotation;                                   // check variable correct
                        Quaternion targetBoxPose = shdwTb_geom.transform.rotation;                          // check variable corrent

                        // Only meaningful during Release, when the box needs to match the target orientation
                        message[i++] = currentStats.orientationalDeviation; // calc_abs_orientation_difference(actualBoxPose, targetBoxPose);
                    }
                    else
                    {
                        message[i++] = 0;
                    }
                }

                /*
                // ------------ ToDO to be made nicer --------------------
                // hardcoded pseudolabel structure for libEMG as one array
                double[] message;
                if (this.sendTermialState)
                {
                    message = new double[] { currentStats.activeDiffDOAs[DOA.HOC].actual, currentStats.activeDiffDOAs[DOA.HOC].should, currentStats.activeDiffDOAs[DOA.WFE].actual,
                    currentStats.activeDiffDOAs[DOA.WFE].should, currentStats.activeDiffDOAs[DOA.WPS].actual, currentStats.activeDiffDOAs[DOA.WPS].should, this.terminal_state };
                    this.terminal_state = 0;                                    // reset terminal state back to 0 if set to 1 or 2
                }
                else
                {
                    message = new double[] { currentStats.activeDiffDOAs[DOA.HOC].actual, currentStats.activeDiffDOAs[DOA.HOC].should, currentStats.activeDiffDOAs[DOA.WFE].actual,
                    currentStats.activeDiffDOAs[DOA.WFE].should, currentStats.activeDiffDOAs[DOA.WPS].actual, currentStats.activeDiffDOAs[DOA.WPS].should };
                }
                */

                //Send Stats
                // Fire the assembled array off as a UDP packet, tagged with category/subcategory so the receiver knows how to parse it
                SimUdpSender.SendArrayAsUDPmessage(array: message, dataType: (FB_Category, FB_Subcategory), sendWithLastUdpTs: true);
                //print(string.Join(",", message));
            }
        }
    }

    /// <summary>
    /// Moves the ghost hand/box preview ("shadow" transfer box) to the target shelf, in either the
    /// "straight" or "sideways" orientation, and drives the ghost hand's DOAs to match that pose.
    /// Runs as a coroutine (single-frame, `yield return null`) so it can be started via StartCoroutine.
    /// </summary>
    private IEnumerator TransformGhostHdToTarget()
    {
        if (currentInteractTaskConfig.targetPose == InteractTaskConfig.TbOris.straight)
        {
            // as long as this does not make any problems, set pos and ori directly
            // Place the shadow box directly on top of the target shelf
            this.shdwTb_weld.transform.position = currentInteractTaskConfig.targetShelf.transform.position + new Vector3(0, this.shdwTb_geom.GetComponentInChildren<MjGeom>().Box.Extents.y + currentInteractTaskConfig.targetShelf.GetComponent<MjGeom>().Box.Extents.y, 0);

            // set TB orientation
            this.shdwTb_weld.transform.rotation = currentInteractTaskConfig.targetShelf.transform.rotation * Quaternion.Euler(0, currentInteractTaskConfig.tbYRotationTarget, 0);

            // rotate WFE in the other direction to counterbalance
            // Compensate the ghost hand's wrist flex/extend so the hand still looks natural holding a rotated box
            GhstHandController.OverwriteCurrVal(DOA.WFE, -currentInteractTaskConfig.tbYRotationTarget * Mathf.Deg2Rad);

            // rotate WPS back to 0
            GhstHandController.OverwriteCurrVal(DOA.WPS, 0);

            // open HOC to let go of box => ToDo: Check if this is needed for sideways
            // Fully open the hand (Hand Open/Close = 0) to depict "releasing" the box
            GhstHandController.OverwriteCurrVal(DOA.HOC, 0);

        }
        else if (currentInteractTaskConfig.targetPose == InteractTaskConfig.TbOris.sideways)
        {
            // rotate kinematic chain
            // Rotate the shadow box 90 degrees onto its side (45,0,90 fixed offset combined with the shelf's own rotation)
            this.shdwTb_weld.transform.rotation = currentInteractTaskConfig.targetShelf.transform.rotation * Quaternion.Euler(45, 0, 90);


            // change position and adjust for rotation
            // Note: uses Extents.x here (box's now-vertical side length after the 90-degree rotation) instead of Extents.y
            this.shdwTb_weld.transform.position = currentInteractTaskConfig.targetShelf.transform.position + new Vector3(0, this.shdwTb_geom.GetComponentInChildren<MjGeom>().Box.Extents.x + currentInteractTaskConfig.targetShelf.GetComponent<MjGeom>().Box.Extents.y, 0);

            // rotate WPS into supination to counterbalance
            GhstHandController.OverwriteCurrVal(DOA.WPS, 1);

            // actuate WFE to get straight arm adjusted to cupboard level
            // Pick a wrist flex/extend value depending on which shelf height is being targeted
            if (currentInteractTaskConfig.targetShelf.name.Contains("bot")) GhstHandController.OverwriteCurrVal(DOA.WFE, 0);
            else if (currentInteractTaskConfig.targetShelf.name.Contains("mid")) GhstHandController.OverwriteCurrVal(DOA.WFE, 0.5f);
            else if (currentInteractTaskConfig.targetShelf.name.Contains("top")) GhstHandController.OverwriteCurrVal(DOA.WFE, 1);


        }

        yield return null;
    }

    /// <summary>
    /// Moves the ghost hand/box preview to the *start* shelf (mirror of
    /// <see cref="TransformGhostHdToTarget"/>) and closes the ghost hand's fingers around it,
    /// depicting the grasp the user should perform.
    /// </summary>
    private IEnumerator TransformGhostToStart()
    {
        if (currentInteractTaskConfig.startPose == InteractTaskConfig.TbOris.straight)
        {
            // as long as this does not make any problems, set pos and ori directly
            this.shdwTb_weld.transform.position = currentInteractTaskConfig.startShelf.transform.position + new Vector3(0, this.shdwTb_geom.GetComponentInChildren<MjGeom>().Box.Extents.y + currentInteractTaskConfig.targetShelf.GetComponent<MjGeom>().Box.Extents.y, 0);

            // set TB orientation
            this.shdwTb_weld.transform.rotation = currentInteractTaskConfig.startShelf.transform.rotation * Quaternion.Euler(0, currentInteractTaskConfig.tbYRotationStart, 0);

            // rotate WFE in the other direction to counterbalance
            GhstHandController.OverwriteCurrVal(DOA.WFE, -currentInteractTaskConfig.tbYRotationStart * Mathf.Deg2Rad);

            // rotate WPS back to 0
            GhstHandController.OverwriteCurrVal(DOA.WPS, 0);

        }
        else if (currentInteractTaskConfig.startPose == InteractTaskConfig.TbOris.sideways)
        {
            // rotate kinematic chain
            this.shdwTb_weld.transform.rotation = currentInteractTaskConfig.startShelf.transform.rotation * Quaternion.Euler(45, 0, 90);


            // change position and adjust for rotation
            this.shdwTb_weld.transform.position = currentInteractTaskConfig.startShelf.transform.position + new Vector3(0, this.shdwTb_geom.GetComponentInChildren<MjGeom>().Box.Extents.x + currentInteractTaskConfig.targetShelf.GetComponent<MjGeom>().Box.Extents.y, 0);

            // rotate WPS into supination to counterbalance
            GhstHandController.OverwriteCurrVal(DOA.WPS, 90 * Mathf.Deg2Rad);

            //GhstHandController.OverwriteCurrVal(DOA.WFE, 57f * Mathf.Deg2Rad);
            // actuate WFE to get straight arm adjusted to cupboard level
            if (currentInteractTaskConfig.targetShelf.name.Contains("bot")) GhstHandController.OverwriteCurrVal(DOA.WFE, 0);
            else if (currentInteractTaskConfig.targetShelf.name.Contains("mid")) GhstHandController.OverwriteCurrVal(DOA.WFE, 0.5f);
            else if (currentInteractTaskConfig.targetShelf.name.Contains("top")) GhstHandController.OverwriteCurrVal(DOA.WFE, 1);
        }

        // Close around TB
        // Partially close the ghost hand's fingers (Hand Open/Close = 0.6) to show gripping the box
        GhstHandController.OverwriteCurrVal(DOA.HOC, 0.6f);


        yield return null;
    }

    /// <summary>
    /// Teleports the real, physically-simulated transfer box (TB) to the start shelf for a new trial.
    /// Hides its mesh renderers, teleports it in two steps (position first, then position+rotation,
    /// with a short wait in between) to avoid a visible pop/flicker as MuJoCo settles the body, then
    /// re-enables rendering.
    /// </summary>
    private IEnumerator TransformTbToStart()
    {
        Vector3 newPos = new();
        Quaternion newRot = new();

        if (currentInteractTaskConfig.startPose == InteractTaskConfig.TbOris.straight)
        {
            // as long as this does not make any problems, set pos and ori directly // using shdwTb geom because it consists of a single box
            newPos = currentInteractTaskConfig.startShelf.transform.position + new Vector3(0, this.shdwTb_geom.GetComponentInChildren<MjGeom>().Box.Extents.y + currentInteractTaskConfig.startShelf.GetComponent<MjGeom>().Box.Extents.y+0.1f, 0);

            // set TB orientation
            newRot = currentInteractTaskConfig.startShelf.transform.rotation * Quaternion.Euler(0, currentInteractTaskConfig.tbYRotationStart, 0);
        }
        else if (currentInteractTaskConfig.startPose == InteractTaskConfig.TbOris.sideways)
        {
            // rotate kinematic chain
            newRot = currentInteractTaskConfig.startShelf.transform.rotation * Quaternion.Euler(45, 0, 90);

            // change position and adjust for rotation
            newPos = currentInteractTaskConfig.startShelf.transform.position + new Vector3(0, this.shdwTb_geom.GetComponentInChildren<MjGeom>().Box.Extents.x + currentInteractTaskConfig.startShelf.GetComponent<MjGeom>().Box.Extents.y+0.1f, 0);
        }
        // Hide the box while it's being teleported so the player doesn't see it snap/jump
        MeshRenderer[] meshRenderer = TB.transform.GetComponentsInChildren<MeshRenderer>();
        for (int i = 0; i < meshRenderer.Length; i++) meshRenderer[i].enabled = false;

        // Step 1: move only position (rotation=false) so MuJoCo's physics can settle without a large rotational impulse
        MjState.TeleportMjRoot(TB.GetComponentInChildren<MjFreeJoint>(), newPos, false);
        yield return new WaitForSeconds(WAIT_IN_TELEPORT);
        // Step 2: now apply the final position+rotation together
        MjState.TeleportMjRoot(TB.GetComponentInChildren<MjFreeJoint>(), newPos, newRot);
        // Reveal the box again now that it's in its final pose
        for (int i = 0; i < meshRenderer.Length; i++) meshRenderer[i].enabled = true;

        yield return null;
    }

    /// <summary>
    /// Positions the ghost hand's forearm mocap anchor at a given world position/orientation
    /// (used by LimbPos to place the spatial target for the hand to reach).
    /// </summary>
    private IEnumerator TransformGhstHandWForarmWeld(Vector3 pos, Quaternion ori)
    {

        // Offset the anchor slightly up and back from the target so the hand lines up naturally over it
        GhstHandForearmMocap.transform.position = pos + new Vector3(0, 0.15f, 0) - ori * new Vector3(0,0,0.25f);
        GhstHandForearmMocap.transform.rotation = ori * Quaternion.Euler(90, 0, 0) * Quaternion.Euler(0, -90, 0);

        yield return null;
    }

    /// <summary>
    /// Picks a random shelf (excluding any in <paramref name="excludedShelfs"/>) plus a random
    /// box orientation and random yaw rotation, for use as either the start or target of a new
    /// Interact trial.
    /// </summary>
    public (GameObject, InteractTaskConfig.TbOris, float) CreateRandomInteractConf(GameObject[] excludedShelfs = null)
    {
        // get random shelf
        GameObject shelf = GetRandomShelf(excludedShelfs);

        // create random pose and z rotation fitted to interact task
        int orisLen = Enum.GetValues(typeof(InteractTaskConfig.TbOris)).Length;
        InteractTaskConfig.TbOris ori = (InteractTaskConfig.TbOris)UnityEngine.Random.Range(0, orisLen);

        // ToDo: Change this to actual range of motion and fitting WFE
        int yRot = UnityEngine.Random.Range(-45, 45);

        return (shelf, ori, yRot);
    }

    /// <summary>
    /// Picks a random shelf from a random cupboard, retrying until it finds one that isn't in
    /// <paramref name="excludedShelfs"/> (used to avoid picking the same shelf as start and target).
    /// </summary>
    private GameObject GetRandomShelf(GameObject[] excludedShelfs = null)
    {
        if (excludedShelfs == null) excludedShelfs = new GameObject[0];

        // get cupboard
        GameObject cupboard = cupboards[UnityEngine.Random.Range(0, cupboards.Length)];

        GameObject shelf = null;
        // get shelf that is not the excluded shelf
        do
        {
            int shelfsLen = Enum.GetValues(typeof(Shelf)).Length;
            Shelf rdShelf = (Shelf)UnityEngine.Random.Range(0, shelfsLen);  // 0 to 3
            shelf = GetShelfOfCupboard(cupboard, rdShelf);
        }
        while (excludedShelfs.Contains(shelf));

        return shelf;
    }

    /// <summary>
    /// Generates a brand new random Interact trial: picks a start shelf and a different target
    /// shelf (also excluding the previous trial's shelves), builds the <see cref="InteractTaskConfig"/>,
    /// and kicks off the coroutines that move the ghost preview to the target and teleport the real
    /// transfer box to the start.
    /// </summary>
    public void NewRandomInteractTaskConf()
    {
        List<GameObject> excludedShelfs = new List<GameObject>();
        GameObject prevStartShelf = null;

        if (currentInteractTaskConfig != null)
        {
            // Avoid repeating the previous trial's start/target shelves back-to-back
            excludedShelfs.Add(currentInteractTaskConfig.targetShelf); excludedShelfs.Add(currentInteractTaskConfig.startShelf);
        }
        (GameObject rdStartShelf, InteractTaskConfig.TbOris rdTbOrisStart, float rdYRotStart) = CreateRandomInteractConf(excludedShelfs.ToArray());
        // Also make sure the target shelf differs from the newly-picked start shelf
        excludedShelfs.Add(rdStartShelf);
        (GameObject rdTargetShelf, InteractTaskConfig.TbOris rdTbOrisTarget, float rdYRotTarget) = CreateRandomInteractConf(excludedShelfs.ToArray());

        // create a new config
        currentInteractTaskConfig = new InteractTaskConfig(rdStartShelf, rdTbOrisStart, rdYRotStart, rdTargetShelf, rdTbOrisTarget, rdYRotTarget);
        Debug.Log(JsonUtility.ToJson(currentInteractTaskConfig));

        // Show the ghost preview at the target shelf, and physically move the real box to the start shelf
        StartCoroutine(TransformGhostHdToTarget());
        StartCoroutine(TransformTbToStart());
    }

    /// <summary>
    /// Generates a new random TAC trial by picking up to <paramref name="numNew"/> random DOAs
    /// from the ghost hand's available DOAs and setting each to a new random target value
    /// (remapped into that DOA's valid range).
    /// </summary>
    public void NewRandomTacTask(int numNew = 3)
    {
        // choose 1,2 or 3 from this:
        int countDoas = UnityEngine.Random.Range(1, numNew+1);

        List<DOA_mj> rdDoas = SelectRandomItems(new List<DOA_mj>(GhstHandController.DOA_mujoco), countDoas);

        // create a new task conf for the correct DOAs
        foreach (var doa in rdDoas)
        {
            // check if this doa should be changed continue OR set to zero
            // Skip DOAs that aren't part of the ones actually being tracked/compared this session
            if(!currentStats.activeDiffDOAs.Keys.Contains(doa.General.doa)) continue;

            // new random value in the doa range
            float value = UnityEngine.Random.Range(-1f, 1f);
            value = GhstHandController.RemapDOA(value, doa.General);

            GhstHandController.OverwriteCurrVal(doa.General.doa, value, true);
        }
    }

    /// <summary>
    /// Generates a new random LimbPos trial: picks a random shelf as the spatial target, applies
    /// a random TAC-style DOA target on top of it, and moves the ghost hand's forearm anchor to
    /// that shelf's position/orientation.
    /// </summary>
    public void NewRandomLimbPosTask(int numNew = 3)
    {
        // get a random shelf
        GameObject rdShelf = GetRandomShelf();

        // create and apply random TAC config
        NewRandomTacTask(numNew);

        // teleport ghost hand to target
        StartCoroutine(TransformGhstHandWForarmWeld(rdShelf.transform.position, rdShelf.transform.rotation));

    }

    /// <summary>
    /// Randomly selects <paramref name="count"/> distinct items from <paramref name="sourceList"/>
    /// without replacement (Fisher-Yates-style pick-and-remove). Clamps count down and warns if
    /// more items are requested than exist in the source list.
    /// </summary>
    public List<T> SelectRandomItems<T>(List<T> sourceList, int count)
    {
        // Create a temporary list to avoid modifying the original source list
        List<T> tempPool = new List<T>(sourceList);
        List<T> result = new List<T>();

        // Ensure we don't try to select more items than available in the list
        if (count > tempPool.Count)
        {
            Debug.LogWarning("Attempted to select more items than available in the list. Selecting all available items.");
            count = tempPool.Count;
        }

        for (int i = 0; i < count; i++)
        {
            // Generate a random index within the current bounds of the temporary list
            int randomIndex = UnityEngine.Random.Range(0, tempPool.Count);

            // Add the item at the random index to the result list
            result.Add(tempPool[randomIndex]);

            // Remove the selected item from the temporary list to prevent duplicates
            tempPool.RemoveAt(randomIndex);
        }

        return result;
    }

    /// <summary>UI button hook: generates a new random trial for whichever environment is currently active.</summary>
    public void BtnNewConfig()
    {
        if (currentEnv == HIL_env.TAC)
        {
            NewRandomTacTask();
        }

        else if (currentEnv == HIL_env.LimbPos)
        {
            NewRandomLimbPosTask();
        }

        else if (currentEnv == HIL_env.Interact)
        {
            NewRandomInteractTaskConf();
        }
    }

    /// <summary>UI button hook: moves the ghost hand/box preview to the start shelf (grasp pose).</summary>
    public void BtnSetGhostToTB()
    {
        StartCoroutine(TransformGhostToStart());
    }

    /// <summary>UI button hook: moves the ghost hand/box preview to the target shelf (release pose).</summary>
    public void BtnSetGhostToTarget()
    {
        StartCoroutine(TransformGhostHdToTarget());
    }

    // Some methods for reward calculations in transport phase
    /// <summary>Plain Euclidean distance between two positions. Currently unused directly (kept for reward-shaping experiments).</summary>
    private float calc_abs_distance(Vector3 current_pos, Vector3 goal_pos)
    {
        return Vector3.Distance(current_pos, goal_pos);
    }

    /// <summary>
    /// Alternate/legacy path-completion calculation (0..1) using explicit start/current/goal
    /// positions rather than the TBToStart/TBToTarget stats. Superseded by
    /// <see cref="computePathCompletionRatio"/> but kept for reference/reward-shaping experiments.
    /// </summary>
    private float path_completion_ratio(Vector3 start_pos, Vector3 current_pos, Vector3 goal_pos)
    {
        Vector3 sc = current_pos - start_pos;
        Vector3 cg = goal_pos - current_pos;
        return sc.magnitude / (sc.magnitude + cg.magnitude);
    }

    /// <summary>
    /// Projects the current position onto the start->goal line and returns how far along that
    /// line (as a fraction, can exceed 0..1) the projection falls. Optionally clips negative
    /// values (before the start) to 0. Currently unused directly (kept for reward-shaping experiments).
    /// </summary>
    private float distance_projection(Vector3 start_pos, Vector3 current_pos, Vector3 goal_pos, bool clip_negative=false)
    {
        Vector3 sc = current_pos - start_pos;
        Vector3 d = goal_pos - start_pos;
        float projection = Vector3.Dot(sc, d) / Vector3.Dot(d, d);
        if (clip_negative && projection < 0) return 0;
        return projection;
    }

    /// <summary>
    /// Returns an orientation-difference metric between two quaternions in the range [0, 1]:
    /// 0 means identical orientation, 1 means maximally different (90 degrees apart).
    /// Based on 1 - dot^2, which is a common smooth (branch-free) rotation-distance metric.
    /// </summary>
    private float calc_abs_orientation_difference(Quaternion orientation1, Quaternion orientation2)
    {
        float dot = Quaternion.Dot(orientation1, orientation2);
        dot = Mathf.Clamp(dot, -1f, 1f);                            // should not be necessary -> using unit quaternions
        return 1 - dot * dot;
    }

    /// <summary>
    /// Applies the phase decided during this frame's state-machine update
    /// (<see cref="next_currPhase"/>) to <see cref="currentPhase"/>. Deliberately called *after*
    /// <see cref="CheckSendPseudoLabelToLibEMG"/> in <see cref="Update"/> so that a terminal-state
    /// message gets sent while still "in" the phase that triggered it, rather than after already
    /// having moved to the next phase.
    /// </summary>
    private void update_currentphase()
    {
        // updates the current phase
        // set after sending feedback -> terminal state is sent
        // prior phase went from x -> none  => none did not send FB -> terminal state was only sent again when phase was grasp or other phase with FB
        currentPhase = next_currPhase;
    }

    /// <summary>
    /// Computes how far along the start->target path the transfer box currently is, as a 0..1
    /// ratio, using the cached <see cref="Stats.TBToStart"/>/<see cref="Stats.TBToTarget"/> vectors:
    /// distance-from-start / (distance-from-start + distance-to-target).
    /// </summary>
    private float computePathCompletionRatio()
    {
        return currentStats.TBToStart.magnitude / (currentStats.TBToStart.magnitude + currentStats.TBToTarget.magnitude);
    }

    /// <summary>
    /// Checks whether every tracked DOA's should/actual difference is within
    /// <paramref name="tolerance"/> - i.e. whether the prosthetic hand currently matches the
    /// ghost/target hand closely enough to count as "in target" (used by the TAC dwell-time check).
    /// </summary>
    private bool curr_in_tgt(double tolerance)
    {
        // check if n_dof are within thire target
        if (currentStats?.activeDiffDOAs != null)
        {
            foreach (var doa in currentStats.activeDiffDOAs)
            {
                if(Math.Abs(doa.Value.diff) <= tolerance)
                {
                    continue;
                } else
                {
                    return false;
                }
            }
        }
        return true;
    }
}
