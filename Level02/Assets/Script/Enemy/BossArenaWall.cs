using UnityEngine;

public class BossArenaWall : MonoBehaviour
{
    [Header("Wall Objects — assign all wall colliders here")]
    [SerializeField] private GameObject[] wallObjects;

    private void Awake() => SetWalls(false);

    public void ActivateWalls() => SetWalls(true);
    public void DeactivateWalls() => SetWalls(false);

    private void SetWalls(bool active)
    {
        foreach (var wall in wallObjects)
            if (wall != null) wall.SetActive(active);
    }
    
}