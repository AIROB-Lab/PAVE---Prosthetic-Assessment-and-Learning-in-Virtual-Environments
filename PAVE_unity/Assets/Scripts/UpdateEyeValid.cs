using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class UpdateEyeValid : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        this.GetComponent<TextMeshProUGUI>().text = $"Eye Valid: {StreamlinedInputManager.eyeValid} {StreamlinedInputManager.eyeValid2}";
    }
}
