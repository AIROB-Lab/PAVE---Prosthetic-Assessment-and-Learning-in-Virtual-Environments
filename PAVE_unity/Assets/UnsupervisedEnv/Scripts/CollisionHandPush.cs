using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mujoco;


public class CollisionHandPush : MonoBehaviour
{
    [SerializeField] private ParticleSystem confetti_PS;
    public TaskManager taskManager;
    private bool taskPerformed = false;
    private bool isHandStay = false;
    private Material handPrintMaterial;
    public MjHingeJoint pushJoint;
    public float criticalAngle = 20;




    // Start is called before the first frame update
    void Start()
    {
        handPrintMaterial = GetComponent<Renderer>().material;
        handPrintMaterial.DisableKeyword("_EMISSION");
    }


    private void FixedUpdate()
    {
        if (!taskPerformed && isHandStay && pushJoint.Configuration >= criticalAngle) // check angle of the board
        {
            taskPerformed = true;
            if (confetti_PS != null)
            {
                confetti_PS.Play();
            }
            handPrintMaterial.DisableKeyword("_EMISSION");
            StartCoroutine(WaitCoroutine());
        }

    }

    private void OnTriggerStay(Collider other)
    {
        if (!taskPerformed && other.gameObject.name != "XR Origin" && other.gameObject.tag != "noTrigger") 
        {
            handPrintMaterial.EnableKeyword("_EMISSION");
            isHandStay = true;

        }
        
        
    }



    private void OnTriggerExit(Collider other)
    {
        if (!taskPerformed && other.gameObject.name != "XR Origin" && other.gameObject.tag != "noTrigger") // reset if hand leaves surface
        {
            handPrintMaterial.DisableKeyword("_EMISSION");
            isHandStay = false;
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
