using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

public interface ISkill
{
    event Action OnSkillFinished;
    void UseSkill(int skillIndex);
    PlayerInputType GetCurrentSkillConstraints();
    void NotifySkillFinished();
}

public interface ISkillAnimation
{
    AnimationClip GetSkillClip();
}

public interface ISkillExecute
{
    void SkillExecute();
}

[RequireComponent(typeof(EnergyTracker))]
public class PlayerSkill : NetworkBehaviour, ISkill, ISkillAnimation, ISkillExecute
{
    [SerializeField]
    private SO_SkillSet _skillSet;

    private EnergyTracker _energyTracker;

    private SO_Skill _curCastingSkill;
    private int _curCastingSkillIndex = -1;
    private Coroutine _skillRoutine;

    private readonly PlayerStateManager _state = PlayerStateManager.Instance;

    private void Awake()
    {
        _energyTracker = GetComponent<EnergyTracker>();
        _state[gameObject].OnStateChanged += HandleStateChanged;
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        _state[gameObject].OnStateChanged -= HandleStateChanged;
    }

    private void HandleStateChanged(PlayerState state)
    {
        if (state != PlayerState.UsingSkill && _skillRoutine != null)
        {
            StopCoroutine(_skillRoutine);
            _skillRoutine = null;
        }
    }

    /// <summary>
    /// 스킬셋에 들어있는 스킬을 사용하는 메서드
    /// </summary>
    /// <param name="skillIndex">스킬셋 내에서의 스킬 번호</param>
    public void UseSkill(int skillIndex)
    {
        if (
            !IsOwner
            || skillIndex >= _skillSet.Skills.Length
            || _skillSet.Skills[skillIndex] == null
        )
            return;

        if (!_energyTracker.TryConsumeEnergy(_skillSet.Skills[skillIndex].EnergyCost))
        {
            EditorLog.Log("기력이 부족합니다!!!");
            // TODO : UI띄우기
        }
        else
        {
            _curCastingSkill = _skillSet.Skills[skillIndex];
            _curCastingSkillIndex = skillIndex;
            _state[gameObject].State = PlayerState.UsingSkill;

            if (_skillRoutine != null)
                StopCoroutine(_skillRoutine);

            _skillRoutine = StartCoroutine(SkillRoutine(_curCastingSkill));
        }
    }

    public event Action OnSkillFinished;

    public PlayerInputType GetCurrentSkillConstraints()
    {
        return _curCastingSkill != null ? _curCastingSkill.ConstrainedInputs : PlayerInputType.None;
    }

    public void NotifySkillFinished()
    {
        if (_state[gameObject].State == PlayerState.UsingSkill)
        {
            _state[gameObject].State = PlayerState.Idle;
        }
        OnSkillFinished?.Invoke();
    }

    private IEnumerator SkillRoutine(SO_Skill skill)
    {
        float totalTime = skill.SkillClip != null ? skill.SkillClip.length : 0f;

        if (totalTime > 0f)
        {
            yield return new WaitForSeconds(totalTime * skill.ExecuteTime);
        }

        SkillExecute();

        if (totalTime > 0f)
        {
            yield return new WaitForSeconds(totalTime * (1f - skill.ExecuteTime));
        }

        _skillRoutine = null;
        NotifySkillFinished();
    }

    public AnimationClip GetSkillClip()
    {
        return _curCastingSkill != null ? _curCastingSkill.SkillClip : null;
    }

    public void SkillExecute()
    {
        if (!IsOwner)
            return;

        _curCastingSkill.ExecuteSkill(this, _energyTracker);
    }

    public void RequestSpawnEffect(int effectId, Vector3 position, Quaternion rotation)
    {
        if (_curCastingSkillIndex >= 0)
        {
            SpawnEffectServerRpc(_curCastingSkillIndex, effectId, position, rotation);
        }
    }

    [Rpc(SendTo.Server)]
    private void SpawnEffectServerRpc(
        int skillIndex,
        int effectId,
        Vector3 position,
        Quaternion rotation
    )
    {
        if (skillIndex < 0 || skillIndex >= _skillSet.Skills.Length)
            return;

        SO_Skill skill = _skillSet.Skills[skillIndex];
        if (skill != null)
        {
            skill.ServerSpawnEffect(effectId, position, rotation);
        }
    }

    private void OnValidate()
    {
        if (!_skillSet)
        {
            EditorLog.LogError("SO_SkillSet이 할당되지 않았습니다!!!", this);
        }
    }
}
