using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;

public class ForceViz : MonoBehaviour
{
    public GameObject fillScale;
    public Vector2 mappingInMinMax;
    public PubSensorData sensorData;
    public FillingBar fillingBar;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        float sumOfSensorData = sensorData.GetSumOfAllSensorData(componentsX:false, componentsY:false, componentsZ:true).Remap(mappingInMinMax.x, mappingInMinMax.y, 0, 1);
        fillingBar.SetFilling(sumOfSensorData);
    }
}
