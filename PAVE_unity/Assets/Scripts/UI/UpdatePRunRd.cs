using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class UpdatePRunRd : MonoBehaviour
{
    public GameObject pastaBoxManager;
    // Update is called once per frame
    void Update()
    {
        this.GetComponent<TextMeshProUGUI>().text = $"P: {pastaBoxManager.GetComponent<PastaBoxManager>().pastaBoxState.participant_id}, Cell: {pastaBoxManager.GetComponent<PastaBoxManager>().pastaBoxState.cell}, Run: {pastaBoxManager.GetComponent<PastaBoxManager>().pastaBoxState.run}";
    }
}
