using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;


public class FillingBar : MonoBehaviour
{

    public Slider slider;

    public void SetMaxFilling(float maxFilling)
    {
        slider.maxValue = maxFilling;
        slider.value = maxFilling;
    }
    public void SetFilling(float filling)
    {
        slider.value = filling;

    }
    
}
