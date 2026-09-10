using Unity.Netcode;
using UnityEngine;

public class PlayerDeath : NetworkBehaviour, IDeathable
{
    private static readonly int COLOR_CODE = Shader.PropertyToID("_BaseColor");

    private Player _player;
    private PlayerAnimation _playerAnimation;
    private PlayerDamage _playerDamage;
    private PlayerSpawnHandler _spawnHandler;
    private PlayerMapInteraction _mapInteraction;
    private bool _isDeathResolved;

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

    private void Awake()
    {
        _player = GetComponent<Player>();
        _playerAnimation = GetComponent<PlayerAnimation>();
        _playerDamage = GetComponent<PlayerDamage>();
        _spawnHandler = GetComponent<PlayerSpawnHandler>();
        _mapInteraction = GetComponent<PlayerMapInteraction>();
    }

    public void Death()
    {
        if (!IsServer || _isDeathResolved)
            return;

        GameModeManager gameModeManager = GameModeManager.Instance;
        if (gameModeManager == null)
        {
            EditorLog.LogError("사망을 처리할 GameModeManager를 찾을 수 없습니다!", this);
            return;
        }

        _isDeathResolved = true;
        _mapInteraction?.DropHeldObject();

        bool eliminatesPlayer =
            _deathModeFilter != null && _deathModeFilter.IsAllowed(GameManager.Instance.GameKind);

        if (!eliminatesPlayer)
        {
            RespawnOnServer();
            return;
        }

        gameModeManager.ReportPlayerEliminated(OwnerClientId);
        EliminateRpc();
    }

    private void RespawnOnServer()
    {
        Vector3 deathPosition = transform.position;
        _playerDamage.RestoreFullHealth();

        if (!_spawnHandler.TryRespawnAtSpawnPoint())
            EditorLog.LogError("플레이어의 최초 스폰 지점을 찾을 수 없습니다!", this);

        RespawnRpc(deathPosition);
        _isDeathResolved = false;
    }

    [Rpc(SendTo.Everyone)]
    private void RespawnRpc(Vector3 deathPosition)
    {
        PlaySmoke(deathPosition);
        PlayPlayerAudio(AudioManager.Instance?.Container?.Death, deathPosition);
        PlayPlayerAudio(AudioManager.Instance?.Container?.Respawn, transform.position);

        // 치명타와 리스폰이 같은 네트워크 프레임에 처리되면 OwnerNetworkAnimator가
        // 중간 피격 Bool을 전송하지 않을 수 있으므로 모든 화면에서 명시적으로 해제한다.
        _playerAnimation?.ClearImmediateDamageReaction();

        if (IsOwner)
        {
            _player.ResetAfterRespawn();
        }
    }

    [Rpc(SendTo.Everyone)]
    private void EliminateRpc()
    {
        EditorLog.Log($"[{OwnerClientId}]번 플레이어 탈락!");
        PlayPlayerAudio(AudioManager.Instance?.Container?.Death, transform.position);

        if (IsOwner)
            _player.StateMachine.TryChangeState<PlayerDeadState>();

        foreach (Collider collider in GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = false;
        }

        PlaySmoke(transform.position);

        if (TryGetComponent(out Unity.Netcode.Components.NetworkAnimator netAnim))
        {
            netAnim.enabled = false;
        }

        if (TryGetComponent(out Unity.Netcode.Components.NetworkRigidbody netRb))
        {
            netRb.enabled = false;
        }

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

    private void PlaySmoke(Vector3 position)
    {
        if (_smoke == null)
            return;

        ParticleSystem smoke = Instantiate(_smoke, position, Quaternion.identity);
        smoke.Play();
        Destroy(smoke.gameObject, 2f);
    }

    private static void PlayPlayerAudio(AudioClip clip, Vector3 position)
    {
        AudioManager.SfxPlayAtPoint(clip, position);
    }

    private void OnValidate()
    {
        if (!_deathModeFilter)
            EditorLog.LogError("사망 판정용 SO_GameModeFilter가 할당되지 않았습니다!", this);

        if (!_rb)
            EditorLog.LogError("Rigidbody가 할당되지 않았습니다!", this);

        if (!_playerVisual)
            EditorLog.LogError("PlayerVisual이 할당되지 않았습니다!", this);

        if (_renderers.Length == 0)
            EditorLog.LogError("Renderer가 할당되지 않았습니다!", this);

        if (!_smoke)
            EditorLog.LogError("ParticleSystem이 할당되지 않았습니다!", this);
    }
}
