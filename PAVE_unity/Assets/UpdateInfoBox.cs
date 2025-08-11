using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class UpdateInfoBox : MonoBehaviour
{
    // Start is called before the first frame update
    public HIL_Manager hilManager;
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        string stats = hilManager.currentStats.ToString();
        this.GetComponent<TextMeshProUGUI>().text = stats;
    }
}
