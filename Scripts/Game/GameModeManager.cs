using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using Unity.Netcode;
using UnityEngine;

public abstract class GameModeManager : NetworkBehaviour
{
    private static GameModeManager _instance;
    public static GameModeManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindAnyObjectByType<GameModeManager>();

                if (_instance == null)
                {
                    EditorLog.LogError("GameModeManager를 찾을 수 없습니다!!!");
                    return null;
                }
            }

            return _instance;
        }
    }

    [Header("시작 설정")]
    [SerializeField]
    protected Collider[] _startColliders; // 시작지점을 막는 콜라이더

    [SerializeField]
    protected GameObject _startPanel; // UI Panel

    [SerializeField]
    protected TMP_Text _countingText; // 카운트다운 텍스트

    [SerializeField]
    protected int _startCount;

    [Header("결과 UI 설정")]
    [SerializeField]
    protected GameObject _resultPanel; // 결과를 띄울 패널

    [SerializeField]
    protected TMP_Text _resultText; // "승리" 또는 "패배" 글자가 들어갈 텍스트

    protected abstract GameKind GameKind { get; }

    protected virtual void Awake()
    {
        _instance = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // 절대 규칙: 오직 방장(서버)만이 게임 시작 권한을 가집니다.
        if (IsServer)
        {
            // 모든 플레이어가 로딩이 완료되었을 때
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnEveryPlayerLoadedScene;
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        if (IsServer)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
            {
                NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -=
                    OnEveryPlayerLoadedScene;
            }
        }
    }

    protected virtual void OnEveryPlayerLoadedScene(
        string sceneName,
        UnityEngine.SceneManagement.LoadSceneMode loadSceneMode,
        List<ulong> clientsCompleted,
        List<ulong> clientsTimedOut
    )
    {
        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnEveryPlayerLoadedScene;

        GameStartRpc();
    }

    [Rpc(SendTo.Everyone)]
    protected virtual void GameStartRpc()
    {
        GameManager.Instance.GameKind = GameKind;

        CountDown();
    }

    protected virtual void CountDown()
    {
        int preValue = _startCount;
        float scale = _countingText.transform.localScale.x;
        Tweener countingTweener = null;

        DOTween
            .To(
                getter: () => _startCount,
                setter: x =>
                {
                    if (preValue != x)
                    {
                        AudioManager.SfxPlay(AudioManager.Instance.Container.MouseClick);
                        countingTweener?.Complete();
                        countingTweener = _countingText
                            .DOScale(0.2f, 1)
                            .From(scale)
                            .SetEase(Ease.Linear);
                    }
                    _countingText.text = (x + 1).ToString();
                    preValue = x;
                },
                endValue: 0,
                duration: _startCount
            )
            .SetEase(Ease.Linear)
            .OnComplete(GameStartAction);
    }

    protected virtual void GameStartAction()
    {
        _startPanel.SetActive(false);
        foreach (var startCollider in _startColliders)
        {
            startCollider.enabled = false;
        }
    }

    protected virtual void GameFinishAction()
    {
        foreach (Player player in GameManager.Instance.players)
        {
            // 1. 플레이어 조작 스크립트 끄기
            player.enabled = false;

            // 2. 허공에 완벽하게 박제하기 (안전장치 추가)
            if (player.TryGetComponent(out Rigidbody rb))
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true; // 중력 무시, 외부 힘 무시
            }

            // 3. 애니메이터 강제 종료 (현재 프레임에서 그대로 멈춤)
            Animator anim = player.GetComponentInChildren<Animator>();
            if (anim != null)
            {
                anim.enabled = false;
            }
        }

        RelayManager.Instance.SetIntentionalDisconnect();

        GameManager.Instance.GameKind = GameKind.None;

        WaitAction.Wait(3f, ReturnToMainMenu);
    }

    public async void ReturnToMainMenu()
    {
        EditorLog.Log("게임 종료! 메인 화면으로 돌아갑니다.");

        // 1. 방장(Server)인지 클라이언트인지에 따라 로비 처리 다르게 하기
        if (IsServer)
        {
            // 방장이면 UGS 매칭 서버에서 방 자체를 폭파시킵니다.
            await RelayManager.Instance.DeleteLobby();
        }
        else
        {
            // 클라이언트면 내 이름만 로비 명부에서 쓱 지우고 나옵니다.
            await RelayManager.Instance.LeaveLobby();
        }

        // 2. 인게임 네트워크(Netcode Relay) 연결 완전히 끊기
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }

        // 3. 비동기 통신 및 넷코드 종료가 완료될 때까지 아주 잠깐 대기
        await System.Threading.Tasks.Task.Delay(500);

        UnityEngine.SceneManagement.SceneManager.LoadScene("MainScene");
    }
}
