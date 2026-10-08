using TMPro;
using Unity.Netcode;
using UnityEngine;

/// <summary>서버가 선수 번호와 표시용 레이스 진행도를 관리하고 모든 참가자에게 공유한다.</summary>
public sealed class PlayerMatchState : NetworkBehaviour
{
    [SerializeField] private TMP_Text _identityLabel;
    [SerializeField] private Color[] _identityColors =
    {
        new(0.15f, 0.85f, 1f), new(1f, 0.65f, 0.18f),
        new(0.85f, 0.5f, 1f), new(0.5f, 1f, 0.35f),
    };
    private readonly NetworkVariable<int> _number = new(0);
    private readonly NetworkVariable<float> _raceProgress = new(0f);
    private readonly NetworkVariable<int> _routeGates = new(0);
    private PlayerDamage _damage;
    private Camera _viewCamera;

    public int PlayerNumber => _number.Value;
    public float RaceProgress => _raceProgress.Value;
    public int RouteGates => _routeGates.Value;
    public Color IdentityColor => ColorForNumber(PlayerNumber);

    private Color ColorForNumber(int number) => _identityColors == null || _identityColors.Length == 0
        ? Color.white : _identityColors[Mathf.Max(0, number - 1) % _identityColors.Length];

    public override void OnNetworkSpawn()
    {
        _damage = GetComponent<PlayerDamage>();
        _number.OnValueChanged += OnNumberChanged;
        RefreshLabel();
    }

    public void SetPlayerNumber(int number)
    {
        if (IsServer) _number.Value = Mathf.Max(1, number);
    }

    // gates는 현재 위치 뒤의 경유 지점 수다. 실제 체크포인트 통과 여부나 완주 조건으로 사용하지 않는다.
    public void SetRaceTelemetry(float progress, int gates)
    {
        if (!IsServer) return;
        _raceProgress.Value = Mathf.Clamp01(progress);
        _routeGates.Value = Mathf.Max(0, gates);
    }

    private void OnNumberChanged(int previous, int current) => RefreshLabel();

    private void RefreshLabel()
    {
        if (_identityLabel == null) return;
        _identityLabel.text = $"P{PlayerNumber}";
        _identityLabel.color = IdentityColor;
    }

    private void LateUpdate()
    {
        if (!IsSpawned || _identityLabel == null) return;
        bool visible = !IsOwner && PlayerNumber > 0 && (_damage == null || _damage.IsAlive);
        _identityLabel.gameObject.SetActive(visible);
        if (!visible) return;
        if (_viewCamera == null) _viewCamera = Camera.main;
        if (_viewCamera != null)
            _identityLabel.transform.rotation = _viewCamera.transform.rotation;
    }

    public override void OnNetworkDespawn()
    {
        _number.OnValueChanged -= OnNumberChanged;
        if (_identityLabel != null) _identityLabel.gameObject.SetActive(false);
    }
}
