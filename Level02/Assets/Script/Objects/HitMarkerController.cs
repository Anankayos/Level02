using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class HitMarkerController : MonoBehaviour
{
    [Header("Hit Marker Lines")]
    [SerializeField] private Image[] lines;

    [Header("Durations")]
    [SerializeField] private float visibleDuration = 0.08f;
    [SerializeField] private float fadeDuration    = 0.14f;

    [Header("Colors")]
    [Tooltip("White — standard enemy hit")]
    [SerializeField] private Color colorEnemy        = new Color(1f,  1f,    1f,   1f);

    [Tooltip("Gold — killing blow on enemy")]
    [SerializeField] private Color colorKill         = new Color(1f,  0.85f, 0.1f, 1f);

    [Tooltip("Orange — destructible object hit")]
    [SerializeField] private Color colorDestructible = new Color(1f,  0.55f, 0.1f, 1f);

    private Coroutine _co;

    public void Show(HitType type = HitType.Enemy)
    {
        Color c = type switch
        {
            HitType.Kill         => colorKill,
            HitType.Destructible => colorDestructible,
            _                    => colorEnemy
        };

        if (_co != null) StopCoroutine(_co);

        // ── Activate BEFORE starting coroutine ───────────────
        gameObject.SetActive(true);
        // ─────────────────────────────────────────────────────

        _co = StartCoroutine(Flash(c));
    }

    private IEnumerator Flash(Color c)
    {
        // GameObject is already active — just set color and animate
        SetColor(c);

        yield return new WaitForSeconds(visibleDuration);

        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float a = Mathf.Lerp(1f, 0f, t / fadeDuration);
            SetColor(new Color(c.r, c.g, c.b, a));
            yield return null;
        }

        gameObject.SetActive(false);
    }

    private void SetColor(Color c)
    {
        foreach (var img in lines)
            if (img != null) img.color = c;
    }
}