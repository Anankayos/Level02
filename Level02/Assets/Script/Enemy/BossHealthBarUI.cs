using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BossHealthBarUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject      bossHUDRoot;
    [SerializeField] private Slider          healthSlider;
    [SerializeField] private Image           fillImage;
    [SerializeField] private TextMeshProUGUI bossNameLabel;
    [SerializeField] private TextMeshProUGUI partCountLabel;

    [Header("Fill Colors")]
    [SerializeField] private Color healthyColor  = new Color(0.8f, 0.1f, 0.1f, 1f);
    [SerializeField] private Color damagedColor  = new Color(1f,   0.5f, 0f,   1f);
    [SerializeField] private Color criticalColor = new Color(1f,   1f,   0f,   1f);

    private float _maxHP;

    private void Awake()
    {
        // Always start hidden — overrides any Editor active state
        if (bossHUDRoot != null)
            bossHUDRoot.SetActive(false);
    }

    public void Initialize(float maxHP, string bossName)
    {
        _maxHP = maxHP;

        // Must activate root BEFORE touching child components
        bossHUDRoot.SetActive(true);

        healthSlider.minValue = 0f;
        healthSlider.maxValue = 1f;
        healthSlider.value    = 1f;

        if (bossNameLabel)  bossNameLabel.text  = bossName;
        if (partCountLabel) partCountLabel.text = "Parts: 5 / 5";
        if (fillImage)      fillImage.color     = healthyColor;
    }

    public void UpdateHealth(float currentHP, int partsRemaining)
    {
        if (_maxHP <= 0f) return;

        float pct          = Mathf.Clamp01(currentHP / _maxHP);
        healthSlider.value = pct;

        if (partCountLabel) partCountLabel.text = $"Parts: {partsRemaining} / 5";

        if (fillImage)
            fillImage.color = pct > 0.6f ? healthyColor
                            : pct > 0.3f ? damagedColor
                                         : criticalColor;
    }

    public void Hide()
    {
        if (bossHUDRoot != null)
            bossHUDRoot.SetActive(false);
    }
}