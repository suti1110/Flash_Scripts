using Unity.Netcode;
using UnityEngine;

public class PlayerSwordSync : NetworkBehaviour
{
    [Header("Renderer And MeshFilter")]
    [SerializeField]
    private Renderer _swordRenderer;

    [SerializeField]
    private MeshFilter _swordMeshFilter;

    [SerializeField]
    private SO_Sword _sword;

    /// <summary>
    /// 플레이어가 장착 중인 검의 MeshRenderer입니다.
    /// </summary>
    public MeshRenderer SwordMeshRenderer => _swordRenderer as MeshRenderer;

    /// <summary>
    /// 플레이어가 장착 중인 검의 MeshFilter입니다.
    /// </summary>
    public MeshFilter SwordMeshFilter => _swordMeshFilter;

    private readonly NetworkVariable<int> _swordIndex = new();

    public override void OnNetworkSpawn()
    {
        UpdateSword(_swordIndex.Value);

        _swordIndex.OnValueChanged += OnSkinChanged;

        if (IsOwner)
        {
            PlayerLoadoutState loadout = PlayerLoadoutState.Instance;
            loadout.InitializeSword(_sword.DefaultSwordIndex, _sword.AllSwords.Length);
            SetSkinIndexServerRpc(loadout.SwordIndex);
        }
    }

    public override void OnNetworkDespawn()
    {
        _swordIndex.OnValueChanged -= OnSkinChanged;
    }

    [ServerRpc]
    private void SetSkinIndexServerRpc(int index)
    {
        if (index < 0 || index >= _sword.AllSwords.Length)
            return;

        _swordIndex.Value = index;
    }

    private void OnSkinChanged(int preValue, int newValue)
    {
        UpdateSword(newValue);
    }

    private void Awake()
    {
        InitializeDefaultSwordInEditor();
    }

    private void UpdateSword(int index)
    {
        if (index < 0 || index >= _sword.AllSwords.Length)
            return;

        _swordRenderer.sharedMaterial = _sword.AllSwords[index].Material;
        _swordMeshFilter.mesh = _sword.AllSwords[index].Mesh;
    }

    private void InitializeDefaultSwordInEditor()
    {
        if (_sword != null && _sword.AllSwords != null && _sword.AllSwords.Length > 0)
        {
            if (
                _swordMeshFilter != null
                && _swordMeshFilter.sharedMesh == null
                && _swordRenderer != null
            )
            {
                int defaultIdx = Mathf.Clamp(
                    _sword.DefaultSwordIndex,
                    0,
                    _sword.AllSwords.Length - 1
                );
                _swordRenderer.sharedMaterial = _sword.AllSwords[defaultIdx].Material;
                _swordMeshFilter.sharedMesh = _sword.AllSwords[defaultIdx].Mesh;
            }
        }
    }

    private void OnValidate()
    {
        InitializeDefaultSwordInEditor();

        if (!_swordRenderer)
        {
            EditorLog.LogError("Rederer가 입력되지 않았습니다!!!", this);
        }

        if (!_swordMeshFilter)
        {
            EditorLog.LogError("MeshFilter가 입력되지 않았습니다!!!", this);
        }

        if (!_sword)
        {
            EditorLog.LogError("SO_Sword가 입력되지 않았습니다!!!", this);
        }
    }
}
