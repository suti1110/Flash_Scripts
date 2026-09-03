// BattleModeManager는 서버 권한으로 생존 플레이어를 추적하고 최후의 생존자를 승자로 확정한다.
// 클라이언트에는 결과와 UI 연출에 필요한 정보만 전달하여 경기 종료가 중복 처리되지 않도록 한다.

using DG.Tweening; // UI 연출용
using Unity.Netcode;
using UnityEngine;

public class BattleModeManager : GameModeManager
{
    private const float SimultaneousEliminationWindowSeconds = 0.05f;

    // 1. 현재 게임 모드 명시 (부모 클래스에서 사용됨)
    protected override GameKind GameKind => GameKind.Battle;

    private bool _isGameOver = false;
    private Coroutine _pendingWinCheck;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // 서버는 누군가 튕기거나 나가는 것을 감시합니다.
        if (IsServer)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnect;
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;
        }
    }

    private void OnClientDisconnect(ulong clientId)
    {
        EditorLog.Log($"[{clientId}]번 유저가 방을 떠났습니다. 생존자 수를 다시 체크합니다.");
        // 누군가 빡종해서 캐릭터가 사라졌으니, 남은 인원으로 우승자가 가려지는지 확인!
        CheckWinCondition(clientId);
    }

    public override void ReportPlayerEliminated(ulong clientId)
    {
        if (!IsServer || _isGameOver || _pendingWinCheck != null)
            return;

        // 같은 서버 틱 전후로 도착한 치명 피해를 짧게 모은 뒤 생존자를 계산해야
        // 마지막 두 플레이어의 동시 사망을 패킷 처리 순서대로 승패 판정하지 않는다.
        _pendingWinCheck = StartCoroutine(CheckWinConditionAfterTieWindow());
    }

    private System.Collections.IEnumerator CheckWinConditionAfterTieWindow()
    {
        yield return new WaitForSecondsRealtime(SimultaneousEliminationWindowSeconds);
        _pendingWinCheck = null;
        CheckWinCondition();
    }

    // =================================================================
    // 승리 조건 체크 (오직 서버만 호출)
    // - PlayerDeath가 서버에서 배틀 모드 탈락을 확정할 때 이 함수를 호출합니다.
    // =================================================================
    public void CheckWinCondition(ulong disconnectedClientId = 99999)
    {
        if (!IsServer || _isGameOver)
            return;

        int aliveCount = 0;
        ulong lastSurvivorId = 9999; // 아무도 아닌 임시 번호

        foreach (Player player in GameManager.Instance.Players)
        {
            if (!player.TryGetComponent(out PlayerDamage playerDamage))
                continue;

            if (playerDamage.OwnerClientId == disconnectedClientId)
                continue;

            if (playerDamage.IsAlive)
            {
                aliveCount++;
                lastSurvivorId = playerDamage.OwnerClientId;
            }
        }

        // 생존자가 딱 1명 남았다면? 게임 종료!
        if (aliveCount == 1)
        {
            _isGameOver = true;
            EditorLog.Log($"[배틀 모드] 최후의 1인 결정! 우승자: {lastSurvivorId}");
            DeclareWinnerRpc(lastSurvivorId);
        }
        // (선택) 만약 폭탄 같은 걸로 다 같이 죽어서 0명이 됐다면 무승부 처리
        else if (aliveCount == 0)
        {
            _isGameOver = true;
            EditorLog.Log("[배틀 모드] 모두 사망! 무승부입니다.");
            DeclareWinnerRpc(9999);
        }
    }

    // =================================================================
    // 결과 발표 및 게임 마무리 (모두의 화면에서 실행)
    // =================================================================
    [Rpc(SendTo.Everyone)]
    private void DeclareWinnerRpc(ulong winnerClientId)
    {
        // 1. 결과 패널 띄우기
        if (_resultPanel != null)
            _resultPanel.SetActive(true);

        if (_resultText != null)
        {
            // 글자 쾅! 튀어나오는 연출
            _resultText.transform.DOPunchScale(Vector3.one * 1.2f, 0.5f);

            // 2. 승패 분기
            if (winnerClientId == 9999)
            {
                AudioManager.SfxPlay(AudioManager.Instance?.Container?.Draw);
                _resultText.text = "DRAW";
                _resultText.color = Color.gray;
            }
            else if (NetworkManager.Singleton.LocalClientId == winnerClientId)
            {
                AudioManager.SfxPlay(AudioManager.Instance?.Container?.Victory);
                _resultText.text = "VICTORY!";
                _resultText.color = Color.yellow;
            }
            else
            {
                AudioManager.SfxPlay(AudioManager.Instance?.Container?.Defeat);
                _resultText.text = "DEFEAT";
                _resultText.color = Color.red;
            }
        }

        // 3. 부모(GameModeManager)에 만들어둔 마무리 연출 실행!
        // (깃발 올리기 -> 조각상처럼 멈추기 -> 3초 대기 -> 방 폭파 및 로비 이동)
        GameFinishAction();
    }
}
