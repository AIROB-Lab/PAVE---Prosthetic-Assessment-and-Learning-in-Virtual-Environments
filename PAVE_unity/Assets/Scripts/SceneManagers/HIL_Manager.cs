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
using Unity.XR.Oculus;
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
    [SerializeField]

    public PseudoFeedbackConfig[] pseudoFeedbackConfig;


    private void Awake()
    {
        Application.targetFrameRate = 90;


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


        if (currentEnv == HIL_env.Interact || currentEnv == HIL_env.LimbPos)
        {
            // get all cupboards
            cupboards = GameObject.FindGameObjectsWithTag("cupboard");
            cupboards = cupboards.OrderBy(go => go.name).ToArray();
        }
        currentPhase = HIL_phases.None;
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
            SendPseudoLabelToLibEMG();
        }

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
                if (CollisionManager.FindCollisionByNames("Grasp_collider_box", currentInteractTaskConfig.startShelf.name, contains: true).Count == 0)
                {
                    currentPhase = HIL_phases.Transport;

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

    private void PerformLimbPosLoop()
    {
        throw new NotImplementedException();
    }
    public double timoutTime;
    double timoutStart;
    private void PerformTacLoop()
    {
        // ----- START OF HIL PHASES ----- Only None and Reach
        switch (currentPhase)
        {
            case HIL_phases.None:

                // Start new random task config
                this.NewRandomTacTaskConf();

                // move to next phase
                currentPhase = HIL_phases.Reach;

                // add other things that need to be set up for Reach...
                timoutStart = StreamlinedInputManager.Now;

                break;

            case HIL_phases.Reach:

                // if timout or target reach generate new one => just timout for now
                if (StreamlinedInputManager.Now - timoutStart > timoutTime) currentPhase = HIL_phases.None;

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
            currentStats.hdToStart = currentInteractTaskConfig.startShelf.transform.position - palmGeom.transform.position;
            currentStats.hdToTarget = currentInteractTaskConfig.targetShelf.transform.position - palmGeom.transform.position;
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

    private void SendPseudoLabelToLibEMG()
    {
        // find current phase and send out feedback
        foreach (var phaseConfig in this.pseudoFeedbackConfig)
        {
            if (currentPhase == phaseConfig.phase && phaseConfig.feedback)
            {
                // ------------ ToDO to be made nicer --------------------
                // hardcoded pseudolabel structure for libEMG as one array
                double[] message = { currentStats.activeDiffDOAs[DOA.HOC].actual, currentStats.activeDiffDOAs[DOA.HOC].should, currentStats.activeDiffDOAs[DOA.WFE].actual,
                currentStats.activeDiffDOAs[DOA.WFE].should, currentStats.activeDiffDOAs[DOA.WPS].actual, currentStats.activeDiffDOAs[DOA.WPS].should };
                //Send Stats
                SimUdpSender.SendArrayAsUDPmessage(array: message, dataType: (6, 0), sendWithLastUdpTs: true);
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

            // open HOC to let go of box
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
        yield return new WaitForSeconds(0.5f); 
        MjState.TeleportMjRoot(TB.GetComponentInChildren<MjFreeJoint>(), newPos, newRot);
        for (int i = 0; i < meshRenderer.Length; i++) meshRenderer[i].enabled = true;

        yield return null;
    }

    public (GameObject, InteractTaskConfig.TbOris, float) CreateRandomInteractConf(GameObject excludedShelf = null)
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
        int orisLen = Enum.GetValues(typeof(InteractTaskConfig.TbOris)).Length;
        InteractTaskConfig.TbOris ori = (InteractTaskConfig.TbOris)UnityEngine.Random.Range(0, orisLen);

        // ToDo: Change this to actual range of motion and fitting WFE
        int yRot = UnityEngine.Random.Range(-45, 45);

        return (shelf, ori, yRot);
    }

    public void NewRandomInteractTaskConf()
    {
        (GameObject rdStartShelf, InteractTaskConfig.TbOris rdTbOrisStart, float rdYRotStart) = CreateRandomInteractConf();
        (GameObject rdTargetShelf, InteractTaskConfig.TbOris rdTbOrisTarget, float rdYRotTarget) = CreateRandomInteractConf(excludedShelf: rdStartShelf);

        // create a new config
        currentInteractTaskConfig = new InteractTaskConfig(rdStartShelf, rdTbOrisStart, rdYRotStart, rdTargetShelf, rdTbOrisTarget, rdYRotTarget);
        Debug.Log(JsonUtility.ToJson(currentInteractTaskConfig));

        StartCoroutine(TransformGhostHdToTarget());
        StartCoroutine(TransformTbToStart());
    }

    public void NewRandomTacTaskConf()
    {
        // choose 1,2 or 3 from this:
        int countDoas = UnityEngine.Random.Range(1, GhstHandController.DOA_mujoco.Length + 1);

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

    public void BtnSetGhostToTB()
    {
        StartCoroutine(TransformGhostToStart());
    }

    public void BtnSetGhostToTarget()
    {
        StartCoroutine(TransformGhostHdToTarget());
    }
}
