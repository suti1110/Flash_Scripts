using DG.Tweening;
using TMPro;
using Unity.Netcode;
using UnityEngine;

public class RaceModeManager : GameModeManager
{
    protected override GameKind GameKind => GameKind.Race;

    public static RaceModeManager MyInstance => Instance as RaceModeManager;

    [Rpc(SendTo.Everyone)]
    public void DeclareWinnerRpc(ulong winnerClientId)
    {
        // 1. 패널 띄우기
        if (_resultPanel != null)
        {
            _resultPanel.SetActive(true);
            _resultText.transform.DOPunchScale(Vector3.one * 1.2f, 0.5f);
        }

        // 2. 내 번호와 우승자의 번호를 비교해서 승패를 가릅니다!
        if (NetworkManager.Singleton.LocalClientId == winnerClientId)
        {
            // 우승자 번호가 내 번호랑 똑같다면? 내가 1등!
            if (_resultText != null)
            {
                _resultText.text = "VICTORY!";
                _resultText.color = Color.yellow;
            }
            EditorLog.Log("내가 1등입니다! 승리!");
        }
        else
        {
            // 번호가 다르다면? 나는 2, 3, 4등 중 한 명이므로 패배!
            if (_resultText != null)
            {
                _resultText.text = "DEFEAT";
                _resultText.color = Color.gray;
            }
            EditorLog.Log($"[{winnerClientId}]번 유저가 우승했습니다. 나는 패배...");
        }

        GameFinishAction();
    }
}
