using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static Unity.VisualScripting.Member;

public class CollisionManager : MonoBehaviour
{
    private static List<(GameObject, GameObject)> triggeredColliders = new List<(GameObject, GameObject)>();

    private string collisionHeader = "time_stamp_s" + "," + "tag1" + "," + "name1" +  "," + "tag2" + "," + "name2" + "," + "value" + Environment.NewLine;
    
    // Start is called before the first frame update
    void Start()
    {
        LoggingManager.CreateNewLog("Collisions", collisionHeader);
    }

    public static void HandleTriggerEnter(float time, GameObject source, GameObject other)
    {
        // no need for duplicates (the other way around)
        if (!triggeredColliders.Contains((other, source)))
        {
            triggeredColliders.Add((source, other));
            print($"new collision {time} {source.name} {other.name}");

            string _addBuffer = StreamlinedInputManager.Now + "," + source.tag + "," + source.name + "," + other.tag + "," + other.name + "," + "Started" + Environment.NewLine;
            LoggingManager.AddToBuffer("Collisions", _addBuffer);
        }
    }

    public static void HandleTriggerExit(float time, GameObject source, GameObject other)
    {
        if (triggeredColliders.Contains((source, other)))
        {
            triggeredColliders.Remove((source, other));
            print($"exit collision {time} {source.name} {other.name}");

            string _addBuffer = StreamlinedInputManager.Now + "," + source.tag + "," + source.name + "," + other.tag + "," + other.name + "," + "Stopped" + Environment.NewLine;
            LoggingManager.AddToBuffer("Collisions", _addBuffer);
        }
    }

    // You can add more methods here to handle OnTriggerStay or other collision-related events.

    // This method can be called to get a list of all currently triggered colliders.
    public static List<(GameObject, GameObject)> GetTriggeredColliders()
    {
        return triggeredColliders;
    }

    public static List<(GameObject, GameObject)> FindCollisionByTag(string tag)
    {
        List<(GameObject, GameObject)> collisions = new List<(GameObject, GameObject)>();
        foreach ( var GOs in triggeredColliders)
        {
            if (GOs.Item1 == null || GOs.Item2 == null) continue;
            if (GOs.Item1.CompareTag(tag) || GOs.Item2.CompareTag(tag))
            {
                collisions.Add((GOs.Item1, GOs.Item2));
            }
        }

        return collisions;
    }

    public static List<(GameObject, GameObject)> FindCollisionBetweenTags(string tag1, string tag2)
    {
        List<(GameObject, GameObject)> collisions = new List<(GameObject, GameObject)>();
        
        foreach (var GOs in triggeredColliders)
        {
            if (GOs.Item1 == null || GOs.Item2 == null) continue;
            if ((GOs.Item1.CompareTag(tag1) && GOs.Item2.CompareTag(tag2)) || (GOs.Item1.CompareTag(tag2) && GOs.Item2.CompareTag(tag1)))
            {
                collisions.Add((GOs.Item1, GOs.Item2));
            }
        }

        return collisions;
    }

    public static List<(GameObject, GameObject)> FindCollisionBetweenTagAndObj(string tag1, string nameObj)
    {
        List<(GameObject, GameObject)> collisions = new List<(GameObject, GameObject)>();

        foreach (var GOs in triggeredColliders)
        {
            if(GOs.Item1 == null || GOs.Item2 == null) continue;
            if ((GOs.Item1.CompareTag(tag1) && GOs.Item2.name == nameObj) || (GOs.Item2.CompareTag(tag1) && GOs.Item1.name == nameObj))
            {
                collisions.Add((GOs.Item1, GOs.Item2));
            }
        }
        return collisions;
    }

    public static List<(GameObject, GameObject)> FindCollisionByNames(string name1, string name2, bool contains=false)
    {
        List<(GameObject, GameObject)> collisions = new List<(GameObject, GameObject)>();

        foreach (var GOs in triggeredColliders)
        {
            // this way it also fires if table is given and GO is Table12
            if (contains)
            {
                if (GOs.Item1 == null || GOs.Item2 == null) continue;
                if ((GOs.Item1.name.ToLower().Contains(name1.ToLower()) && GOs.Item2.name.ToLower().Contains(name2.ToLower())) || (GOs.Item2.name.ToLower().Contains(name1.ToLower()) && GOs.Item1.name.ToLower().Contains(name2.ToLower())))
                {
                    collisions.Add((GOs.Item1, GOs.Item2));
                }
            }
            // exact match
            else
            {
                if (GOs.Item1 == null || GOs.Item2 == null) continue;
                if ((GOs.Item1.name == name1 && GOs.Item2.name == name2) || (GOs.Item2.name == name1 && GOs.Item1.name == name2))
                {
                    collisions.Add((GOs.Item1, GOs.Item2));
                }
            }
        }
        return collisions;
    }
}
