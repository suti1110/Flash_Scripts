using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>서버가 공유한 경기 정보를 읽어 내 순위 또는 전체 생존 인원만 표시한다.</summary>
public sealed class MatchInfoUI : MonoBehaviour
{
    [Header("레이스 · 오른쪽 아래")]
    [SerializeField, InspectorName("내 순위")] private TMP_Text _rank;
    [SerializeField, InspectorName("경로 지점 수")] private TMP_Text _route;

    [Header("순위별 Color Gradient")]
    [SerializeField, InspectorName("1등 · 금색")] private TMP_ColorGradient _firstPlaceGradient;
    [SerializeField, InspectorName("2등 · 은색")] private TMP_ColorGradient _secondPlaceGradient;
    [SerializeField, InspectorName("3등 · 동색")] private TMP_ColorGradient _thirdPlaceGradient;
    [SerializeField, InspectorName("4등 이하 · 어두운 회색")] private TMP_ColorGradient _remainingPlaceGradient;

    [Header("배틀 · 중앙 위")]
    [SerializeField, InspectorName("생존 인원")] private TMP_Text _aliveCount;

    private readonly List<PlayerMatchState> _players = new(4);
    private float _nextRefresh;

    private void Update()
    {
        if (Time.unscaledTime < _nextRefresh || GameManager.Instance == null)
            return;
        _nextRefresh = Time.unscaledTime + 0.2f;
        _players.Clear();
        foreach (var player in GameManager.Instance.Players)
            if (player != null && player.TryGetComponent(out PlayerMatchState state) && state.IsSpawned)
                _players.Add(state);

        bool race = GameManager.Instance.GameKind == GameKind.Race;
        if (_rank != null) _rank.gameObject.SetActive(race);
        if (_route != null) _route.gameObject.SetActive(race);
        if (_aliveCount != null) _aliveCount.gameObject.SetActive(!race);

        if (race)
        {
            if (_rank == null || _route == null)
                return;
            // 다른 선수의 진행도는 순위 계산에만 사용하고 목록으로 표시하지 않는다.
            // 같은 진행도에서는 선수 번호로 순서를 고정해 화면이 불필요하게 흔들리지 않게 한다.
            _players.Sort((a, b) => a.RaceProgress != b.RaceProgress
                ? b.RaceProgress.CompareTo(a.RaceProgress) : a.PlayerNumber.CompareTo(b.PlayerNumber));
            int localIndex = _players.FindIndex(player => player.IsOwner);
            if (localIndex < 0)
            {
                _rank.text = "--";
                _route.text = "ROUTE --";
                return;
            }
            var mode = GameModeManager.Instance as RaceModeManager;
            PresentRace(localIndex + 1, _players[localIndex].RouteGates, mode != null ? mode.RouteGateCount : 0);
        }
        else if (_aliveCount != null)
        {
            int alive = 0;
            foreach (var player in _players)
                if (player.TryGetComponent(out PlayerDamage damage) && damage.IsAlive)
                    alive++;
            _aliveCount.text = $"ALIVE  {alive} / {_players.Count}";
        }
    }

    // 글꼴·크기·명암은 씬과 재질에서 설정하고, 여기서는 문자열과 순위별 프리셋만 갱신한다.
    private void PresentRace(int rank, int routePointsBehind, int totalRoutePoints)
    {
        // 영문 순위 접미사. 11·12·13등은 끝자리와 관계없이 TH를 사용한다.
        int lastTwoDigits = rank % 100;
        string suffix = lastTwoDigits >= 11 && lastTwoDigits <= 13 ? "TH"
            : rank % 10 == 1 ? "ST" : rank % 10 == 2 ? "ND" : rank % 10 == 3 ? "RD" : "TH";
        _rank.text = $"{rank}<size=45%>{suffix}</size>";
        _rank.colorGradientPreset = rank == 1 ? _firstPlaceGradient
            : rank == 2 ? _secondPlaceGradient : rank == 3 ? _thirdPlaceGradient : _remainingPlaceGradient;
        // Route는 표시용 경로 지점 수이며 실제 체크포인트 통과 판정이 아니다.
        _route.text = $"ROUTE  {routePointsBehind} / {totalRoutePoints}";
    }
}

