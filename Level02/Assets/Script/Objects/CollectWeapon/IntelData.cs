using UnityEngine;

[System.Serializable]
public class IntelData
{
    public string title;
    [TextArea(3, 10)]
    public string content;
    public Sprite thumbnail; // optional illustration shown in UI
}