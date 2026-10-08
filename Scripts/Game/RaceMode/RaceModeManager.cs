using DG.Tweening;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 레이스의 표시용 진행도를 서버에서 계산하고, FinishLine이 전달한 우승자를 모든 참가자에게 알린다.
/// 경로 지점은 씬에서 편집하는 Transform이며 체크포인트 통과를 강제하는 게임 규칙이 아니다.
/// </summary>
public class RaceModeManager : GameModeManager
{
    [Header("경기 정보용 경로")]
    [SerializeField, InspectorName("경로 지점 (시작 → 경유 지점 → 결승)")]
    [Tooltip("씬의 빈 오브젝트를 진행 순서대로 넣습니다. 지점을 이동하면 표시용 경로도 바뀝니다. 실제 체크포인트나 결승 판정에는 사용하지 않습니다.")]
    private Transform[] _routePoints = System.Array.Empty<Transform>();

    [SerializeField, InspectorName("높이 차이 무시 (XZ 평면)")]
    [Tooltip("직선 수평 코스에서만 사용합니다. 층이나 높이로 경로가 구분되는 코스에서는 끕니다.")]
    private bool _horizontalProgress;

    // 경기 정보는 서버가 0.2초마다 계산하고 PlayerMatchState를 통해 모든 참가자에게 보낸다.
    private const float TelemetryInterval = 0.2f;
    private float _nextTelemetry;
    private bool _raceFinished;
    public int RouteGateCount => Mathf.Max(0, _routePoints.Length - 2);
    public Transform[] RoutePoints => _routePoints;
    public bool HorizontalProgress => _horizontalProgress;

    protected override GameKind GameKind => GameKind.Race;

    private void Update()
    {
        if (!IsServer || _raceFinished || Time.time < _nextTelemetry || GameManager.Instance == null)
            return;

        _nextTelemetry = Time.time + TelemetryInterval;
        foreach (var player in GameManager.Instance.Players)
        {
            if (player == null || !player.TryGetComponent(out PlayerMatchState state))
                continue;

            if (TryEvaluateProgress(player.transform.position, out float progress, out int routePointsBehind))
            {
                // 경로 끝 부근의 위치만으로 완주를 인정하지 않는다. 100%는 FinishLine이 확정한다.
                state.SetRaceTelemetry(Mathf.Min(0.999f, progress), routePointsBehind);
            }
        }
    }

    /// <summary>
    /// 현재 위치를 전체 경로의 가장 가까운 선분에 투영해 진행률을 계산한다.
    /// 앞 지점의 통과 기록을 요구하지 않으므로 우회나 지름길로 뒤 구간에 도착해도 갱신된다.
    /// 뒤로 이동하거나 리스폰하면 현재 위치에 맞춰 진행률도 내려간다.
    /// </summary>
    public bool TryEvaluateProgress(Vector3 position, out float progress, out int routePointsBehind)
    {
        progress = 0f;
        routePointsBehind = 0;
        if (_routePoints == null || _routePoints.Length < 2)
            return false;

        float totalLength = 0f;
        float nearestDistanceSquared = float.PositiveInfinity;
        float distanceAlongRoute = 0f;
        int nearestSegment = 0;
        float nearestSegmentT = 0f;

        for (int i = 0; i < _routePoints.Length - 1; i++)
        {
            // 비어 있는 참조를 이어 붙이면 의도하지 않은 지름길이 생긴다. 잘못된 설정은 계산을 중단한다.
            if (_routePoints[i] == null || _routePoints[i + 1] == null)
                return false;

            Vector3 start = _routePoints[i].position;
            Vector3 segment = ProjectToProgressSpace(_routePoints[i + 1].position - start);
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared < 0.000001f)
                continue;

            float length = Mathf.Sqrt(lengthSquared);
            Vector3 fromStart = ProjectToProgressSpace(position - start);
            // t=0은 선분 시작, t=1은 끝. 범위를 제한해 선분 밖으로 진행률이 연장되지 않게 한다.
            float t = Mathf.Clamp01(Vector3.Dot(fromStart, segment) / lengthSquared);
            float distanceSquared = (fromStart - segment * t).sqrMagnitude;
            if (distanceSquared < nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                distanceAlongRoute = totalLength + length * t;
                nearestSegment = i;
                nearestSegmentT = t;
            }
            totalLength += length;
        }

        if (totalLength < 0.001f)
            return false;

        progress = Mathf.Clamp01(distanceAlongRoute / totalLength);
        // HUD의 지점 수는 현재 구간 뒤에 있는 경유 지점 수다. 실제 체크포인트 통과 기록이 아니다.
        routePointsBehind = Mathf.Min(RouteGateCount, nearestSegment + (nearestSegmentT >= 0.9999f ? 1 : 0));
        return true;
    }

    private Vector3 ProjectToProgressSpace(Vector3 value) =>
        _horizontalProgress ? new Vector3(value.x, 0f, value.z) : value;

    protected override void OnValidate()
    {
        base.OnValidate();
        // 씬을 설정하는 중에는 빈 배열을 허용하되, 등록한 배열의 누락은 위치와 함께 알려준다.
        for (int i = 0; i < _routePoints.Length; i++)
            if (_routePoints[i] == null)
                Debug.LogWarning($"레이스 경로 {i + 1}번 지점이 비어 있습니다. 경로 참조를 지정하세요.", this);
    }

    public override void ReportPlayerFinished(ulong clientId)
    {
        // FinishLine의 서버 충돌 판정에서 호출된다. 첫 완주자만 확정하고 중복 요청은 무시한다.
        if (IsServer && !_raceFinished)
        {
            _raceFinished = true;
            foreach (var player in GameManager.Instance.Players)
                if (
                    player.TryGetComponent(out PlayerMatchState state)
                    && state.OwnerClientId == clientId
                )
                    state.SetRaceTelemetry(1f, RouteGateCount);
            DeclareWinnerRpc(clientId);
        }
    }

    [Rpc(SendTo.Everyone)]
    public void DeclareWinnerRpc(ulong winnerClientId)
    {
        // 서버가 확정한 결과를 각 참가자의 화면에 표시한다.
        if (_resultPanel != null)
        {
            _resultPanel.SetActive(true);
            if (_resultText != null)
                _resultText.transform.DOPunchScale(Vector3.one * 1.2f, 0.5f);
        }

        // 클라이언트 ID는 접속 식별자다. HUD의 P1, P2 같은 표시 번호와는 다르다.
        if (NetworkManager.Singleton.LocalClientId == winnerClientId)
        {
            AudioManager.SfxPlay(AudioManager.Instance?.Container?.Victory);
            if (_resultText != null)
            {
                _resultText.text = "VICTORY!";
                _resultText.color = Color.yellow;
            }
            EditorLog.Log("내가 1등입니다! 승리!");
        }
        else
        {
            AudioManager.SfxPlay(AudioManager.Instance?.Container?.Defeat);
            if (_resultText != null)
            {
                _resultText.text = "DEFEAT";
                _resultText.color = Color.gray;
            }
            EditorLog.Log($"[{winnerClientId}]번 유저가 우승했습니다. 나는 패배...");
        }

        // 공통 종료 처리로 입력과 경기 흐름을 정리하고 메뉴 복귀를 예약한다.
        GameFinishAction();
    }
}
