using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mujoco;

public class CollisionOnButton : MonoBehaviour
{
    public TaskManager taskManager;

    private AudioSource audioSource;
    public Vector3 pressedOffset;
    private Vector3 originalPosition;
    

    // Start is called before the first frame update
    void Start()
    {
        audioSource = GetComponent<AudioSource>();
        originalPosition = transform.localPosition;

    }

    // Update is called once per frame
    void Update()
    {
       
    }



    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.name != "XR Origin" && other.tag != this.gameObject.tag)
        {
           this.GetComponent<Collider>().isTrigger = false;
            StartCoroutine(PressButton());
        }
        
    }

    private IEnumerator PressButton()
    {
        // press button down
        transform.localPosition = originalPosition + pressedOffset;

        // play sound
        if (audioSource != null && !audioSource.isPlaying)
        {
            audioSource.Play();
        }

        // Wait until button jumps back
        yield return new WaitForSeconds(0.3f);

        // button in original posision
        transform.localPosition = originalPosition;

        yield return new WaitForSeconds(2.0f);

        //taskManager.taskSuccess = true;

        TaskManager.taskState = TaskState.Successful;
        
        taskManager.EndCurrentTask();

    }




}
