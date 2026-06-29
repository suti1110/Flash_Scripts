using Unity.Netcode;
using UnityEngine;

public class PlayerDeath : NetworkBehaviour, IDeathable
{
    private static readonly int COLOR_CODE = Shader.PropertyToID("_BaseColor");

    private readonly PlayerStateManager _state = PlayerStateManager.Instance;

    [SerializeField]
    private Rigidbody _rb;

    [SerializeField]
    private SO_GameModeFilter _deathModeFilter;

    [SerializeField]
    private GameObject _playerVisual;

    [SerializeField]
    private Renderer[] _renderers;
    private MaterialPropertyBlock _propertyBlock;

    [SerializeField]
    private ParticleSystem _smoke;

    [SerializeField]
    private float _ghostAlpha;

    public void Death()
    {
        _state[gameObject].State = PlayerState.Dead;

        if (IsOwner)
        {
            DieServerRpc(); // 서버에게 알림
        }
    }

    [Rpc(SendTo.Server)]
    private void DieServerRpc()
    {
        // 서버에서는 매니저에게 생존자 체크를 지시합니다. (이전 배틀 모드 매니저 활용)
        if (_deathModeFilter != null && _deathModeFilter.IsAllowed(GameManager.Instance.GameKind))
        {
            if (BattleModeManager.MyInstance != null)
                BattleModeManager.MyInstance.CheckWinCondition();
        }

        // 모든 사람들의 화면에서 이 캐릭터를 숨기라고 명령!
        DieClientRpc();
    }

    [Rpc(SendTo.Everyone)]
    private void DieClientRpc()
    {
        EditorLog.Log($"[{OwnerClientId}]번 플레이어 탈락!");

        foreach (Collider collider in GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = false;
        }

        ParticleSystem smoke = Instantiate(_smoke, transform.position, Quaternion.identity);
        smoke.Play();
        Destroy(smoke.gameObject, 2f); // 이펙트 찌꺼기 정리

        if (TryGetComponent(out Unity.Netcode.Components.NetworkAnimator netAnim))
        {
            netAnim.enabled = false;
        }

        if (TryGetComponent(out Unity.Netcode.Components.NetworkRigidbody netRb))
        {
            netRb.enabled = false;
        }

        // 기획상 유령끼리 볼 필요가 없다면 NetworkTransform도 꺼버립니다.
        if (TryGetComponent(out Unity.Netcode.Components.NetworkTransform netTransform))
        {
            netTransform.enabled = false;
        }

        if (!IsOwner)
        {
            _playerVisual.SetActive(false);
        }
        else
        {
            _propertyBlock = new();
            foreach (Renderer renderer in _renderers)
            {
                Color color = renderer.sharedMaterial.color;
                color.a = _ghostAlpha;
                _propertyBlock.SetColor(COLOR_CODE, color);
                renderer.SetPropertyBlock(_propertyBlock);
            }
        }
    }

    private void OnValidate()
    {
        if (!_deathModeFilter)
        {
            EditorLog.LogError("SO_GameModeFilter가 할당되지 않았습니다!", this);
        }

        if (!_rb)
        {
            EditorLog.LogError("Rigidbody가 할당되지 않았습니다!", this);
        }

        if (!_playerVisual)
        {
            EditorLog.LogError("PlayerVisual이 할당되지 않았습니다!", this);
        }

        if (_renderers.Length == 0)
        {
            EditorLog.LogError("Renderer가 할당되지 않았습니다!", this);
        }

        if (!_smoke)
        {
            EditorLog.LogError("ParticleSystem이 할당되지 않았습니다!", this);
        }
    }
}
