using System;
using UnityEngine;

public class LogTransform : MonoBehaviour
{

    // header for this log
    private string header = "time_stamp_s" + ";" +
        "localPosition_xyz" + ";" +
        "localRotation_xyzw" + ";" +
        "globalPosition_xyz" + ";" +
        "globalRotation_xyzw" + ";" +
        Environment.NewLine;

    // Start is called before the first frame update
    void Start()
    {
        LoggingManager.CreateNewLog(fileName: this.gameObject.name, header: header);
    }

    // Update is called once per frame
    void Update()
    {
        string _buffer = StreamlinedInputManager.Now + ";" + 
            this.transform.localPosition.ToString("F6") + ";" + 
            this.transform.localRotation.ToString("F6") + ";" +
            this.transform.position.ToString("F6") + ";" +
            this.transform.rotation.ToString("F6") + ";" + Environment.NewLine;
        LoggingManager.AddToBuffer(this.gameObject.name, _buffer);
    }
}
