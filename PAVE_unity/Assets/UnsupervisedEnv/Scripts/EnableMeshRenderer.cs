using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnableMeshRenderer : MonoBehaviour
{
    [SerializeField] private bool activeRenderer = true;
    private bool lastState; // store last known state

    public void Start()
    {
        lastState = activeRenderer;
        ToggleMeshRenderers(activeRenderer);
    }

    public void Update()
    {
        if (activeRenderer != lastState) 
        {
            ToggleMeshRenderers(activeRenderer);
            lastState = activeRenderer;
        }
    }

    private void ToggleMeshRenderers(bool state)
    {
        MeshRenderer[] meshRenderers = GetComponentsInChildren<MeshRenderer>();
        foreach (MeshRenderer renderer in meshRenderers)
        {
            renderer.enabled = state;
        }
    }

    private void OnApplicationQuit()
    {
        ToggleMeshRenderers(true);
    }
}
