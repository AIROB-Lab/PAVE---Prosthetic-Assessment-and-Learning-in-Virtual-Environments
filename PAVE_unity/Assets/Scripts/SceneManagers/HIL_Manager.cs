using Mujoco;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Net.NetworkInformation;
using Unity.VisualScripting;
using Unity.XR.CoreUtils;
using UnityEngine;
using static PastaBoxManager;

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

    public class TaskConfig
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

        public TaskConfig(GameObject startShelf, TbOris startPose, float tbYRotationStart,GameObject targetShelf, TbOris targetPose, float tbYRotationTarget)
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

        public Dictionary<DOA, diffDOA> diffDOAs = new();

        public Stats(DOA[] doas)
        {
            foreach (DOA doa in doas)
            {
                diffDOAs.Add(doa, new diffDOA(doa, -1, -1));
            }
        }

        public override string ToString()
        {
            string output = string.Empty;

            output += $"Dist HdToStart: {Math.Round(hdToStart.magnitude, 2)} (x: {Math.Round(hdToStart.x, 2)}, y: {Math.Round(hdToStart.y, 2)}, z: {Math.Round(hdToStart.z, 2)}) [m]" +
                $"\r\nDist HdToTargt: {Math.Round(hdToTarget.magnitude, 2)} (x: {Math.Round(hdToTarget.x, 2)}, y: {Math.Round(hdToTarget.y, 2)}, z: {Math.Round(hdToTarget.z, 2)}) [m]\r\n\r\n";

            foreach (var doadiff in diffDOAs)
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
                this.diff = should-actual;
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

    public TaskConfig currentTaskConfig;
    public HIL_phases currentPhase;
    public Stats currentStats;

    public GameObject shdwTb_weld;
    public GameObject shdwTb_geom;

    //[SerializeField]
    private GameObject[] cupboards;
    [SerializeField]
    private GameObject GhstHand;
    private HandController GhstHandController;
    [SerializeField]
    private GameObject PrsthHand;
    private HandController PrsthHandController;
    [SerializeField]
    private GameObject TB;
    [SerializeField]
    private GameObject palmGeom;
    [SerializeField]
    private float GRASP_DISTANCE;
    [SerializeField]
    private float RELEASE_DISTANCE;
    [SerializeField]
    bool RemapStatsToIncomingRange;

    public PseudoFeedbackConfig[] pseudoFeedbackConfig;

    
    private void Awake()
    {
        // create stats obj
        currentStats = new Stats(new DOA[] { DOA.HOC, DOA.WFE, DOA.WPS });
        currentStats.RemapToIncomingRange = RemapStatsToIncomingRange;
    }
    void Start()
    {
        // get the first enabled handcontrollers
        GhstHandController = GhstHand.GetComponentsInChildren<HandController>(includeInactive: false)
            .Where(c => c.enabled)
            .ToArray()[0];
        PrsthHandController = PrsthHand.GetComponentsInChildren<HandController>(includeInactive: false)
            .Where(c => c.enabled)
            .ToArray()[0];

        // get all cupboards
        cupboards = GameObject.FindGameObjectsWithTag("cupboard");
        cupboards = cupboards.OrderBy(go => go.name).ToArray();

        currentPhase = HIL_phases.None;
    }


    // Update is called once per frame
    void Update()
    {
        // it is still null the first time
        if (currentTaskConfig != null)
        {
            // update current stats
            UpdateStats();

            // send out pseudo labels based on stats and activated phase config
            SendPseudoLabelToLibEMG();
        }

        // ----- START OF HIL PHASES ----- // ToDo?: Event-based instead of checking every cycle
        switch (currentPhase)
        {
            case HIL_phases.None:

                // Start new random task config
                this.NewRandomTaskConf();

                // move to next phase
                currentPhase = HIL_phases.Reach;

                // add other things that need to be set up for Reach...


                break;

            case HIL_phases.Reach:

                // check if distance to start is smaller then threshold
                if (currentStats.hdToStart.magnitude <= GRASP_DISTANCE)
                {
                    currentPhase = HIL_phases.Grasp;

                    // add other things that need to be set up for Grasp...
                    // - Send ghost hand to grasp
                    BtnSetGhostToTB();
                }
                break;

            case HIL_phases.Grasp:

                // Check if TB does not touch plate anymore => Switch to transport
                if (CollisionManager.FindCollisionByNames("Grasp_collider_box", currentTaskConfig.startShelf.name, contains: true).Count == 0)
                {
                    currentPhase = HIL_phases.Transport;

                    // add other things that need to be set up for Grasp...
                    // - Set ghost to target
                    BtnSetGhostToTarget();
                }

                // check if object was touched already 
                if (CollisionManager.FindCollisionBetweenTagAndObj("hand_collider", "ROI_collider_box").Count > 0)
                {
                    currentTaskConfig.tbTouched = true;
                }

                // TODO find out if new new target should be set when user fails to grasp it correctly
                // Check for fails (when Hand leaves Grasp_collider_box after initial contact e.g. the object falls over etc)
                if (currentTaskConfig.tbTouched && CollisionManager.FindCollisionBetweenTagAndObj("hand_collider", "Grasp_collider_box").Count == 0)
                {
                    print("phase: Object is not connected to hand anymore in Grasp phase");
                    // Go to None
                    currentPhase = HIL_phases.None;
                    break;
                }
                break;

            case HIL_phases.Transport:
                // as soon as the hand (with box) comes into close proximity of the target switch to Release
                if (currentStats.hdToTarget.magnitude <= RELEASE_DISTANCE)
                {
                    // switch to next: Release
                    currentPhase = HIL_phases.Release;
                    break;
                }

                // If hand is not connected (check roi or grasp collidere) => Fail => Phase.None
                if (CollisionManager.FindCollisionBetweenTagAndObj("hand_collider", "Grasp_collider_box").Count == 0)
                {
                    print("phase: FAIL - Object is not connected to hand anymore in transport phase");

                    // Switch to phase none
                    currentPhase = HIL_phases.None;
                    break;
                }

                break;

            case HIL_phases.Release:
                // If hand is not connected (check roi or grasp collidere) => Fail => Phase.None
                if (CollisionManager.FindCollisionBetweenTagAndObj("hand_collider", "Grasp_collider_box").Count == 0)
                {
                    print("phase: FAIL - Object is not connected to hand anymore in transport phase");

                    // Switch to phase none
                    currentPhase = HIL_phases.None;
                    break;
                }

                // ToDo see if we need to differantiate between success and failures => Maybe for rewards or user feedback

                break;

            default:
                
                break;
        }

    }


    private GameObject GetShelfOfCupboard(GameObject cupboard, Shelf shelf)
    {
       return cupboard.GetNamedChild("level_" + shelf.ToString());
    }

    private void UpdateStats()
    {
        // update stats
        currentStats.hdToStart = currentTaskConfig.startShelf.transform.position - palmGeom.transform.position;
        currentStats.hdToTarget = currentTaskConfig.targetShelf.transform.position - palmGeom.transform.position;

        var keys = currentStats.diffDOAs.Keys.ToArray();
        foreach (var key in keys)
        {
            float? should = GhstHandController.GetValueForDOA(key, currentStats.RemapToIncomingRange);

            float? actual = PrsthHandController.GetValueForDOA(key, currentStats.RemapToIncomingRange);

            if (should != null)
            {
                // get should and actual // ToDo? Change to update instead of always creating new object
                currentStats.diffDOAs[key] = new Stats.diffDOA(key, should.Value, actual.Value);
            }
        }
    }

    private void SendPseudoLabelToLibEMG()
    {
        // find current phase and send out feedback
        foreach (var phaseConfig in this.pseudoFeedbackConfig)
        {
            if (currentPhase == phaseConfig.phase && phaseConfig.feedback)
            {
                // ------------ ToDO to be made nicer --------------------
                // hardcoded pseudolabel structure for libEMG as one array
                double[] message = { currentStats.diffDOAs[DOA.HOC].actual, currentStats.diffDOAs[DOA.HOC].should, currentStats.diffDOAs[DOA.WFE].actual,
                currentStats.diffDOAs[DOA.WFE].should, currentStats.diffDOAs[DOA.WPS].actual, currentStats.diffDOAs[DOA.WPS].should };
                //Send Stats
                SimUdpSender.SendArrayAsUDPmessage(array: message, dataType: (6, 0), sendWithLastUdpTs: true);
                //print(string.Join(",", message));
            }
        }
    }

    private IEnumerator TransformGhostHdToTarget()
    {
        if (currentTaskConfig.targetPose == TaskConfig.TbOris.straight)
        {
            // as long as this does not make any problems, set pos and ori directly
            this.shdwTb_weld.transform.position = currentTaskConfig.targetShelf.transform.position + new Vector3(0, this.shdwTb_geom.GetComponentInChildren<MjGeom>().Box.Extents.y + currentTaskConfig.targetShelf.GetComponent<MjGeom>().Box.Extents.y, 0);

            // set TB orientation 
            this.shdwTb_weld.transform.rotation = currentTaskConfig.targetShelf.transform.rotation * Quaternion.Euler(0, currentTaskConfig.tbYRotationTarget, 0);

            // rotate WFE in the other direction to counterbalance
            GhstHandController.OverwriteCurrVal(DOA.WFE, -currentTaskConfig.tbYRotationTarget * Mathf.Deg2Rad);

            // rotate WPS back to 0
            GhstHandController.OverwriteCurrVal(DOA.WPS, 0);

            // open HOC to let go of box
            GhstHandController.OverwriteCurrVal(DOA.HOC, 0);

        }
        else if (currentTaskConfig.targetPose == TaskConfig.TbOris.sideways)
        {
            // rotate kinematic chain
            this.shdwTb_weld.transform.rotation = currentTaskConfig.targetShelf.transform.rotation * Quaternion.Euler(45, 0, 90);


            // change position and adjust for rotation
            this.shdwTb_weld.transform.position = currentTaskConfig.targetShelf.transform.position + new Vector3(0, this.shdwTb_geom.GetComponentInChildren<MjGeom>().Box.Extents.x + currentTaskConfig.targetShelf.GetComponent<MjGeom>().Box.Extents.y, 0);

            // rotate WPS into supination to counterbalance
            GhstHandController.OverwriteCurrVal(DOA.WPS, 1);

            // actuate WFE to get straight arm adjusted to cupboard level
            if (currentTaskConfig.targetShelf.name.Contains("bot")) GhstHandController.OverwriteCurrVal(DOA.WFE, 0);
            else if (currentTaskConfig.targetShelf.name.Contains("mid")) GhstHandController.OverwriteCurrVal(DOA.WFE, 0.5f);
            else if (currentTaskConfig.targetShelf.name.Contains("top")) GhstHandController.OverwriteCurrVal(DOA.WFE, 1);


        }

        yield return null;
    }

    private IEnumerator TransformGhostToStart()
    {
        if (currentTaskConfig.startPose == TaskConfig.TbOris.straight)
        {
            // as long as this does not make any problems, set pos and ori directly
            this.shdwTb_weld.transform.position = currentTaskConfig.startShelf.transform.position + new Vector3(0, this.shdwTb_geom.GetComponentInChildren<MjGeom>().Box.Extents.y + currentTaskConfig.targetShelf.GetComponent<MjGeom>().Box.Extents.y, 0);

            // set TB orientation 
            this.shdwTb_weld.transform.rotation = currentTaskConfig.startShelf.transform.rotation * Quaternion.Euler(0, currentTaskConfig.tbYRotationStart, 0);

            // rotate WFE in the other direction to counterbalance
            GhstHandController.OverwriteCurrVal(DOA.WFE, -currentTaskConfig.tbYRotationStart * Mathf.Deg2Rad);

            // rotate WPS back to 0
            GhstHandController.OverwriteCurrVal(DOA.WPS, 0);

        }
        else if (currentTaskConfig.startPose == TaskConfig.TbOris.sideways)
        {
            // rotate kinematic chain
            this.shdwTb_weld.transform.rotation = currentTaskConfig.startShelf.transform.rotation * Quaternion.Euler(45, 0, 90);


            // change position and adjust for rotation
            this.shdwTb_weld.transform.position = currentTaskConfig.startShelf.transform.position + new Vector3(0, this.shdwTb_geom.GetComponentInChildren<MjGeom>().Box.Extents.x + currentTaskConfig.targetShelf.GetComponent<MjGeom>().Box.Extents.y, 0);

            // rotate WPS into supination to counterbalance
            GhstHandController.OverwriteCurrVal(DOA.WPS, 90 * Mathf.Deg2Rad);

            //GhstHandController.OverwriteCurrVal(DOA.WFE, 57f * Mathf.Deg2Rad);
            // actuate WFE to get straight arm adjusted to cupboard level
            if (currentTaskConfig.targetShelf.name.Contains("bot")) GhstHandController.OverwriteCurrVal(DOA.WFE, 0);
            else if (currentTaskConfig.targetShelf.name.Contains("mid")) GhstHandController.OverwriteCurrVal(DOA.WFE, 0.5f);
            else if (currentTaskConfig.targetShelf.name.Contains("top")) GhstHandController.OverwriteCurrVal(DOA.WFE, 1);
        }

        // Close around TB
        GhstHandController.OverwriteCurrVal(DOA.HOC, 0.6f);


        yield return null;
    }

    private IEnumerator TransformTbToStart()
    {
        Vector3 newPos = new();
        Quaternion newRot = new();

        if (currentTaskConfig.startPose == TaskConfig.TbOris.straight)
        {
            // as long as this does not make any problems, set pos and ori directly // using shdwTb geom because it consists of a single box
            newPos = currentTaskConfig.startShelf.transform.position + new Vector3(0, this.shdwTb_geom.GetComponentInChildren<MjGeom>().Box.Extents.y + currentTaskConfig.startShelf.GetComponent<MjGeom>().Box.Extents.y+0.1f, 0);

            // set TB orientation 
            newRot = currentTaskConfig.startShelf.transform.rotation * Quaternion.Euler(0, currentTaskConfig.tbYRotationStart, 0);
        }
        else if (currentTaskConfig.startPose == TaskConfig.TbOris.sideways)
        {
            // rotate kinematic chain
            newRot = currentTaskConfig.startShelf.transform.rotation * Quaternion.Euler(45, 0, 90);

            // change position and adjust for rotation
            newPos = currentTaskConfig.startShelf.transform.position + new Vector3(0, this.shdwTb_geom.GetComponentInChildren<MjGeom>().Box.Extents.x + currentTaskConfig.startShelf.GetComponent<MjGeom>().Box.Extents.y+0.1f, 0);
        }
        MeshRenderer[] meshRenderer = TB.transform.GetComponentsInChildren<MeshRenderer>();
        for (int i = 0; i < meshRenderer.Length; i++) meshRenderer[i].enabled = false;

        MjState.TeleportMjRoot(TB.GetComponentInChildren<MjFreeJoint>(), newPos, false);
        yield return new WaitForSeconds(0.5f); 
        MjState.TeleportMjRoot(TB.GetComponentInChildren<MjFreeJoint>(), newPos, newRot);
        for (int i = 0; i < meshRenderer.Length; i++) meshRenderer[i].enabled = true;

        yield return null;
    }

    public (GameObject, TaskConfig.TbOris, float) CreateRandomConf(GameObject excludedShelf = null)
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
        while (GameObject.ReferenceEquals(shelf, excludedShelf));


        // create random pose and z rotation
        int orisLen = Enum.GetValues(typeof(TaskConfig.TbOris)).Length;
        TaskConfig.TbOris ori = (TaskConfig.TbOris)UnityEngine.Random.Range(0, orisLen);

        // ToDo: Change this to actual range of motion and fitting WFE
        int yRot = UnityEngine.Random.Range(-45, 45);

        return (shelf, ori, yRot);
    }

    public void NewRandomTaskConf()
    {
        (GameObject rdStartShelf, TaskConfig.TbOris rdTbOrisStart, float rdYRotStart) = CreateRandomConf();
        (GameObject rdTargetShelf, TaskConfig.TbOris rdTbOrisTarget, float rdYRotTarget) = CreateRandomConf(excludedShelf: rdStartShelf);

        // create a new config
        currentTaskConfig = new TaskConfig(rdStartShelf, rdTbOrisStart, rdYRotStart, rdTargetShelf, rdTbOrisTarget, rdYRotTarget);
        Debug.Log(JsonUtility.ToJson(currentTaskConfig));

        StartCoroutine(TransformGhostHdToTarget());
        StartCoroutine(TransformTbToStart());
    }

    public void BtnSetGhostToTB()
    {
        StartCoroutine(TransformGhostToStart());
    }

    public void BtnSetGhostToTarget()
    {
        StartCoroutine(TransformGhostHdToTarget());
    }
}
