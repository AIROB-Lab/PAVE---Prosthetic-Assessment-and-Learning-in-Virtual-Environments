using UnityEngine;
using UnityEngine.UI;

public class TargetColourSwitch : MonoBehaviour
{
    public Slider actualSlider;   // drag FillingBar_actual here
    public Slider shouldSlider;   // drag FillingBar_should here
    [Range(0f, 1f)] public float tolerance = 0.1f; // how close counts as "inside"
    public Color outsideColor = Color.red;
    public Color insideColor = Color.green;
    private Image img;

    void Start()
    {
        img = GetComponent<Image>();
    }

    void Update()
    {
        if (actualSlider == null || shouldSlider == null) return;
        float diff = Mathf.Abs(actualSlider.normalizedValue - shouldSlider.normalizedValue);
        Color c = (diff <= tolerance) ? insideColor : outsideColor;
        c.a = 0.4f;              // always 40% transparent
        img.color = c;
    }
}