using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class TierHeightController : MonoBehaviour
{
    public TMP_InputField topField;
    public TMP_InputField midField;
    public TMP_InputField botField;

    [Tooltip("Local Y (relative to each cupboard) if false; world Y if true")]
    public bool useWorldPosition = false;

    [Tooltip("How much each +/- click moves the shelf, in metres (0.01 = 1 cm)")]
    public float step = 0.01f;

    [Header("Initial shelf heights (metres) - applied on Start")]
    [Tooltip("If true, force the heights below at startup. If false, keep whatever the shelves already have in the scene.")]
    public bool applyInitialHeightsOnStart = true;
    public float initialBotY = 0.5f;
    public float initialMidY = 1.0f;
    public float initialTopY = 1.5f;

    [Tooltip("Re-apply the chosen height every frame so the MuJoCo solver can't overwrite it")]
    public bool holdAgainstPhysics = true;

    [Tooltip("Print how many levels were found for each tier at startup")]
    public bool verbose = false;

    // one entry per tier (top/mid/bot)
    class Tier
    {
        public Transform[] levels;
        public TMP_InputField field;
        public float targetY;
    }

    readonly List<Tier> tiers = new List<Tier>();

    void Start()
    {
        AddTier(topField, "level_top", initialTopY);
        AddTier(midField, "level_mid", initialMidY);
        AddTier(botField, "level_bot", initialBotY);
    }

    void AddTier(TMP_InputField field, string levelName, float initialY)
    {
        var levels = FindAllByName(levelName);
        if (verbose) Debug.Log($"[TierHeight] {levelName}: found {levels.Length} level(s).");
        if (field == null || levels.Length == 0) return;

        // Inspector value is the source of truth at startup; otherwise keep the scene's current Y.
        float startY = applyInitialHeightsOnStart ? initialY : CurrentY(levels);

        var tier = new Tier { levels = levels, field = field, targetY = startY };
        tiers.Add(tier);

        SetY(levels, startY);                       // apply the initial height immediately
        field.onEndEdit.AddListener(text => ApplyText(tier, text));
        field.SetTextWithoutNotify(startY.ToString("0.###"));

        // make room on the left so typed text doesn't sit under the buttons
        if (field.textViewport != null)
        {
            var vp = field.textViewport;
            vp.offsetMin = new Vector2(Mathf.Max(vp.offsetMin.x, 30f), vp.offsetMin.y);
        }

        // + on the top half, - on the bottom half, inside the box on the left
        MakeStepButton(field, tier, "+", true, +step);
        MakeStepButton(field, tier, "\u2212", false, -step); // − minus sign
    }

    // Runs after Update and after the MuJoCo plugin has written its transforms,
    // so the height we re-assert here is the one that survives on screen.
    void LateUpdate()
    {
        if (!holdAgainstPhysics) return;
        foreach (var tier in tiers)
            SetY(tier.levels, tier.targetY);
    }

    void MakeStepButton(TMP_InputField field, Tier tier, string label, bool topHalf, float delta)
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
        btn.onClick.AddListener(() => Step(tier, delta));

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

    void Step(Tier tier, float delta)
    {
        tier.targetY += delta;
        SetY(tier.levels, tier.targetY);
        tier.field.SetTextWithoutNotify(tier.targetY.ToString("0.###"));
    }

    void ApplyText(Tier tier, string text)
    {
        if (!float.TryParse(text, out float h)) { Debug.LogWarning($"'{text}' isn't a valid number."); return; }
        tier.targetY = h;
        SetY(tier.levels, tier.targetY);
        tier.field.SetTextWithoutNotify(tier.targetY.ToString("0.###"));
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
        var result = new List<Transform>();
        foreach (var t in all)
            if (t.name == targetName && t.gameObject.scene.IsValid())
                result.Add(t);
        return result.ToArray();
    }
}
