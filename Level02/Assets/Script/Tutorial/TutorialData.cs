using UnityEngine;

[CreateAssetMenu(fileName = "TutorialData", menuName = "Level02/Tutorial Data", order = 1)]
public class TutorialData : ScriptableObject
{
    public enum TutorialType { PopUp, Overlay }

    [Header("Type")]
    public TutorialType tutorialType = TutorialType.PopUp;

    [Header("Content")]
    [TextArea(3, 6)]
    public string bodyText;

    [Header("Overlay Only")]
    public Sprite image;
    public float holdDuration = 2f;

    [Header("PopUp Only")]
    public float autoHideAfter = 0f;
}
