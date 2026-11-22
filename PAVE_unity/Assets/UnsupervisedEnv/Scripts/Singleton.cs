using UnityEngine;

/// <summary>Legacy student-task singleton. Unity lookup and creation must run on the main thread.
/// Auto-created instances survive scene changes; prefer explicit scene references in new task code.</summary>
public class Singleton<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T instance;
    private static readonly object gate = new object ();
    public static T Instance
    {
        get
        {
            if (applicationIsQuitting)
            {
                Debug.LogWarning("[Singleton] Instance '" + typeof(T) + "' already destroyed on application quit." + " Won't create again - returning null.");
                return null;
            }

            lock (gate)
            {
                if (instance == null)
                {
                    instance = (T)FindFirstObjectByType(typeof(T));
                    if (FindObjectsByType(typeof(T), FindObjectsSortMode.InstanceID).Length > 1)
                    {
                        Debug.LogError("[Singleton] Something went really wrong " + " - there should never be more than 1 singleton!" + " Reopening the scene might fix it.");
                        return instance;
                    }

                    if (instance == null)
                    {
                        GameObject singleton = new GameObject();
                        instance = singleton.AddComponent<T>();
                        singleton.name = "(singleton) " + typeof(T).ToString();
                        DontDestroyOnLoad(singleton);
                        Debug.Log("[Singleton] An instance of " + typeof(T) + " is needed in the scene, so '" + singleton + "' was created with DontDestroyOnLoad.");
                    }
                }

                return instance;
            }
        }
    }

    private static bool applicationIsQuitting = false;
    /// <summary>Existing tasks stop automatic recreation after destruction, including shutdown.</summary>
    public void OnDestroy()
    {
        applicationIsQuitting = true;
    }
}
