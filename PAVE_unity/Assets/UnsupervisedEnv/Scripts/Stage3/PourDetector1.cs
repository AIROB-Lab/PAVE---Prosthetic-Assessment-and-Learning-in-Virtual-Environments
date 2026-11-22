using System.Collections;
using System.IO;
using UnityEngine;

public class PourDetector1 : MonoBehaviour


// based on https://www.youtube.com/watch?v=hyiyjUEReYg
{
    public int pourTreshold = 45;
    public Transform origin = null;
    public GameObject streamPrefab = null;

    public static bool isPouring1 = false;
    private Stream1 currentStream = null;


    private void Update()
    {
        // pouring effect just in level 2 active
        if(LevelManager.level == Levels.PourIngredients && LevelManager.placed)
        {
            bool pourCheck = CalculatePourAngle() < pourTreshold; // < true; > false

            if (isPouring1 != pourCheck) // to not check every frame
            {
                isPouring1 = pourCheck;
                if (isPouring1)
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

    private Stream1 CreateStream()
    {
        GameObject streamObject = Instantiate(streamPrefab, origin.position, Quaternion.identity, transform);
        return streamObject.GetComponent<Stream1>();
    }
}