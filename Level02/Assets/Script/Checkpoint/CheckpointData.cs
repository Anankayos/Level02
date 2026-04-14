using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class CheckpointData
{
    public Vector3              playerPosition;
    public Quaternion           playerRotation;
    public float                playerHealth;
    public bool                 hasRifle;
    public int                  ammo;
    public List<KeyType>        collectedKeys   = new();
    public int                  atmCards;
    public List<IntelData>      collectedIntel  = new();
    // IDs of objects permanently destroyed/collected BEFORE this checkpoint
    public HashSet<string>      persistentIDs   = new();
}