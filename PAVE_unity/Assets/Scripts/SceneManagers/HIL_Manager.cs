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

public class HIL_Manager : MonoBehaviour
{
    [Serializable]
    /// <summary>
    /// Definition similar to PastaBoxManager.cs
    /// </summary>

    public enum HIL_mode
    {
        Train,
        Test
    }

    public enum HIL_env
    {
        TAC,
        LimbPos,
        Interact
    }

    public enum HIL_phases
    {
        None,
        Reach,
        Grasp,
        Transport,
        Release
    }

    public class InteractTaskConfig
    {
        public enum TbOris
        {
            straight,
            sideways
        }

        // whats the start orientation
        public GameObject startShelf;
        public TbOris startPose;
        public float tbYRotationStart;

        // what is the target orientation
        public GameObject targetShelf;
        public TbOris targetPose;
        public float tbYRotationTarget;

        public bool tbTouched;

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
    public class Stats
    {
        public bool RemapToIncomingRange;
        public Vector3 hdToTarget = new();
        public Vector3 hdToStart = new();
        public Vector3 TBToStart = new();
        public Vector3 TBToTarget = new();
        public float pathCompletionRatio = new();
        public Quaternion TBOrientation = new();
        public Quaternion ghstTBOrientation = new();
        public float orientationalDeviation = new();

        public Dictionary<DOA, diffDOA> activeDiffDOAs = new();

        public Stats(DOA[] doas)
        {
            foreach (DOA doa in doas)
            {
                activeDiffDOAs.Add(doa, new diffDOA(doa, -1, -1));
            }
        }

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
            public DOA doa;
            public float should;
            public float actual;
            public float diff;

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
    public struct PseudoFeedbackConfig
    {
        //public bool RemapToIncomingRange;
        public HIL_phases phase;
        public bool feedback;
        public DOA[] doas;
    }

    public enum Shelf
    {
        bot,
        mid,
        top
    }

    public HIL_mode currentMode;
    public HIL_env currentEnv;

    public InteractTaskConfig currentInteractTaskConfig;
    public HIL_phases currentPhase;
    public Stats currentStats;

    public GameObject shdwTb_weld;
    public GameObject shdwTb_geom;

    //[SerializeField]
    private GameObject[] cupboards;
    [SerializeField]
    private GameObject GhstHandWTb;
    [SerializeField]
    private GameObject GhstHand;
    private HandController GhstHandController;
    private GameObject GhstHandForearmMocap;
    [SerializeField]
    private GameObject PrsthHand;
    private HandController PrsthHandController;
    private GameObject palm;
    private GameObject palmGhst;
    [SerializeField]
    private GameObject TB;
    private Transform TB_OrientAligned;
    [SerializeField]
    private float GRASP_DISTANCE;
    [SerializeField]
    private float RELEASE_DISTANCE;
    [SerializeField]
    private float PCR_THRESHOLD;
    [SerializeField]
    private float ORIENTDEVIATION_THRESHOLD;
    [SerializeField]
    bool RemapStatsToIncomingRange;
    [SerializeField]
    private float WAIT_IN_TELEPORT;
    [SerializeField]
    public PseudoFeedbackConfig[] pseudoFeedbackConfig;
    public byte FB_Category;
    public byte FB_Subcategory;
    [SerializeField]
    bool ReinforcementLearning;
    private HIL_phases next_currPhase;

    [SerializeField]
    private int randomSeed = 42;


    private void Awake()
    {
        Application.targetFrameRate = 90;

        UnityEngine.Random.InitState(randomSeed);

        // create stats obj
        currentStats = new Stats(new DOA[] { DOA.HOC, DOA.WFE, DOA.WPS });
        currentStats.RemapToIncomingRange = RemapStatsToIncomingRange;

    }
    void Start()
    {

        // get the correct Ghost Hand
        if (currentEnv == HIL_env.Interact)
        {
            GhstHand = GhstHandWTb;
        }
        GhstHandController = GhstHand.GetComponentsInChildren<HandController>(includeInactive: false)
            .Where(c => c.enabled)
            .ToArray()[0];
        // get the Prsth hand
        PrsthHandController = PrsthHand.GetComponentsInChildren<HandController>(includeInactive: false)
            .Where(c => c.enabled)
            .ToArray()[0];

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
            cupboards = GameObject.FindGameObjectsWithTag("cupboard");
            cupboards = cupboards.OrderBy(go => go.name).ToArray();
        }

        TB_OrientAligned = TB.transform.Find("OrientAligned");

        currentPhase = HIL_phases.None;
        next_currPhase = HIL_phases.None;
    }


    // Update is called once per frame
    void Update()
    {
        // it is still null the first time
        if ((currentEnv == HIL_env.Interact && currentInteractTaskConfig != null) || (currentEnv != HIL_env.Interact))
        {
            // update current stats
            UpdateStats();

            // send out pseudo labels based on stats and activated phase config
            CheckSendPseudoLabelToLibEMG();
        }

        update_currentphase();  // updates currentPhase after sending PseudolabelToLibEMG -> terminal state is sent right away -> used to wait until phase with feedback

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

    private void PerformInteractLoop()
    {
        // ----- START OF HIL PHASES ----- // ToDo?: Event-based instead of checking every cycle
        switch (currentPhase)
        {
            case HIL_phases.None:

                // Start new random task config
                this.NewRandomInteractTaskConf();


                // move to next phase
                next_currPhase = HIL_phases.Reach;

                // add other things that need to be set up for Reach...

                break;

            case HIL_phases.Reach:

                // check if distance to start is smaller then threshold
                if (currentStats.hdToStart.magnitude <= GRASP_DISTANCE)
                {
                    next_currPhase = HIL_phases.Grasp;

                    // add other things that need to be set up for Grasp...
                    // - Send ghost hand to grasp
                    BtnSetGhostToTB();
                }
                break;

            case HIL_phases.Grasp:

                // Check if TB does not touch plate anymore => Switch to transport
                if (CollisionManager.FindCollisionByNames("Grasp_collider_box", currentInteractTaskConfig.startShelf.name, contains: true).Count == 0)
                {
                    next_currPhase = HIL_phases.Transport;

                    // add other things that need to be set up for Grasp...
                    // - Set ghost to target
                    BtnSetGhostToTarget();
                }

                // check if object was touched already 
                if (CollisionManager.FindCollisionBetweenTagAndObj("hand_collider", "ROI_collider_box").Count > 0)
                {
                    currentInteractTaskConfig.tbTouched = true;
                }

                // TODO find out if new new target should be set when user fails to grasp it correctly
                // Check for fails (when Hand leaves Grasp_collider_box after initial contact e.g. the object falls over etc)
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
                if (CollisionManager.FindCollisionBetweenTagAndObj("hand_collider", "Grasp_collider_box").Count == 0)
                {
                    // ToDo: successfull task completion should be:
                    // - box is released -> no colission
                    // - box is within certain threshold (pos. and orientation)

                    // Check collision shelve - box
                    if (CollisionManager.FindCollisionByNames("Grasp_collider_box", currentInteractTaskConfig.targetShelf.name, contains: true).Count > 0)
                    {
                        // Check box within certain threshold (position and orientation)
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

    private void PerformLimbPosLoop()
    {
        switch (currentPhase)
        {
            case HIL_phases.None:

                // Start new random task config
                this.NewRandomLimbPosTask(2);

                // move to next phase
                next_currPhase = HIL_phases.Reach;

                break;

            case HIL_phases.Reach:

                // check if distance to start is smaller then threshold
                if (currentStats.hdToStart.magnitude <= GRASP_DISTANCE)
                {
                    next_currPhase = HIL_phases.Grasp;
                    timeoutStart = StreamlinedInputManager.Now;

                }
                break;

            case HIL_phases.Grasp:
                // if timout or target reach generate new one => just timout for now
                if (StreamlinedInputManager.Now - timeoutStart > timeoutTime)
                {
                    next_currPhase = HIL_phases.None;
                    this.terminal_state = 1;                                    // added teminal state
                }

                break;

        }
    }
    public double timeoutTime;
    public double dwellTime;
    double timeoutStart;
    double dwellTimeStart;
    private void PerformTacLoop()
    {
        // ----- START OF HIL PHASES ----- Only None and Reach
        switch (currentPhase)
        {
            case HIL_phases.None:

                // Start new random task config
                this.NewRandomTacTask(2);

                // move to next phase
                next_currPhase = HIL_phases.Reach;

                // add other things that need to be set up for Reach...
                timeoutStart = StreamlinedInputManager.Now;

                break;

            case HIL_phases.Reach:

                // if timout or target reach generate new one => just timout for now
                // could add terminal signal here (within if statement)
                if (!curr_in_tgt(0.25))
                {
                    // reset dwell time
                    dwellTimeStart = StreamlinedInputManager.Now;
                }

                if (StreamlinedInputManager.Now - timeoutStart > timeoutTime)
                {
                    next_currPhase = HIL_phases.None;
                    this.terminal_state = 2;                                 // added teminal state (failure)
                }
                else if(StreamlinedInputManager.Now - dwellTimeStart >= dwellTime)
                {
                    next_currPhase = HIL_phases.None;
                    this.terminal_state = 1;                                 // added teminal state (success)
                }

                break;

        }
    }

    private GameObject GetShelfOfCupboard(GameObject cupboard, Shelf shelf)
    {
       return cupboard.GetNamedChild("level_" + shelf.ToString());
    }

    private void UpdateStats()
    {
        if ((currentEnv == HIL_env.Interact) || (currentEnv == HIL_env.LimbPos))
        {
            // update stats
            if (currentEnv == HIL_env.Interact)
            {
                // the interact task has a separat config to keep track of the more complex structure, use start and target shelfs
                currentStats.hdToStart = currentInteractTaskConfig.startShelf.transform.position - palm.transform.position;
                currentStats.hdToTarget = currentInteractTaskConfig.targetShelf.transform.position - palm.transform.position;
                
                // Compute path completion ratio
                Vector3 startShelveTopPos = currentInteractTaskConfig.startShelf.transform.position + new Vector3(0, currentInteractTaskConfig.startShelf.GetComponent<MjGeom>().Box.Extents.y, 0);
                Vector3 targetShelveTopPos = currentInteractTaskConfig.targetShelf.transform.position + new Vector3(0, currentInteractTaskConfig.targetShelf.GetComponent<MjGeom>().Box.Extents.y, 0);
                Vector3 TBBottomPos = this.TB.transform.position - this.TB.transform.rotation * new Vector3(0, this.TB.GetComponentInChildren<MjGeom>().Box.Extents.y, 0);
                currentStats.TBToStart = startShelveTopPos - TBBottomPos;
                currentStats.TBToTarget = targetShelveTopPos - TBBottomPos;
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
                currentStats.hdToStart = palm.transform.position - palmGhst.transform.position;
            }
        }

        var keys = currentStats.activeDiffDOAs.Keys.ToArray();
        foreach (var key in keys)
        {
            float? should = GhstHandController.GetValueForDOA(key, currentStats.RemapToIncomingRange);

            float? actual = PrsthHandController.GetValueForDOA(key, currentStats.RemapToIncomingRange);

            if (should != null)
            {
                // get should and actual // ToDo? Change to update instead of always creating new object
                currentStats.activeDiffDOAs[key] = new Stats.diffDOA(key, should.Value, actual.Value);
            }
        }
    }

    double terminal_state;
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
                    message[i++] = this.terminal_state;
                    this.terminal_state = 0;                                            // reset terminal state

                    if (currentPhase == HIL_phases.Transport || currentPhase == HIL_phases.Release)
                    {
                        // add feedback for box position
                        message[i++] = currentStats.pathCompletionRatio; //path_completion_ratio(startBoxPosition, actualBoxPosition, targetBoxPosition);
                    } else
                    {
                        message[i++] = 0; 
                    }

                    if (currentPhase == HIL_phases.Release) {

                        // add feedback for box pose
                        Quaternion actualBoxPose = TB.transform.rotation;                                   // check variable correct
                        Quaternion targetBoxPose = shdwTb_geom.transform.rotation;                          // check variable corrent

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
                SimUdpSender.SendArrayAsUDPmessage(array: message, dataType: (FB_Category, FB_Subcategory), sendWithLastUdpTs: true);
                //print(string.Join(",", message));
            }
        }
    }

    private IEnumerator TransformGhostHdToTarget()
    {
        if (currentInteractTaskConfig.targetPose == InteractTaskConfig.TbOris.straight)
        {
            // as long as this does not make any problems, set pos and ori directly
            this.shdwTb_weld.transform.position = currentInteractTaskConfig.targetShelf.transform.position + new Vector3(0, this.shdwTb_geom.GetComponentInChildren<MjGeom>().Box.Extents.y + currentInteractTaskConfig.targetShelf.GetComponent<MjGeom>().Box.Extents.y, 0);

            // set TB orientation 
            this.shdwTb_weld.transform.rotation = currentInteractTaskConfig.targetShelf.transform.rotation * Quaternion.Euler(0, currentInteractTaskConfig.tbYRotationTarget, 0);

            // rotate WFE in the other direction to counterbalance
            GhstHandController.OverwriteCurrVal(DOA.WFE, -currentInteractTaskConfig.tbYRotationTarget * Mathf.Deg2Rad);

            // rotate WPS back to 0
            GhstHandController.OverwriteCurrVal(DOA.WPS, 0);

            // open HOC to let go of box => ToDo: Check if this is needed for sideways
            GhstHandController.OverwriteCurrVal(DOA.HOC, 0);

        }
        else if (currentInteractTaskConfig.targetPose == InteractTaskConfig.TbOris.sideways)
        {
            // rotate kinematic chain
            this.shdwTb_weld.transform.rotation = currentInteractTaskConfig.targetShelf.transform.rotation * Quaternion.Euler(45, 0, 90);


            // change position and adjust for rotation
            this.shdwTb_weld.transform.position = currentInteractTaskConfig.targetShelf.transform.position + new Vector3(0, this.shdwTb_geom.GetComponentInChildren<MjGeom>().Box.Extents.x + currentInteractTaskConfig.targetShelf.GetComponent<MjGeom>().Box.Extents.y, 0);

            // rotate WPS into supination to counterbalance
            GhstHandController.OverwriteCurrVal(DOA.WPS, 1);

            // actuate WFE to get straight arm adjusted to cupboard level
            if (currentInteractTaskConfig.targetShelf.name.Contains("bot")) GhstHandController.OverwriteCurrVal(DOA.WFE, 0);
            else if (currentInteractTaskConfig.targetShelf.name.Contains("mid")) GhstHandController.OverwriteCurrVal(DOA.WFE, 0.5f);
            else if (currentInteractTaskConfig.targetShelf.name.Contains("top")) GhstHandController.OverwriteCurrVal(DOA.WFE, 1);


        }

        yield return null;
    }

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
        GhstHandController.OverwriteCurrVal(DOA.HOC, 0.6f);


        yield return null;
    }

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
        MeshRenderer[] meshRenderer = TB.transform.GetComponentsInChildren<MeshRenderer>();
        for (int i = 0; i < meshRenderer.Length; i++) meshRenderer[i].enabled = false;

        MjState.TeleportMjRoot(TB.GetComponentInChildren<MjFreeJoint>(), newPos, false);
        yield return new WaitForSeconds(WAIT_IN_TELEPORT); 
        MjState.TeleportMjRoot(TB.GetComponentInChildren<MjFreeJoint>(), newPos, newRot);
        for (int i = 0; i < meshRenderer.Length; i++) meshRenderer[i].enabled = true;

        yield return null;
    }

    private IEnumerator TransformGhstHandWForarmWeld(Vector3 pos, Quaternion ori)
    {

        GhstHandForearmMocap.transform.position = pos + new Vector3(0, 0.15f, 0) - ori * new Vector3(0,0,0.25f);
        GhstHandForearmMocap.transform.rotation = ori * Quaternion.Euler(90, 0, 0) * Quaternion.Euler(0, -90, 0);

        yield return null;
    }

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

    private GameObject GetRandomShelf(GameObject[] excludedShelfs = null)
    {
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

    public void NewRandomInteractTaskConf()
    {
        List<GameObject> excludedShelfs = new List<GameObject>();
        GameObject prevStartShelf = null;

        if (currentInteractTaskConfig != null)
        {
            excludedShelfs.Add(currentInteractTaskConfig.targetShelf); excludedShelfs.Add(currentInteractTaskConfig.startShelf);
        }
        (GameObject rdStartShelf, InteractTaskConfig.TbOris rdTbOrisStart, float rdYRotStart) = CreateRandomInteractConf(excludedShelfs.ToArray());
        excludedShelfs.Add(rdStartShelf);
        (GameObject rdTargetShelf, InteractTaskConfig.TbOris rdTbOrisTarget, float rdYRotTarget) = CreateRandomInteractConf(excludedShelfs.ToArray());

        // create a new config
        currentInteractTaskConfig = new InteractTaskConfig(rdStartShelf, rdTbOrisStart, rdYRotStart, rdTargetShelf, rdTbOrisTarget, rdYRotTarget);
        Debug.Log(JsonUtility.ToJson(currentInteractTaskConfig));

        StartCoroutine(TransformGhostHdToTarget());
        StartCoroutine(TransformTbToStart());
    }

    public void NewRandomTacTask(int numNew = 3)
    {
        // choose 1,2 or 3 from this:
        int countDoas = UnityEngine.Random.Range(1, numNew+1);

        List<DOA_mj> rdDoas = SelectRandomItems(new List<DOA_mj>(GhstHandController.DOA_mujoco), countDoas);

        // create a new task conf for the correct DOAs
        foreach (var doa in rdDoas)
        {
            // check if this doa should be changed continue OR set to zero
            if(!currentStats.activeDiffDOAs.Keys.Contains(doa.General.doa)) continue;

            // new random value in the doa range
            float value = UnityEngine.Random.Range(-1f, 1f);
            value = GhstHandController.RemapDOA(value, doa.General);

            GhstHandController.OverwriteCurrVal(doa.General.doa, value, true);
        }
    }

    public void NewRandomLimbPosTask(int numNew = 3)
    {
        // get a random shelf
        GameObject rdShelf = GetRandomShelf();

        // create and apply random TAC config
        NewRandomTacTask(numNew);

        // teleport ghost hand to target
        StartCoroutine(TransformGhstHandWForarmWeld(rdShelf.transform.position, rdShelf.transform.rotation));

    }

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

    public void BtnSetGhostToTB()
    {
        StartCoroutine(TransformGhostToStart());
    }

    public void BtnSetGhostToTarget()
    {
        StartCoroutine(TransformGhostHdToTarget());
    }

    // Some methods for reward calculations in transport phase
    private float calc_abs_distance(Vector3 current_pos, Vector3 goal_pos)
    {
        return Vector3.Distance(current_pos, goal_pos);
    }

    private float path_completion_ratio(Vector3 start_pos, Vector3 current_pos, Vector3 goal_pos)
    {
        Vector3 sc = current_pos - start_pos;
        Vector3 cg = goal_pos - current_pos;
        return sc.magnitude / (sc.magnitude + cg.magnitude);
    }

    private float distance_projection(Vector3 start_pos, Vector3 current_pos, Vector3 goal_pos, bool clip_negative=false)
    {
        Vector3 sc = current_pos - start_pos;
        Vector3 d = goal_pos - start_pos;
        float projection = Vector3.Dot(sc, d) / Vector3.Dot(d, d);
        if (clip_negative && projection < 0) return 0; 
        return projection;
    }

    private float calc_abs_orientation_difference(Quaternion orientation1, Quaternion orientation2)
    {
        float dot = Quaternion.Dot(orientation1, orientation2);
        dot = Mathf.Clamp(dot, -1f, 1f);                            // should not be necessary -> using unit quaternions
        return 1 - dot * dot;
    }

    private void update_currentphase()
    {
        // updates the current phase
        // set after sending feedback -> terminal state is sent
        // prior phase went from x -> none  => none did not send FB -> terminal state was only sent again when phase was grasp or other phase with FB
        currentPhase = next_currPhase;
    }

    private float computePathCompletionRatio()
    {
        return currentStats.TBToStart.magnitude / (currentStats.TBToStart.magnitude + currentStats.TBToTarget.magnitude);
    }

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
