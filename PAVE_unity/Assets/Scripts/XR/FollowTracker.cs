using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;

/// <summary>
/// This is a class specifically for objects including mujoco weld objects
/// </summary>
public class FollowTracker : MonoBehaviour
{
    public GameObject leadingObject;

    public float lerpPos;
    public float lerpQuat;
    public float followDist;
    public float followAng;
    public Vector3 offsetPos_loc;
    public Vector3 offsetEuler;

    public bool offsetInGlobal;
    public bool faceCamera;


    public bool HandFollowValid = false;

    private bool trackingStarted = false;

    // Start is called before the first frame update
    void Start()
    {
        // let some time pass for initialization
        Invoke("StartTracking", 3);
    }

    private void Update()
    {
        // only start tracking after a set amount of time
        if (!trackingStarted) return;

        Vector3 targetPos = new();
        Quaternion targetRot = new();

        // adjust position
        if (!offsetInGlobal)
        {
            targetPos = leadingObject.transform.position + leadingObject.transform.rotation * offsetPos_loc;
        }
        else
        {
            targetPos = leadingObject.transform.position + offsetPos_loc;
        }

        // adjust rotation
        targetRot = leadingObject.transform.rotation * Quaternion.Euler(offsetEuler);

        if (faceCamera)
        {
            Vector3 direction = Camera.main.transform.position - leadingObject.transform.position;
            direction.y = 0; // Optional: lock rotation to Y axis only
            targetRot = Quaternion.LookRotation(direction);

        }

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

    }

    private void StartTracking()
    {
        trackingStarted = true;
    }
}
