using Mujoco;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using UnityEngine;
using Color = UnityEngine.Color;

public class ChangeColorBySensor : MonoBehaviour
{
    public GameObject forceSensorGO;

    public Vector2 minMaxForce;

    // Define colors for the gradient
    Color black = Color.black;
    Color deepRed = new Color(0.5f, 0f, 0f);
    Color orange = new Color(1f, 0.5f, 0f);
    Color yellow = Color.yellow;
    Color white = Color.white;



    // Create a color gradient
    private Gradient gradient = new Gradient();
  
    // Start is called before the first frame update
    void Start()
    {
        gradient.SetKeys(
        new GradientColorKey[] {
                new GradientColorKey(black, 0f),
                new GradientColorKey(deepRed, 0.25f),
                new GradientColorKey(orange, 0.5f),
                new GradientColorKey(yellow, 0.75f),
                new GradientColorKey(white, 1f)
        },
        new GradientAlphaKey[] {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
        }
    );
      
    }

    // Update is called once per frame
    void Update()
    {
        
            if(forceSensorGO != null)
            {
                // get the force value component
                float forceZ = forceSensorGO.GetComponent<MjSiteVectorSensor>().SensorReading.z;
                float mappedForce = forceZ.Remap(minMaxForce.x, minMaxForce.y, 0, 1);

                // set it to the material of the gameobject
                // this.transform.gameObject.GetComponent<Renderer>().material.color = gradient.Evaluate(mappedForce);

                // Enable emission and set emission color
                Color emissionColor = gradient.Evaluate(mappedForce) * 1.0f;
                this.transform.gameObject.GetComponent<Renderer>().material.EnableKeyword("_EMISSION");
                this.transform.gameObject.GetComponent<Renderer>().material.SetColor("_EmissionColor", emissionColor);
            }

        

    }
}
