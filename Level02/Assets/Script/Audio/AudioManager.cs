using System.Collections;
using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Music Tracks")]
    public AudioClip mainSceneMusic;
    public AudioClip battleMusic;
    public AudioClip bossBattleMusic;

    [Header("Settings")]
    [Range(0f, 1f)] public float musicVolume = 0.8f;
    public float crossfadeDuration = 1.5f;

    private AudioSource[] _sources = new AudioSource[2];
    private int _activeSource = 0;

    public enum MusicState { Main, Battle, Boss }
    private MusicState _currentState;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        for (int i = 0; i < 2; i++)
        {
            _sources[i] = gameObject.AddComponent<AudioSource>();
            _sources[i].loop = true;
            _sources[i].playOnAwake = false;
            _sources[i].volume = 0f;
        }
    }

    void Start() => PlayMusic(MusicState.Main);

    public void PlayMusic(MusicState state)
    {
        if (_currentState == state && _sources[_activeSource].isPlaying) return;
        _currentState = state;

        AudioClip clip = state switch
        {
            MusicState.Battle => battleMusic,
            MusicState.Boss   => bossBattleMusic,
            _                 => mainSceneMusic
        };

        StartCoroutine(Crossfade(clip));
    }

    private IEnumerator Crossfade(AudioClip newClip)
    {
        int next = 1 - _activeSource;
        _sources[next].clip = newClip;
        _sources[next].Play();

        float elapsed = 0f;
        while (elapsed < crossfadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / crossfadeDuration;
            _sources[_activeSource].volume = Mathf.Lerp(musicVolume, 0f, t);
            _sources[next].volume          = Mathf.Lerp(0f, musicVolume, t);
            yield return null;
        }

        _sources[_activeSource].Stop();
        _activeSource = next;
    }
}