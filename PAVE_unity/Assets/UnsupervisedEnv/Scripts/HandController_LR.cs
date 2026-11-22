using UnityEngine;

/// <summary>Preserves student scene references while using the shared controller.</summary>
public class HandController_LR : HandController
{
    protected override void OnEnable()
    {
        base.OnEnable();
        // Migration bridge: the training rule is implemented outside the controller.
        if (GetComponent<WpsTrainingRestriction>() == null)
        {
            gameObject.AddComponent<WpsTrainingRestriction>();
        }
    }
}
