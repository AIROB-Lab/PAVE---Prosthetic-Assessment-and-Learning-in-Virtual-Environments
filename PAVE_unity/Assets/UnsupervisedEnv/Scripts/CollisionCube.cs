using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CollisionCube : MonoBehaviour
{
    public CatchAndCollect catchAndCollect;
    public int indexCube;
    public GameObject ground;
    // Update is called once per frame
    private void OnTriggerEnter(Collider other)
    {
        if (other.name == "ground_geom")
        {
            catchAndCollect.stopTeleport[indexCube] = true;
            catchAndCollect.cubeReachedGround[indexCube] = true;
            ground.GetComponent<Renderer>().material.DisableKeyword("_EMISSION");
        }
        else if (other.tag == "hand_collider")
        {
            catchAndCollect.stopTeleport[indexCube] = true;
            ground.GetComponent<Renderer>().material.EnableKeyword("_EMISSION");
        }
    }
}
