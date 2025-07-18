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
    public enum Shelf
    {
        bot, 
        mid,
        top
    }

    public GameObject shdwTb_weld;
    public TaskConfig newTaskConfig;
    public GameObject shdwTb_geom;


    [SerializeField]
    private GameObject[] cupboards;
    [SerializeField]
    private GameObject ShdwHandController;
    [SerializeField]
    private GameObject TB;



    void Start()
    {
        cupboards = GameObject.FindGameObjectsWithTag("cupboard");
        cupboards = cupboards.OrderBy(go => go.name).ToArray();

        //Invoke("NewRandomShdwLoc", 3);

    }

    // Update is called once per frame
    void Update()
    {
                
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
        newTaskConfig = new TaskConfig(rdStartShelf, rdTbOrisStart, rdYRotStart, rdTargetShelf, rdTbOrisTarget, rdYRotTarget);
        Debug.Log(JsonUtility.ToJson(newTaskConfig));

        StartCoroutine(TransformGhostHdToTarget());
        StartCoroutine(TransformTbToStart());
    }

    private IEnumerator TransformGhostHdToTarget()
    {
        if (newTaskConfig.targetPose == TaskConfig.TbOris.straight)
        {
            // as long as this does not make any problems, set pos and ori directly
            this.shdwTb_weld.transform.position = newTaskConfig.targetShelf.transform.position + new Vector3(0, this.shdwTb_geom.GetComponentInChildren<MjGeom>().Box.Extents.y + newTaskConfig.targetShelf.GetComponent<MjGeom>().Box.Extents.y, 0);

            // set TB orientation 
            this.shdwTb_weld.transform.rotation = newTaskConfig.targetShelf.transform.rotation * Quaternion.Euler(0, newTaskConfig.tbYRotationTarget, 0);

            // rotate WFE in the other direction to counterbalance
            this.ShdwHandController.GetComponent<HandController>().OverwriteCurrVal(DOA.WFE, -newTaskConfig.tbYRotationTarget * Mathf.Deg2Rad);

            // rotate WPS back to 0
            this.ShdwHandController.GetComponent<HandController>().OverwriteCurrVal(DOA.WPS, 0);

        }
        else if (newTaskConfig.targetPose == TaskConfig.TbOris.sideways)
        {
            // rotate kinematic chain
            this.shdwTb_weld.transform.rotation = newTaskConfig.targetShelf.transform.rotation * Quaternion.Euler(57, 0, 90);


            // change position and adjust for rotation
            this.shdwTb_weld.transform.position = newTaskConfig.targetShelf.transform.position + new Vector3(0, this.shdwTb_geom.GetComponentInChildren<MjGeom>().Box.Extents.x + newTaskConfig.targetShelf.GetComponent<MjGeom>().Box.Extents.y, 0);

            // rotate WPS into supination to counterbalance
            this.ShdwHandController.GetComponent<HandController>().OverwriteCurrVal(DOA.WPS, 90 * Mathf.Deg2Rad);

            // actuate WFE to get straight arm // ? to be adjusted to cupboard level
            this.ShdwHandController.GetComponent<HandController>().OverwriteCurrVal(DOA.WFE, 57f *  Mathf.Deg2Rad);

        }

        yield return null;
    }

    private IEnumerator TransformTbToStart()
    {
        Vector3 newPos = new();
        Quaternion newRot = new();

        if (newTaskConfig.startPose == TaskConfig.TbOris.straight)
        {
            // as long as this does not make any problems, set pos and ori directly // using shdwTb geom because it consists of a single box
            newPos = newTaskConfig.startShelf.transform.position + new Vector3(0, this.shdwTb_geom.GetComponentInChildren<MjGeom>().Box.Extents.y + newTaskConfig.startShelf.GetComponent<MjGeom>().Box.Extents.y+0.1f, 0);

            // set TB orientation 
            newRot = newTaskConfig.startShelf.transform.rotation * Quaternion.Euler(0, newTaskConfig.tbYRotationStart, 0);
        }
        else if (newTaskConfig.startPose == TaskConfig.TbOris.sideways)
        {
            // rotate kinematic chain
            newRot = newTaskConfig.startShelf.transform.rotation * Quaternion.Euler(45, 0, 90);

            // change position and adjust for rotation
            newPos = newTaskConfig.startShelf.transform.position + new Vector3(0, this.shdwTb_geom.GetComponentInChildren<MjGeom>().Box.Extents.x + newTaskConfig.startShelf.GetComponent<MjGeom>().Box.Extents.y+0.1f, 0);
        }

        MjState.TeleportMjRoot(TB.GetComponentInChildren<MjFreeJoint>(), newPos, newRot);
        yield return null;
    }
}
