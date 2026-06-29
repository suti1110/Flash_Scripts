using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneToBGM : MonoBehaviour
{
    [System.Serializable]
    public struct SceneToClip
    {
        public string SceneName;
        public AudioClip Clip;
    }

    [SerializeField]
    private SceneToClip[] _sceneToClips;

    private readonly Dictionary<string, AudioClip> _audioClipDic = new();

    private void Awake()
    {
        foreach (var clip in _sceneToClips)
        {
            _audioClipDic[clip.SceneName] = clip.Clip;
        }

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        AudioManager.BgmPlay(_audioClipDic[scene.name]);
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }
}
