using TMPro;
using UnityEngine;

public class ROITextUpdate : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        this.GetComponent<TextMeshProUGUI>().text = $"ROI: Hand - {StreamlinedInputManager.lookingAtHand}, Object - {StreamlinedInputManager.lookingAtObject}, Target - {StreamlinedInputManager.lookingAtTarget}";
    }
}
