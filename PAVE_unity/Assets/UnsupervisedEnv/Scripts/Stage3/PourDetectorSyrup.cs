using System.Collections;
using System.IO;
using UnityEngine;

public class PourDetectorSyrup : MonoBehaviour


// based on https://www.youtube.com/watch?v=hyiyjUEReYg
{
    public int pourTreshold = 45;
    public Transform origin = null;
    public GameObject streamPrefab = null;

    public bool isPouring = false;
    private StreamSyrup currentStream = null;


    private void FixedUpdate()
    {
        // pouring effect just in level 10 active
        if (LevelManager.level == Levels.SqueezeSyrup)
        {
            //bool pourCheck = CalculatePourAngle() < pourTreshold; // < true; > false

            bool pourCheck = LevelManager.currentTotalForceSyrup >= LevelManager.maxSqueezeForce; // start pouring at squeezing force

            if (isPouring != pourCheck)
            {
                isPouring = pourCheck;
                if (isPouring)
                {
                    StartPour();
                }
                else
                {
                    EndPour();
                }
            }
        }

    }

    private void StartPour()
    {

        currentStream = CreateStream();
        currentStream.Begin();
    }

    private void EndPour()
    {
        // Empty
        currentStream.End();
        currentStream = null;
    }

    private float CalculatePourAngle()
    {
        return transform.up.y * Mathf.Rad2Deg; // angle in degrees from object (up green axis, forward blue axis, right red axis)
    }

    private StreamSyrup CreateStream()
    {
        GameObject streamObject = Instantiate(streamPrefab, origin.position, Quaternion.identity, transform);
        return streamObject.GetComponent<StreamSyrup>();
    }
}