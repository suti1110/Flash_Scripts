using System;
using DG.Tweening;
using TMPro;
using UnityEngine;

public sealed class GameStartPresentation : IDisposable
{
    private readonly Collider[] _startColliders;
    private readonly GameObject _startPanel;
    private readonly TMP_Text _countingText;
    private readonly int _startCount;
    private readonly Vector3 _originalCountingScale;

    private Tween _countdownTween;
    private Tween _countingPulseTween;

    public GameStartPresentation(
        Collider[] startColliders,
        GameObject startPanel,
        TMP_Text countingText,
        int startCount
    )
    {
        _startColliders = startColliders;
        _startPanel = startPanel;
        _countingText = countingText;
        _startCount = Mathf.Max(0, startCount);
        _originalCountingScale =
            countingText != null ? countingText.transform.localScale : Vector3.one;
    }

    public void Play()
    {
        _countdownTween?.Kill();
        ResetCountingScale();

        if (_startCount == 0 || _countingText == null)
        {
            OpenStartGate();
            return;
        }

        int previousValue = _startCount;

        _countdownTween = DOTween
            .To(
                () => _startCount,
                value =>
                {
                    if (previousValue != value)
                    {
                        AudioManager.SfxPlay(AudioManager.Instance.Container.MouseClick);
                        ResetCountingScale();
                        _countingPulseTween = _countingText.DOScale(0.2f, 1f).SetEase(Ease.Linear);
                    }

                    _countingText.SetText("{0:0}", value + 1);
                    previousValue = value;
                },
                0,
                _startCount
            )
            .SetEase(Ease.Linear)
            .OnComplete(OpenStartGate);
    }

    public void Dispose()
    {
        _countdownTween?.Kill();
        _countdownTween = null;
        ResetCountingScale();
    }

    private void OpenStartGate()
    {
        ResetCountingScale();
        AudioManager.SfxPlay(AudioManager.Instance?.Container?.GameStart);

        if (_startPanel != null)
            _startPanel.SetActive(false);

        if (_startColliders == null)
            return;

        foreach (Collider startCollider in _startColliders)
        {
            if (startCollider != null)
                startCollider.enabled = false;
        }
    }

    private void ResetCountingScale()
    {
        _countingPulseTween?.Kill();
        _countingPulseTween = null;

        if (_countingText == null)
            return;

        // 카운트다운과 결과 표시가 같은 텍스트를 공유하므로 트윈의 축소값을 남기지 않는다.
        _countingText.transform.localScale = _originalCountingScale;
    }
}
// GameStartPresentation은 경기 규칙과 진행 상태 중 하나의 독립된 게임플레이 책임을 담당한다.
// 모드별 정책을 분리하여 공통 경기 흐름이 구체적인 모드 구현에 직접 의존하지 않도록 한다.
