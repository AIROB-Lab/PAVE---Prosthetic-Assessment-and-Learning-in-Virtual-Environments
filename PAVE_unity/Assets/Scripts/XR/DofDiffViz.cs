using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UI;

public class DofDiffViz : MonoBehaviour
{
    public GameObject fillScale;
    public DOA doa;
    public Slider fillingBarActual;
    public Slider fillingBarShould;
    public HIL_Manager hil_manager;

    public bool VizRemapRange;
    public Vector2 VizRemapRangeIn;
    public Vector2 VizRemapRangeOut;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        // get current stats for this doa
        float actual =  hil_manager.currentStats.activeDiffDOAs[doa].actual;
        float should =  hil_manager.currentStats.activeDiffDOAs[doa].should;

        if (VizRemapRange)
        {
            actual = actual.Remap(VizRemapRangeIn.x, VizRemapRangeIn.y, VizRemapRangeOut.x, VizRemapRangeOut.y);
            should = should.Remap(VizRemapRangeIn.x, VizRemapRangeIn.y, VizRemapRangeOut.x, VizRemapRangeOut.y);
        }
        

        // adjust bars
        fillingBarActual.value = actual;
        fillingBarShould.value = should;


    }
}
