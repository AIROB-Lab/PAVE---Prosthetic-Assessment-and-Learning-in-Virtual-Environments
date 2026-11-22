using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;

/// <summary>
/// This is a class specifically for objects including mujoco weld objects
/// </summary>
public class FollowTrackerBowl : MonoBehaviour
{
    public GameObject leadingObject;

    public GameObject weldGeometry;
    public GameObject counterMujoco;

    public TaskManager taskManager;

    public float lerpPos;
    public float lerpQuat;
    public float followDist;
    public float followAng;
    public Vector3 offsetPos_loc;
    public Vector3 offsetEuler;

    public bool HandFollowValid = false;

    
    // Start is called before the first frame update
    void Start()
    {
       
    }

    // Update is called once per frame
    void Update()
    {

    }
    private void FixedUpdate()
    {
        // only start tracking when certain task started
        if (!TaskManager.wpsTask)
        {
            this.transform.position = new Vector3(0, 0, 0);
            return;
        }

        Vector3 targetPos = leadingObject.transform.position + leadingObject.transform.rotation * offsetPos_loc;
        Quaternion targetRot = leadingObject.transform.rotation * Quaternion.Euler(offsetEuler);

        if ((this.transform.position - targetPos).magnitude < followDist && Quaternion.Angle(this.transform.rotation, targetRot) < followAng)
        {
            this.transform.position = targetPos;
            this.transform.rotation = targetRot;

            // to unset the transparency in the SceneManager
            HandFollowValid = true;
        }
        else
        {
            this.transform.position += (targetPos - this.transform.position) * lerpPos * Time.fixedDeltaTime;
            this.transform.rotation = Quaternion.Lerp(this.transform.rotation, targetRot, lerpQuat);

            // to set the transparency in the SceneManager
            HandFollowValid = false;
        }

        

        //counter the mujoco behaviour
        //if ((counterMujoco.transform.localPosition + weldGeometry.transform.localPosition).magnitude > followDist)
        //{
        //    counterMujoco.transform.localPosition -= (weldGeometry.transform.localPosition + counterMujoco.transform.localPosition).normalized * lerpPos * Time.fixedDeltaTime;
        //}
    }

    // not needed for now
    public void RealignMj()
    {
        // check world pos diff of should and is of geometry
        Vector3 shouldPos = this.transform.position;
        Vector3 isPosition = weldGeometry.transform.position; // is position of the geometry

        if ((shouldPos - isPosition).magnitude >= 0.2f)
        {
            // what is the current adjustment already?
            Vector3 currentAdjustment = counterMujoco.transform.position - shouldPos;

            // is there further adjustment needed?
            Vector3 furtherAdjustment = shouldPos - isPosition;

            // where does the counter need to be then
            counterMujoco.transform.position = shouldPos + currentAdjustment + furtherAdjustment;

            //counterMujoco.transform.localPosition -= weldGeometry.transform.localPosition;
            // add rotation
        }
    }

    
}
