using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class CollisionTracker : MonoBehaviour
{
    // Update is called once per frame
    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.name != "XR Origin" && other.tag != this.gameObject.tag)
        {
            CollisionManager.HandleTriggerEnter(Time.time, this.gameObject, other.gameObject);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        CollisionManager.HandleTriggerExit(Time.time, this.gameObject, other.gameObject);
    }
}
