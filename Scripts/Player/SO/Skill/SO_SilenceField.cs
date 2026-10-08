using UnityEngine;

[CreateAssetMenu(fileName = "SilenceField", menuName = "Player/Skill/Silence Field")]
public sealed class SO_SilenceField : SO_Skill, ITimedSkill
{
    private sealed class RuntimeState : IPlayerSilenceSource, IEnergyRecoveryBlocker
    {
        private readonly Transform _caster;
        private readonly TimedSkillContext _context;
        private GameObject _areaVisual;
        private float _radius;
        private float _power;
        private float _immunity;

        public RuntimeState(in TimedSkillContext context)
        {
            _context = context;
            _caster = context.Caster;
        }

        public void StartVisual(GameObject areaPrefab, float radius)
        {
            if (_caster != null && areaPrefab != null)
            {
                // 시전자별 표시만 생성한다. 영역 판정은 프리팹의 콜라이더에 의존하지 않는다.
                _areaVisual = Object.Instantiate(areaPrefab, _caster);
                _areaVisual.transform.localPosition = Vector3.zero;
                _areaVisual.transform.localRotation = Quaternion.identity;
                _areaVisual.transform.localScale = areaPrefab.transform.localScale * (radius * 2f);
            }
        }

        public void Update(float radius, float power, float immunity)
        {
            _radius = radius;
            _power = power;
            _immunity = immunity;
        }

        public float GetSilencePower(Transform target)
        {
            if (!_context.IsActive || target == null || target == _caster || _caster == null)
                return 0f;
            return (target.position - _caster.position).sqrMagnitude <= _radius * _radius
                ? _power : 0f;
        }

        public float GetSilenceImmunity(Transform target) =>
            _context.IsActive && _caster != null && target == _caster ? _immunity : 0f;

        // 같은 영역 시전자처럼 침묵 면역이 이긴 대상은 회복도 허용한다.
        public bool BlocksRecovery(Transform target) =>
            GetSilencePower(target) > 0f
            && target.GetComponent<IPlayerSkillStatus>() is { IsSilenced: true };

        public void Clear()
        {
            if (_areaVisual != null)
                Object.Destroy(_areaVisual);
        }
    }

    [field: SerializeField, Min(0.01f), InspectorName("침묵 지속 시간")]
    public float Duration { get; private set; } = 10f;
    public bool CanReactivateWhileActive => true;

    [field: SerializeField, Min(0.01f), InspectorName("침묵 영역 반경")]
    public float Radius { get; private set; } = 20f;

    [SerializeField, InspectorName("침묵 영역 시각 효과 프리팹")]
    [Tooltip("원점 중심의 지름 1m 구체 프리팹입니다. 발동 중 시전자 자식으로 생성해 영역 지름에 맞게 확대합니다.")]
    private GameObject _areaPrefab;
    [field: SerializeField, Min(0.01f), InspectorName("영역 침묵 수치")]
    public float SilencePower { get; private set; } = 1f;

    [field: SerializeField, Min(0f), InspectorName("시전자 침묵 면역 수치")]
    public float CasterImmunity { get; private set; } = 2f;

    public void StartTimedSkill(in TimedSkillContext context)
    {
        var state = new RuntimeState(context);
        context.RuntimeState = state;
        state.Update(Radius, SilencePower, CasterImmunity);
        PlayerSilenceSources.Register(state);
        PlayerEnergyRecoverySources.Register(state);
        state.StartVisual(_areaPrefab, Radius);
    }

    public void UpdateTimedSkill(in TimedSkillContext context)
    {
        if (context.RuntimeState is RuntimeState state)
            state.Update(Radius, SilencePower, CasterImmunity);
    }

    public void EndTimedSkill(in TimedSkillContext context)
    {
        if (context.RuntimeState is not RuntimeState state)
            return;
        PlayerSilenceSources.Unregister(state);
        PlayerEnergyRecoverySources.Unregister(state);
        state.Clear();
        context.RuntimeState = null;
    }

    public override void ExecuteSkill(in SkillExecutionContext context)
    {
        context.TimedSkills?.ActivateTimedSkill(this);
    }

    protected override void OnValidate()
    {
        base.OnValidate();
        if (SilencePower <= 0f)
            EditorLog.LogError("영역 침묵 수치는 0보다 커야 합니다.", this);
        if (!float.IsFinite(Duration) || Duration <= 0f)
            EditorLog.LogError("침묵 지속 시간은 0보다 커야 합니다.", this);
        if (!float.IsFinite(Radius) || Radius <= 0f)
            EditorLog.LogError("침묵 영역 반경은 0보다 커야 합니다.", this);
        if (CasterImmunity <= SilencePower)
            EditorLog.LogError("시전자 면역 수치는 영역 침묵 수치보다 커야 합니다.", this);
        if (_areaPrefab == null)
            EditorLog.LogError("침묵 영역 시각 효과 프리팹이 할당되지 않았습니다.", this);
        else if (_areaPrefab.GetComponentInChildren<Collider>(true) != null)
            EditorLog.LogError("침묵 영역 프리팹에는 판정용 Collider를 넣지 마세요.", this);
    }
}
