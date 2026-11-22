using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Mujoco;

//public class ByteEvent : UnityEvent<byte[]> { }
//public class ObjectEvent : UnityEvent<DOA_mj[]> { }

public class EventManager : Singleton<EventManager>
{
    // Define Event
    //public ByteEvent newDataArrived = new ByteEvent(); // invoke in SIM
    //public ObjectEvent receivingData = new ObjectEvent(); // invoke in HandController
    public UnityEvent buttonEvent = new UnityEvent();


    // Start is called before the first frame update
    void Start()
    {
    }

    // Update is called once per frame
    void Update()
    {
    }
}