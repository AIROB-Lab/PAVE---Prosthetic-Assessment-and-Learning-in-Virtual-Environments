using UnityEngine;

/// <summary>Study-specific axis restriction for the existing isolated WPS exercise.</summary>
public class WpsTrainingRestriction : MonoBehaviour
{
    HandController controller;
    TaskManager task;
    void Awake()
    {
        controller = GetComponent<HandController>();
    }

    void Start()
    {
        task = FindFirstObjectByType<TaskManager>();
    }

    void Update()
    {
        bool restrict = task != null && TaskManager.wpsTask;
        controller.SetAxisRestricted(DOA.HOC, restrict);
        controller.SetAxisRestricted(DOA.WFE, restrict);
    }

    void OnDisable()
    {
        if (controller == null)
        {
            return;
        }

        controller.SetAxisRestricted(DOA.HOC, false);
        controller.SetAxisRestricted(DOA.WFE, false);
    }
}
