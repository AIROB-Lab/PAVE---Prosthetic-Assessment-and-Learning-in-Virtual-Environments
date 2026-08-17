using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TierHeightController : MonoBehaviour
{
    public TMP_InputField topField;
    public TMP_InputField midField;
    public TMP_InputField botField;

    [Tooltip("Local Y (relative to each cupboard) if false; world Y if true")]
    public bool useWorldPosition = false;

    [Tooltip("How much each +/- click moves the shelf, in metres (0.01 = 1 cm)")]
    public float step = 0.01f;

    Transform[] topLevels, midLevels, botLevels;

    void Start()
    {
        topLevels = FindAllByName("level_top");
        midLevels = FindAllByName("level_mid");
        botLevels = FindAllByName("level_bot");
        Hook(topField, topLevels);
        Hook(midField, midLevels);
        Hook(botField, botLevels);
    }

    void Hook(TMP_InputField field, Transform[] levels)
    {
        if (field == null || levels.Length == 0) return;

        field.onEndEdit.AddListener(text => ApplyText(field, levels, text));
        field.SetTextWithoutNotify(CurrentY(levels).ToString("0.###"));

        // make room on the left so typed text doesn't sit under the buttons
        if (field.textViewport != null)
        {
            var vp = field.textViewport;
            vp.offsetMin = new Vector2(Mathf.Max(vp.offsetMin.x, 30f), vp.offsetMin.y);
        }

        // + on the top half, - on the bottom half, inside the box on the left
        MakeStepButton(field, levels, "+", true, +step);
        MakeStepButton(field, levels, "\u2212", false, -step); // − minus sign
    }

    void MakeStepButton(TMP_InputField field, Transform[] levels, string label, bool topHalf, float delta)
    {
        var go = new GameObject(topHalf ? "BtnPlus" : "BtnMinus",
                                typeof(RectTransform), typeof(Image), typeof(Button));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(field.transform, false);
        rt.anchorMin = new Vector2(0f, topHalf ? 0.5f : 0f);
        rt.anchorMax = new Vector2(0f, topHalf ? 1f : 0.5f);
        rt.pivot = new Vector2(0f, topHalf ? 1f : 0f);
        rt.sizeDelta = new Vector2(28f, 0f);
        rt.anchoredPosition = new Vector2(1f, topHalf ? -1f : 1f);

        var img = go.GetComponent<Image>();
        img.color = new Color(0.82f, 0.82f, 0.82f, 1f);

        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => Step(field, levels, delta));

        var lgo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        var lrt = lgo.GetComponent<RectTransform>();
        lrt.SetParent(rt, false);
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
        var txt = lgo.GetComponent<TextMeshProUGUI>();
        txt.text = label;
        txt.alignment = TextAlignmentOptions.Center;
        txt.enableAutoSizing = true; txt.fontSizeMin = 6; txt.fontSizeMax = 32;
        txt.color = Color.black;
        txt.raycastTarget = false;
    }

    void Step(TMP_InputField field, Transform[] levels, float delta)
    {
        float y = CurrentY(levels) + delta;
        SetY(levels, y);
        field.SetTextWithoutNotify(y.ToString("0.###"));
    }

    void ApplyText(TMP_InputField field, Transform[] levels, string text)
    {
        if (!float.TryParse(text, out float h)) { Debug.LogWarning($"'{text}' isn't a valid number."); return; }
        SetY(levels, h);
        field.SetTextWithoutNotify(h.ToString("0.###"));
    }

    float CurrentY(Transform[] levels)
        => useWorldPosition ? levels[0].position.y : levels[0].localPosition.y;

    void SetY(Transform[] levels, float y)
    {
        foreach (var lvl in levels)
        {
            if (useWorldPosition) { var p = lvl.position; p.y = y; lvl.position = p; }
            else { var p = lvl.localPosition; p.y = y; lvl.localPosition = p; }
        }
    }

    Transform[] FindAllByName(string targetName)
    {
        var all = Resources.FindObjectsOfTypeAll<Transform>();
        var result = new System.Collections.Generic.List<Transform>();
        foreach (var t in all)
            if (t.name == targetName && t.gameObject.scene.IsValid())
                result.Add(t);
        return result.ToArray();
    }
}