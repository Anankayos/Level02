using System.Collections;
using UnityEngine;

public class BossBridge : MonoBehaviour
{
    [Header("Bridge")]
    [SerializeField] private GameObject bridgeObject;
    [SerializeField] private float      riseHeight   = 3f;   // how far below it starts
    [SerializeField] private float      riseDuration = 1.5f; // seconds to rise up

    [Header("Optional VFX")]
    [SerializeField] private GameObject spawnVFX;

    private Vector3 _targetPos;
    private bool    _spawned = false;

    private void Awake()
    {
        if (bridgeObject != null)
        {
            _targetPos = bridgeObject.transform.position;
            // Start sunken below floor
            bridgeObject.transform.position = _targetPos - Vector3.up * riseHeight;
            bridgeObject.SetActive(false);
        }
    }

    public void SpawnBridge()
    {
        if (_spawned || bridgeObject == null) return;
        _spawned = true;
        StartCoroutine(RiseBridge());
    }

    private IEnumerator RiseBridge()
    {
        bridgeObject.SetActive(true);

        if (spawnVFX)
            Instantiate(spawnVFX, _targetPos, Quaternion.identity);

        Vector3 startPos = bridgeObject.transform.position;
        float   elapsed  = 0f;

        while (elapsed < riseDuration)
        {
            elapsed += Time.deltaTime;
            float t  = Mathf.SmoothStep(0f, 1f, elapsed / riseDuration);
            bridgeObject.transform.position = Vector3.Lerp(startPos, _targetPos, t);
            yield return null;
        }

        bridgeObject.transform.position = _targetPos;
    }

    public void ResetBridge()
    {
        _spawned = false;
        if (bridgeObject != null)
        {
            bridgeObject.transform.position = _targetPos - Vector3.up * riseHeight;
            bridgeObject.SetActive(false);
        }
    }
}