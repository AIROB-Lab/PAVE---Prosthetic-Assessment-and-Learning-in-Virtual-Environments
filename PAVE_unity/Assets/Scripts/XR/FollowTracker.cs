using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;

/// <summary>
/// This is a class specifically for objects including mujoco weld objects
/// </summary>
/// <summary>Shared calibrated tracking-to-mocap follower. Offsets are meters and degrees.</summary>
public class FollowTracker : MonoBehaviour
{
    public GameObject leadingObject;
    public GameObject weldGeometry;
    public GameObject counterMujoco;
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
    protected virtual void Start()
    {
        // let some time pass for initialization
        Invoke("StartTracking", 3);
    }

    protected virtual void FixedUpdate()
    {
        // only start tracking after a set amount of time
        if (!trackingStarted || leadingObject == null)
        {
            HandFollowValid = false;
            return;
        }

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
        if (faceCamera && Camera.main != null)
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

    /// <summary>Legacy opt-in realignment, retained for serialized button callbacks.</summary>
    public void RealignMj()
    {
        if (weldGeometry == null || counterMujoco == null)
        {
            return;
        }

        Vector3 error = transform.position - weldGeometry.transform.position;
        if (error.magnitude >= 0.2f)
        {
            counterMujoco.transform.position += error;
        }
    }

    private void StartTracking()
    {
        trackingStarted = true;
    }
}
