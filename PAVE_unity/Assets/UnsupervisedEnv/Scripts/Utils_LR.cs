using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Utils_LR
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="path">path inside "Resources folder"</param>
    /// <returns></returns>
    public static string LoadJsonFile(string path)
    {
        // Load the JSON file using Resources.Load()
        TextAsset jsonFile = Resources.Load<TextAsset>(path);

        if (jsonFile != null)
        {
            // JSON file loaded successfully
            Debug.Log("JSON file loaded successfully: " + jsonFile.text);

            return jsonFile.text;

        }
        else
        {
            // JSON file not found
            Debug.LogError("JSON file not found");
        }

        return null;
    }
}
