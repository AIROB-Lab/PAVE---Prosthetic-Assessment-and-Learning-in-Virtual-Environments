using Mujoco;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using Unity.XR.CoreUtils;
using UnityEngine;
using static PastaBoxManager;

public class HIL_Manager : MonoBehaviour
{
    public enum HIL_phases
    {
        None,
        PickUp,
        Transport,
        Dropoff
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

        public TaskConfig(GameObject startShelf, TbOris startPose, float tbYRotationStart,GameObject targetShelf, TbOris targetPose, float tbYRotationTarget)
        {
            this.startShelf = startShelf;
            this.startPose = startPose;
            this.tbYRotationStart = tbYRotationStart;
            
            this.targetShelf = targetShelf;
            this.targetPose = targetPose;
            this.tbYRotationTarget = tbYRotationTarget;
        }
    }

    public class Stats
    {
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

    public enum Shelf
    {
        bot, 
        mid,
        top
    }

    public TaskConfig currentTaskConfig;
    public HIL_phases currentPhase = HIL_phases.None;
    public Stats currentStats;

    public GameObject shdwTb_weld;
    public GameObject shdwTb_geom;

    [SerializeField]
    private GameObject[] cupboards;
    [SerializeField]
    private HandController ShdwHandController;
    [SerializeField]
    private HandController PrsthHandController;
    [SerializeField]
    private GameObject TB;
    [SerializeField]
    private GameObject palmGeom;


    void Start()
    {
        // get all cupboards
        cupboards = GameObject.FindGameObjectsWithTag("cupboard");
        cupboards = cupboards.OrderBy(go => go.name).ToArray();

        // create new random task conf
        this.NewRandomTaskConf();

        // create stats obj
        currentStats = new Stats( new DOA[] { DOA.HOC, DOA.WFE, DOA.WPS });
    }

    // Update is called once per frame
    void Update()
    {
        // update stats
        currentStats.hdToStart = currentTaskConfig.startShelf.transform.position - palmGeom.transform.position;
        currentStats.hdToTarget = currentTaskConfig.targetShelf.transform.position - palmGeom.transform.position;

        foreach (var keyvalue in currentStats.diffDOAs)
        {
            float? should = ShdwHandController.GetValueForDOA(keyvalue.Key);
            float? actual = PrsthHandController.GetValueForDOA(keyvalue.Key);

            if (should != null)
            {
                // get should and actual // ToDo? Change to update instead of always creating new object
                currentStats.diffDOAs[keyvalue.Value.doa] = new Stats.diffDOA(keyvalue.Value.doa, should.Value, actual.Value);
            }

            // 20250723 continue here
        }
    }


    private GameObject GetShelfOfCupboard(GameObject cupboard, Shelf shelf)
    {
       return cupboard.GetNamedChild("level_" + shelf.ToString());
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

    public void SetGhostToTB()
    {
        StartCoroutine(TransformGhostToStart());
    }

    public void SetGhostToTarget()
    {
        StartCoroutine(TransformGhostHdToTarget());
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
            this.ShdwHandController.OverwriteCurrVal(DOA.WFE, -currentTaskConfig.tbYRotationTarget * Mathf.Deg2Rad);

            // rotate WPS back to 0
            this.ShdwHandController.OverwriteCurrVal(DOA.WPS, 0);

        }
        else if (currentTaskConfig.targetPose == TaskConfig.TbOris.sideways)
        {
            // rotate kinematic chain
            this.shdwTb_weld.transform.rotation = currentTaskConfig.targetShelf.transform.rotation * Quaternion.Euler(45, 0, 90);


            // change position and adjust for rotation
            this.shdwTb_weld.transform.position = currentTaskConfig.targetShelf.transform.position + new Vector3(0, this.shdwTb_geom.GetComponentInChildren<MjGeom>().Box.Extents.x + currentTaskConfig.targetShelf.GetComponent<MjGeom>().Box.Extents.y, 0);

            // rotate WPS into supination to counterbalance
            this.ShdwHandController.OverwriteCurrVal(DOA.WPS, 90 * Mathf.Deg2Rad);

            // actuate WFE to get straight arm // ? to be adjusted to cupboard level
            this.ShdwHandController.OverwriteCurrVal(DOA.WFE, 57f *  Mathf.Deg2Rad);

        }

        yield return null;
    }

    private IEnumerator TransformGhostToStart()
    {
        //    Vector3 newPos = new();
        //    Quaternion newRot = new();

        //this.shdwTb_weld.transform.position = this.TB.transform.position;
        //this.shdwTb_weld.transform.rotation = this.TB.transform.rotation;

        //if (newTaskConfig.targetPose == TaskConfig.TbOris.straight)
        //{

        //}
        if (currentTaskConfig.startPose == TaskConfig.TbOris.straight)
        {
            // as long as this does not make any problems, set pos and ori directly
            this.shdwTb_weld.transform.position = currentTaskConfig.startShelf.transform.position + new Vector3(0, this.shdwTb_geom.GetComponentInChildren<MjGeom>().Box.Extents.y + currentTaskConfig.targetShelf.GetComponent<MjGeom>().Box.Extents.y, 0);

            // set TB orientation 
            this.shdwTb_weld.transform.rotation = currentTaskConfig.startShelf.transform.rotation * Quaternion.Euler(0, currentTaskConfig.tbYRotationStart, 0);

            // rotate WFE in the other direction to counterbalance
            this.ShdwHandController.OverwriteCurrVal(DOA.WFE, -currentTaskConfig.tbYRotationStart * Mathf.Deg2Rad);

            // rotate WPS back to 0
            this.ShdwHandController.OverwriteCurrVal(DOA.WPS, 0);

        }
        else if (currentTaskConfig.startPose == TaskConfig.TbOris.sideways)
        {
            // rotate kinematic chain
            this.shdwTb_weld.transform.rotation = currentTaskConfig.startShelf.transform.rotation * Quaternion.Euler(45, 0, 90);


            // change position and adjust for rotation
            this.shdwTb_weld.transform.position = currentTaskConfig.startShelf.transform.position + new Vector3(0, this.shdwTb_geom.GetComponentInChildren<MjGeom>().Box.Extents.x + currentTaskConfig.targetShelf.GetComponent<MjGeom>().Box.Extents.y, 0);

            // rotate WPS into supination to counterbalance
            this.ShdwHandController.OverwriteCurrVal(DOA.WPS, 90 * Mathf.Deg2Rad);

            // actuate WFE to get straight arm // ? to be adjusted to cupboard level
            this.ShdwHandController.OverwriteCurrVal(DOA.WFE, 57f * Mathf.Deg2Rad);
        }


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

        MjState.TeleportMjRoot(TB.GetComponentInChildren<MjFreeJoint>(), newPos, newRot);
        yield return null;
    }
}
