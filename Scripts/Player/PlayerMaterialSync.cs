using Unity.Netcode;
using UnityEngine;

public class PlayerMaterialSync : NetworkBehaviour
{
    [Header("Renderer And Material")]
    [SerializeField]
    private Renderer[] _renderers;

    [SerializeField]
    private SO_PlayerMaterial _playerMaterial; // 모든 스킨과 현재 플레이어의 스킨 정보가 저장된 SO

    private readonly NetworkVariable<int> _skinIndex = new();

    public override void OnNetworkSpawn()
    {
        UpdateSkin(_skinIndex.Value);

        _skinIndex.OnValueChanged += OnSkinChanged;

        if (IsOwner)
        {
            PlayerLoadoutState loadout = PlayerLoadoutState.Instance;
            loadout.InitializeSkin(
                _playerMaterial.DefaultSkinIndex,
                _playerMaterial.AllSkin.Length
            );
            SetSkinIndexServerRpc(loadout.SkinIndex);
        }
    }

    public override void OnNetworkDespawn()
    {
        _skinIndex.OnValueChanged -= OnSkinChanged;
    }

    [ServerRpc]
    private void SetSkinIndexServerRpc(int index)
    {
        if (index < 0 || index >= _playerMaterial.AllSkin.Length)
            return;

        _skinIndex.Value = index;
    }

    private void OnSkinChanged(int preValue, int newValue)
    {
        UpdateSkin(newValue);
    }

    private void UpdateSkin(int index)
    {
        if (index < 0 || index >= _playerMaterial.AllSkin.Length)
            return;

        foreach (Renderer renderer in _renderers)
        {
            renderer.sharedMaterial = _playerMaterial.AllSkin[index];
        }
    }

    private void OnValidate()
    {
        if (_renderers.Length == 0)
        {
            EditorLog.LogError("Rederer[]가 입력되지 않았습니다!!!", this);
        }

        if (!_playerMaterial)
        {
            EditorLog.LogError("SO_PlayerMaterial이 입력되지 않았습니다!!!", this);
        }
    }
}
// PlayerMaterialSync은 플레이어의 입력, 상태 또는 네트워크 표현 중 하나의 독립된 책임을 담당한다.
// 소유자 입력과 서버 판정의 경계를 유지하여 다른 플레이어 인스턴스에서 로직이 중복 실행되지 않도록 한다.
