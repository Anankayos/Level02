using System;
using UnityEngine;

public enum NoiseType { Footstep, Gunshot, Explosion, Impact }

public static class NoiseEmitter
{
    // GameObject source so listeners can ignore their own noise
    public static event Action<Vector3, float, NoiseType, GameObject> OnNoiseEmitted;

    public static void EmitNoise(Vector3 position, float radius, NoiseType type,
                                 GameObject source = null)
    {
        OnNoiseEmitted?.Invoke(position, radius, type, source);
    }
}