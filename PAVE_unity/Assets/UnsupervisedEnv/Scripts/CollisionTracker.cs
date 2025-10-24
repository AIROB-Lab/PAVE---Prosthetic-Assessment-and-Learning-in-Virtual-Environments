using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class CollisionTracker : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {

    }

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
