using UnityEngine;

public class TutorialManager : MonoBehaviour
{
    public static TutorialManager Instance { get; private set; }

    [Header("References")]
    [SerializeField] private TutorialPopupUI popupUI;
    [SerializeField] private TutorialOverlayUI overlayUI;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void Show(TutorialData data)
    {
        if (data == null)
        {
            Debug.LogWarning("[TutorialManager] TutorialData is null.");
            return;
        }

        Debug.Log("[TutorialManager] Show called. Type: " + data.tutorialType + " | Data: " + data.name);

        switch (data.tutorialType)
        {
            case TutorialData.TutorialType.PopUp:
                if (popupUI != null) popupUI.Show(data);
                else Debug.LogError("[TutorialManager] popupUI not assigned in Inspector.");
                break;

            case TutorialData.TutorialType.Overlay:
                if (overlayUI != null) overlayUI.Show(data);
                else Debug.LogError("[TutorialManager] overlayUI not assigned in Inspector.");
                break;
        }
    }

    public void HideAll()
    {
        if (popupUI != null) popupUI.Hide();
        if (overlayUI != null) overlayUI.Hide();
    }
}
