using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneToBGM : MonoBehaviour
{
    private static SceneToBGM _instance;

    [System.Serializable]
    public struct SceneToClip
    {
        public OnlyOneUnityString SceneName;
        public AudioClip Clip;
    }

    [SerializeField]
    private SceneToClip[] _sceneToClips;

    private readonly Dictionary<string, AudioClip> _audioClipDic = new();

    private void Awake()
    {
        if (_instance == null)
            _instance = this;

        foreach (var clip in _sceneToClips)
        {
            if (clip.SceneName == null)
                continue;

            _audioClipDic[clip.SceneName] = clip.Clip;
        }

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyScene(scene.name);
    }

    public static void ApplyForScene(string sceneName)
    {
        if (_instance == null)
            _instance = FindAnyObjectByType<SceneToBGM>();

        _instance?.ApplyScene(sceneName);
    }

    private void ApplyScene(string sceneName)
    {
        if (
            string.IsNullOrWhiteSpace(sceneName)
            || !_audioClipDic.TryGetValue(sceneName, out AudioClip clip)
        )
            return;

        if (clip != null)
            AudioManager.BgmPlay(clip);
        else
            AudioManager.BgmStop();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;

        if (_instance == this)
            _instance = null;
    }
}
// SceneToBGM은 오디오 설정 또는 재생 요청을 한곳에서 관리한다.
// 호출 측이 오디오 소스의 생성과 수명주기를 직접 다루지 않도록 재생 책임을 캡슐화한다.
