using System;
using System.Collections;
using System.Collections.Generic;
using Mujoco;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Events;

public class CollisionObject_LR : MonoBehaviour
{

    // delegate for level 2 - grabbed Objects
    public delegate void ObjectGrabbedEvent(GameObject obj);
    public static event ObjectGrabbedEvent OnObjectGrabbed;
    public AudioSource successSound;
    public AudioSource failureSound;        
    public GameObject outsidePosition = null; //outside position 
    private bool isInsideHand = false; // to detect object grabbing and release



    private void OnTriggerEnter(Collider other)
    {
        if(LevelManager.level == Levels.CollectIngredients)
        {
            if (other.CompareTag("hand_collider"))
            {
                isInsideHand = true;

            }

            if (other.gameObject.name != "XR Origin" && other.tag == "CollectionArea")
            {
                if (!LevelManager.collectedObjects.Contains(gameObject)) // exclude that the list already contains this gameObject 
                {
                    LevelManager.collectedObjects.Add(this.gameObject); // add collected object to list of ingredients
                    PlaySound(successSound);
                    Debug.Log(this.gameObject.name + " collected");
                    //log collision
                    AddToEventBuffer(StreamlinedInputManager.Now, "CollisionEvent", $"{this.gameObject.name}-{other.gameObject.name}", $"{(int)LevelManager.level}", $"{LevelManager.level}");
                }
                
            }
            if (other.gameObject.name == "floor")
            {
                if (!LevelManager.fallenObjects.Contains(gameObject.name) && !LevelManager.collectedObjects.Contains(gameObject)) // exclude that the list already contains this gameObject 
                {
                    LevelManager.fallenObjects.Add(this.gameObject.name); // add fallen object to list
                    if(failureSound != null)
                        PlaySound(failureSound);
                    //teleport outside when fallen
                    switch (gameObject.name)
                    {
                        case "milk_collider":
                            StartCoroutine(TeleportToCorrectPosition(LevelManager.milkBottle, LevelManager.milkBottle.outsidePosition));
                            break;
                        case "sugar_collider":
                            StartCoroutine(TeleportToCorrectPosition(LevelManager.sugarBox, LevelManager.sugarBox.outsidePosition));
                            break;
                        case "flour_collider":
                            StartCoroutine(TeleportToCorrectPosition(LevelManager.flourBox, LevelManager.flourBox.outsidePosition));
                            break ;
                        case "egg1_collider":
                            StartCoroutine(TeleportToCorrectPosition(LevelManager.egg1, LevelManager.egg1.outsidePosition));
                            break;
                        case "egg2_collider":
                            StartCoroutine(TeleportToCorrectPosition(LevelManager.egg2, LevelManager.egg2.outsidePosition));
                            break;
                    }


                    
                    //log collision
                    AddToEventBuffer(StreamlinedInputManager.Now, "CollisionEvent", $"{this.gameObject.name}-{other.gameObject.name}", $"{(int)LevelManager.level}", $"{LevelManager.level}");

                }
            }
        }
        if(LevelManager.level == Levels.PourIngredients && LevelManager.placed == true)
        {

            //if (other.gameObject.name != "XR Origin" && other.tag != this.gameObject.tag && other.tag != "noTrigger")
            if (other.CompareTag("hand_collider"))
            {
                isInsideHand = true;
                
            }

            if(other.gameObject.name == "floor")
            {
                if (!LevelManager.fallenObjects.Contains(gameObject.name) && !LevelManager.pouredObjects.Contains(gameObject)) // exclude that the list already contains this gameObject 
                {
                    LevelManager.fallenObjects.Add(this.gameObject.name); // add fallen object to list
                    if (failureSound != null)
                        PlaySound(failureSound);
                    //teleport outside when fallen
                    switch (gameObject.name)
                    {
                        case "milk_collider":
                            StartCoroutine(TeleportToCorrectPosition(LevelManager.milkBottle, LevelManager.milkBottle.outsidePosition));
                            break;
                        case "sugar_collider":
                            StartCoroutine(TeleportToCorrectPosition(LevelManager.sugarBox, LevelManager.sugarBox.outsidePosition));
                            break;
                        case "flour_collider":
                            StartCoroutine(TeleportToCorrectPosition(LevelManager.flourBox, LevelManager.flourBox.outsidePosition));
                            break;
                        case "egg1_collider":
                            StartCoroutine(TeleportToCorrectPosition(LevelManager.egg1, LevelManager.egg1.outsidePosition));
                            break;
                        case "egg2_collider":
                            StartCoroutine(TeleportToCorrectPosition(LevelManager.egg2, LevelManager.egg2.outsidePosition));
                            break;
                    }
                    //log collision
                    AddToEventBuffer(StreamlinedInputManager.Now, "CollisionEvent", $"{this.gameObject.name}-{other.gameObject.name}", $"{(int)LevelManager.level}", $"{LevelManager.level}");
                }
            }
        }

        if(LevelManager.level == Levels.FlipPancake1 || LevelManager.level == Levels.FlipPancake2) //|| LevelManager.level == Levels.FlipPancake3
        {
            if (other.gameObject.name == "pan_collider" && this.gameObject.name != "spatula_collider")
            {
                // SUCCESS
                LevelManager.flipped = true;
            }
            if (other.gameObject.name == "floor")
            {
                if (!LevelManager.fallenObjects.Contains(gameObject.name)) // exclude that the list already contains this gameObject 
                {
                    LevelManager.fallenObjects.Add(this.gameObject.name); // add fallen object to list
                    //log collision
                    AddToEventBuffer(StreamlinedInputManager.Now, "CollisionEvent", $"{this.gameObject.name}-{other.gameObject.name}", $"{(int)LevelManager.level}", $"{LevelManager.level}");
                }
            }


        }

        if(LevelManager.level == Levels.PlacePancake1 || LevelManager.level == Levels.PlacePancake2) //|| LevelManager.level == Levels.PlacePancake3
        {
            if(this.gameObject.name != "spatula_collider")
            {
                if (other.gameObject.name is "plate" or "pancake_Cooked1_collider" or "pancake_Cooked2_collider")
                {
                    // SUCCESS
                    LevelManager.onPlate = true;
                }
            }
            
            if (other.gameObject.name == "floor")
            {
                if (!LevelManager.fallenObjects.Contains(gameObject.name)) // exclude that the list already contains this gameObject 
                {
                    LevelManager.fallenObjects.Add(this.gameObject.name); // add fallen object to list
                    //log collision
                    AddToEventBuffer(StreamlinedInputManager.Now, "CollisionEvent", $"{this.gameObject.name}-{other.gameObject.name}", $"{(int)LevelManager.level}", $"{LevelManager.level}");
                }
            }
        }

        if(LevelManager.level == Levels.SqueezeSyrup)
        {
            
            if (other.CompareTag("hand_collider"))
            {
                isInsideHand = true;

            }


            if (other.gameObject.name == "floor")
            {
                if (!LevelManager.fallenObjects.Contains(gameObject.name)) // exclude that the list already contains this gameObject 
                {
                    LevelManager.fallenObjects.Add(this.gameObject.name); // add fallen object to list
                    //log collision
                    AddToEventBuffer(StreamlinedInputManager.Now, "CollisionEvent", $"{this.gameObject.name}-{other.gameObject.name}", $"{(int)LevelManager.level}", $"{LevelManager.level}");
                }
            }
        }

       
    }

    private void OnTriggerExit(Collider other)
    {
        if(LevelManager.level == Levels.CollectIngredients)
        {
            if (other.CompareTag("hand_collider"))
            {
                isInsideHand = false; // object release
            }
        }

        if (LevelManager.level == Levels.PourIngredients && LevelManager.placed == true)
        {
            if (other.CompareTag("hand_collider"))
            {
                isInsideHand = false; // object release
            }
        }

        if (LevelManager.level == Levels.SqueezeSyrup)
        {
            if (other.CompareTag("hand_collider"))
            {
                isInsideHand = false; // object release
            }

        }
    }

    public void AddToEventBuffer(double now, string ev, string name, string number, string level)
    {
        // "time_stamp_s" + "," + "participant" + "," + "event" + "," + "name" + "," + "level_number" + "," + "level_name" + Environment.NewLine;
        string addBuffer = $"{now},,{ev.ToString()},{name},{number},{level},{Environment.NewLine}";
        LoggingManager.AddToBuffer("Stage3StudyEvent", addBuffer);
    }

    public void PlaySound(AudioSource sound)
    {
        if (sound != null)
        {
            sound.Play();
        }
    }



    public void FixedUpdate()
    {
        if(LevelManager.level == Levels.PourIngredients && LevelManager.placed == true || LevelManager.level == Levels.SqueezeSyrup || LevelManager.level == Levels.CollectIngredients)
        {
            if (isInsideHand)
            {
                // event delegate to send name of grabbed object
                OnObjectGrabbed?.Invoke(this.gameObject);
            }
        }
    }


    IEnumerator TeleportToCorrectPosition(Ingredient ingr, GameObject pos)
    {
        // teleport
        if (ingr.freeJoint != null)
        {
            MjState.TeleportMjRoot(ingr.freeJoint, pos.transform.position, pos.transform.rotation);
        }


        yield return new WaitForSeconds(0.05f);

        // check 
        bool positionCorrect = Vector3.Distance(ingr.gameObj.transform.position, pos.transform.position) < 0.05f;
        bool rotationCorrect = Quaternion.Angle(ingr.gameObj.transform.rotation, pos.transform.rotation) < 1f;

        if (!positionCorrect || !rotationCorrect)
        {
            // teleport again
            MjState.TeleportMjRoot(ingr.freeJoint, pos.transform.position, pos.transform.rotation);
        }
        else
        {
            yield break;
        }

    }
}
