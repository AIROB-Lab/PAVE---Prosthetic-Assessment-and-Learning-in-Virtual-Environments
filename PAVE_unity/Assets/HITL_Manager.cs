using Mujoco;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.XR.CoreUtils;
using UnityEngine;
using static PastaBoxManager;

public class HITL_Manager : MonoBehaviour
{
    public class ShdwHdConfig
    {
        public enum TbOris
        {
            straight,
            sideways
        }

        public GameObject targetShelf;
        public TbOris currentPose;
        public float tbZRotation;

        public ShdwHdConfig(GameObject targetShelf, TbOris currentPose, float tbZRotation)
        {
            this.targetShelf = targetShelf;
            this.currentPose = currentPose;
            this.tbZRotation = tbZRotation;
        }
    }
    public enum Shelf
    {
        bot, 
        mid,
        top
    }

    public GameObject shdwTb;
    public ShdwHdConfig shdwHdConfig;


    [SerializeField]
    private GameObject[] cupboards;

    // Start is called before the first frame update
    void Start()
    {
        cupboards = GameObject.FindGameObjectsWithTag("cupboard");
        cupboards = cupboards.OrderBy(go => go.name).ToArray();

        Invoke("NewRandomShdwLoc", 3);

    }

    // Update is called once per frame
    void Update()
    {
                
    }


    private GameObject GetShelfOfCupboard(GameObject cupboard, Shelf shelf)
    {
       return cupboard.GetNamedChild("level_" + shelf.ToString());
    }

    public void NewRandomShdwLoc()
    {
        // get cupboard
        GameObject cupboard = cupboards[UnityEngine.Random.Range(0, cupboards.Length)];
        // get level
        int shelfsLen = Enum.GetValues(typeof(Shelf)).Length;
        Shelf rdShelf = (Shelf)UnityEngine.Random.Range(0, shelfsLen);  // 0 to 3
        GameObject shelf = GetShelfOfCupboard(cupboard, rdShelf);

        // create random pose and z rotation
        int orisLen = Enum.GetValues(typeof(ShdwHdConfig.TbOris)).Length;
        ShdwHdConfig.TbOris ori = (ShdwHdConfig.TbOris) UnityEngine.Random.Range(0, orisLen);

        // ToDo: Change this to actual range of motion and fitting WFE
        int zRot = UnityEngine.Random.Range(90, 0);

        // create a new config
        shdwHdConfig = new ShdwHdConfig(shelf, ori, zRot);
        Debug.Log(JsonUtility.ToJson(shdwHdConfig));

        StartCoroutine(TransformShdwHd());
    }

    private IEnumerator TransformShdwHd()
    {
        this.shdwTb.transform.position = shdwHdConfig.targetShelf.transform.position + new Vector3(0, this.shdwTb.GetComponentInChildren<MjGeom>().Box.Extents.y,0);

        //Vector3 currentPos = this.shdwTb.transform.position;
        //Quaternion currentRot = this.shdwTb.transform.rotation;
        //// same lerping as in tracker
        //if ((currentPos - shdwHdConfig.targetShelf.transform.position).magnitude < 0.20 && Quaternion.Angle(this.transform.rotation, targetRot) < followAng)
        //{
        //    this.transform.position = targetPos;
        //    this.transform.rotation = targetRot;

        //    // to unset the transparency in the SceneManager
        //    HandFollowValid = true;
        //}
        //else
        //{
        //    this.transform.position += (targetPos - this.transform.position) * lerpPos * Time.fixedDeltaTime;
        //    this.transform.rotation = Quaternion.Lerp(this.transform.rotation, targetRot, lerpQuat);

        //    // to set the transparency in the SceneManager
        //    HandFollowValid = false;
        //}

        yield return null;
    }

}
