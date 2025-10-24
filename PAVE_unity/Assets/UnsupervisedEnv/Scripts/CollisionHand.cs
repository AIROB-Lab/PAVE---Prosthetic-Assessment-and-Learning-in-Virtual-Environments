using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mujoco;

public class CollisionHand : MonoBehaviour
{
    [SerializeField] private ParticleSystem confetti_PS;
    public TaskManager taskManager;
    //[SerializeField] private Material handPrintMaterial;
    private bool taskPerformed = false;
    private Material handPrintMaterial;
    private float timeOnSurface = 0f;
    private float requiredTime = 2.5f;
    private bool isHandOnSurface = false;
    public MjActuator wristFlex; 
    
    

    // Start is called before the first frame update
    void Start()
    {
        handPrintMaterial = GetComponent<Renderer>().material;
        handPrintMaterial.DisableKeyword("_EMISSION");
    }

    // Update is called once per frame
    void Update()
    {
        if (!isHandOnSurface) // reset if hand leaves surface
        {
            timeOnSurface = 0f;
        }
    }

    private void OnTriggerStay(Collider other)
    {
        if (!taskPerformed && other.gameObject.name != "XR Origin" && other.gameObject.tag != "noTrigger") //&& wristFlex.Control < -0.5
        {
            isHandOnSurface = true;
            timeOnSurface += Time.deltaTime;
            handPrintMaterial.EnableKeyword("_EMISSION");

            if (timeOnSurface >= requiredTime)
            {
                taskPerformed = true;
                if (confetti_PS != null)
                    confetti_PS.Play();
                handPrintMaterial.DisableKeyword("_EMISSION");
                timeOnSurface = 0f;
                StartCoroutine(WaitCoroutine());
            }
            
        }
    }


         
    private void OnTriggerExit(Collider other)
    {
        if (!taskPerformed && other.gameObject.name != "XR Origin" && other.gameObject.tag != "noTrigger") // reset if hand leaves surface
        {
            isHandOnSurface = false;
            timeOnSurface = 0f;
            handPrintMaterial.DisableKeyword("_EMISSION");
        }
    }

    IEnumerator WaitCoroutine()
    {
        yield return new WaitForSeconds(1.0f);
    

        TaskManager.taskState = TaskState.Successful;
        taskManager.EndCurrentTask();

        yield return new WaitForSeconds(0.5f); // buffer delay before
        taskPerformed = false;


    }

}
