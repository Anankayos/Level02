using UnityEngine;

/// <summary>
/// Attach anywhere. Call ShowTutorial() from UnityEvents, animation events,
/// buttons, doors, cutscenes, or any script.
/// </summary>
public class TutorialDirectCaller : MonoBehaviour
{
    [SerializeField] private TutorialData tutorialData;

    public void ShowTutorial()
    {
        if (TutorialManager.Instance == null)
        {
            Debug.LogError("[TutorialDirectCaller] TutorialManager not found.");
            return;
        }
        TutorialManager.Instance.Show(tutorialData);
    }
}
