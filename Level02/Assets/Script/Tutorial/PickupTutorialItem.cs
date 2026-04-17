using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Add to any pickable item prefab.
/// Call OnPickedUp() from your existing pickup code.
/// Tutorial fires only on the FIRST pickup of each itemTypeId ever.
/// </summary>
public class PickupTutorialItem : MonoBehaviour
{
    [Header("Item Identity")]
    [Tooltip("Shared key for all instances of this item type. e.g. 'medkit', 'keycard', 'sword'")]
    [SerializeField] private string itemTypeId;

    [Header("Tutorial")]
    [SerializeField] private TutorialData tutorialData;

    private static readonly HashSet<string> SeenThisSession = new HashSet<string>();
    private const string PrefPrefix = "TUT_PICKUP_";

    public void OnPickedUp()
    {
        if (string.IsNullOrEmpty(itemTypeId))
        {
            Debug.LogWarning("[PickupTutorialItem] itemTypeId is empty on " + gameObject.name);
            return;
        }

        if (tutorialData == null)
        {
            Debug.LogWarning("[PickupTutorialItem] No TutorialData on " + gameObject.name);
            return;
        }

        if (IsFirstTime(itemTypeId))
        {
            MarkAsSeen(itemTypeId);
            TutorialManager.Instance.Show(tutorialData);
            Debug.Log("[PickupTutorialItem] First pickup of '" + itemTypeId + "' - tutorial shown.");
        }
        else
        {
            Debug.Log("[PickupTutorialItem] Already seen '" + itemTypeId + "' - skipping.");
        }
    }

    private static bool IsFirstTime(string id)
    {
        if (SeenThisSession.Contains(id)) return false;
        if (PlayerPrefs.GetInt(PrefPrefix + id, 0) == 1) return false;
        return true;
    }

    private static void MarkAsSeen(string id)
    {
        SeenThisSession.Add(id);
        PlayerPrefs.SetInt(PrefPrefix + id, 1);
        PlayerPrefs.Save();
    }

    [ContextMenu("Reset Tutorial For This Item")]
    public void ResetForTesting()
    {
        SeenThisSession.Remove(itemTypeId);
        PlayerPrefs.DeleteKey(PrefPrefix + itemTypeId);
        Debug.Log("[PickupTutorialItem] Reset '" + itemTypeId + "'");
    }

    private void OnValidate()
    {
        if (string.IsNullOrEmpty(itemTypeId))
            Debug.LogWarning("[PickupTutorialItem] itemTypeId empty on " + gameObject.name, this);
        if (tutorialData == null)
            Debug.LogWarning("[PickupTutorialItem] No TutorialData on " + gameObject.name, this);
    }
}
