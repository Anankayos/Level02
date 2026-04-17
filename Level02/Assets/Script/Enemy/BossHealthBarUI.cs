using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BossHealthBarUI : MonoBehaviour
{
    [Header("Links — assign TutorialCanvas children")]
    [SerializeField] private GameObject   bossHUDRoot;   // new child panel inside TutorialCanvas
    [SerializeField] private Slider       healthSlider;
    [SerializeField] private Image        fillImage;
    [SerializeField] private TextMeshProUGUI bossNameLabel;
    [SerializeField] private TextMeshProUGUI partCountLabel;

    [Header("Colors")]
    [SerializeField] private Color healthyColor  = new Color(0.8f, 0.1f, 0.1f);
    [SerializeField] private Color damagedColor  = new Color(1f,   0.5f, 0f);
    [SerializeField] private Color criticalColor = new Color(1f,   1f,   0f);

    private float _maxHP;

    public void Initialize(float maxHP, string bossName)
    {
        _maxHP = maxHP;
        bossNameLabel.text = bossName;
        healthSlider.maxValue = maxHP;
        healthSlider.value    = maxHP;
        bossHUDRoot.SetActive(true);
    }

    public void UpdateHealth(float currentHP, int partsRemaining)
    {
        healthSlider.value = currentHP;
        partCountLabel.text = $"Parts: {partsRemaining}/5";

        float pct = currentHP / _maxHP;
        fillImage.color = pct > 0.6f ? healthyColor
                        : pct > 0.3f ? damagedColor
                                     : criticalColor;
    }

    public void Hide() => bossHUDRoot.SetActive(false);
}