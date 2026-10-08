using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// 활성 ITimedSkill의 등록, 네트워크 복원, 갱신, 종료 시점만 관리한다.
// 실제 효과의 시작·매 프레임 처리·정리는 각 스킬 SO가 담당한다.
public sealed class PlayerTimedSkillEffects
{
    private readonly NetworkBehaviour _owner;
    private readonly SO_SkillSet _skillSet;
    private readonly NetworkList<NetworkTimedSkillInfo> _networkSkills;
    private readonly List<TimedSkillInfo> _activeSkills = new();
    private readonly List<TimedSkillInfo> _updateSnapshot = new();
    private readonly List<TimedSkillInfo> _reconcileSnapshot = new();
    private readonly Func<double> _clock;
    private bool _isSubscribed;
    private bool _isClearing;
    private bool _isSynchronizing;
    private bool _networkDirty;

    private double CurrentTime => _owner.IsSpawned
        ? _owner.NetworkManager.ServerTime.Time : Time.timeAsDouble;

    public PlayerTimedSkillEffects(
        NetworkBehaviour owner, SO_SkillSet skillSet, NetworkList<NetworkTimedSkillInfo> networkSkills
    )
    {
        _owner = owner;
        _skillSet = skillSet;
        _networkSkills = networkSkills;
        _clock = () => CurrentTime;
    }

    public void OnNetworkSpawn()
    {
        // NGO는 비활성 Behaviour에도 Spawn 콜백을 전달할 수 있다.
        if (!_owner.isActiveAndEnabled)
            return;
        Subscribe();
        RebuildNetworkSkills();
    }

    public void OnEnable()
    {
        if (_owner.IsSpawned)
        {
            Subscribe();
            RebuildNetworkSkills();
        }
    }

    public void OnDisable()
    {
        Unsubscribe();
        CancelActiveEffects();
    }

    public void OnNetworkDespawn()
    {
        Unsubscribe();
        if (_owner.IsServer)
            _networkSkills.Clear();
        EndAllSkills();
    }

    public void Clear()
    {
        Unsubscribe();
        EndAllSkills();
    }

    private void HandleListChanged(NetworkListEvent<NetworkTimedSkillInfo> _) =>
        RebuildNetworkSkills();

    private void Subscribe()
    {
        if (_isSubscribed)
            return;
        _networkSkills.OnListChanged += HandleListChanged;
        _isSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_isSubscribed)
            return;
        _networkSkills.OnListChanged -= HandleListChanged;
        _isSubscribed = false;
    }

    private void RebuildNetworkSkills()
    {
        _networkDirty = true;
        if (_isSynchronizing || _isClearing)
            return;

        _isSynchronizing = true;
        try
        {
            do
            {
                _networkDirty = false;
                double now = CurrentTime;
                _reconcileSnapshot.Clear();
                _reconcileSnapshot.AddRange(_activeSkills);
                foreach (TimedSkillInfo info in _reconcileSnapshot)
                {
                    bool retained = false;
                    for (int i = 0; i < _networkSkills.Count; i++)
                    {
                        NetworkTimedSkillInfo incoming = _networkSkills[i];
                        if (incoming.SkillNetworkId == info.Skill.NetworkId && incoming.EndTime > now
                            && info.RemainingTime(now) > 0d)
                        {
                            retained = true;
                            break;
                        }
                    }
                    if (!retained)
                        EndSkill(info);
                    if (_networkDirty)
                        break;
                }
                if (_networkDirty)
                    continue;

                for (int i = 0; i < _networkSkills.Count; i++)
                {
                    NetworkTimedSkillInfo incoming = _networkSkills[i];
                    SO_Skill skill = _skillSet.GetSkillByNetworkId(incoming.SkillNetworkId);
                    if (!double.IsFinite(incoming.EndTime) || incoming.EndTime <= now
                        || skill is not ITimedSkill timedSkill)
                        continue;
                    TimedSkillInfo info = FindActive(skill);
                    if (info != null)
                        info.EndTime = incoming.EndTime;
                    else
                        AddActiveSkill(skill, timedSkill, incoming.EndTime);
                    // 시작/종료 콜백에서 네트워크 목록이 바뀌면 최신 목록으로 다시 맞춘다.
                    if (_networkDirty)
                        break;
                }
            } while (_networkDirty);
        }
        finally
        {
            _reconcileSnapshot.Clear();
            _isSynchronizing = false;
        }
    }
    public void Update()
    {
        double now = CurrentTime;
        if (_owner.IsSpawned && _owner.IsServer)
        {
            for (int i = _networkSkills.Count - 1; i >= 0; i--)
            {
                if (i >= _networkSkills.Count)
                    continue;
                if (_networkSkills[i].EndTime <= now)
                    _networkSkills.RemoveAt(i);
            }
        }

        // 콜백에서 다른 스킬이 추가되거나 전체 취소돼도 현재 순회를 훼손하지 않는다.
        _updateSnapshot.Clear();
        _updateSnapshot.AddRange(_activeSkills);
        for (int i = 0; i < _updateSnapshot.Count; i++)
        {
            TimedSkillInfo info = _updateSnapshot[i];
            if (info.HasEnded)
                continue;
            double remaining = info.RemainingTime(now);
            if (remaining <= 0d)
            {
                EndSkill(info);
                continue;
            }

            info.TimedSkill.UpdateTimedSkill(CreateContext(info, Time.deltaTime));
        }
        _updateSnapshot.Clear();
    }

    public void CancelActiveEffects()
    {
        EndAllSkills();
        if (_owner.IsSpawned && _owner.IsServer)
            _networkSkills.Clear();
    }

    public bool IsActive(SO_Skill skill)
    {
        TimedSkillInfo info = FindActive(skill);
        return info != null && info.RemainingTime(CurrentTime) > 0d;
    }

    public void ActivateLocal(SO_Skill skill, ITimedSkill timedSkill)
    {
        if (_isClearing || timedSkill == null || !float.IsFinite(timedSkill.Duration)
            || timedSkill.Duration <= 0f)
            return;
        TimedSkillInfo activeInfo = FindActive(skill);
        if (activeInfo != null && activeInfo.RemainingTime(CurrentTime) <= 0d)
        {
            EndSkill(activeInfo);
            activeInfo = null;
        }
        if (activeInfo != null)
        {
            if (timedSkill.CanReactivateWhileActive)
                activeInfo.EndTime = CurrentTime + timedSkill.Duration;
            return;
        }

        AddActiveSkill(skill, timedSkill, CurrentTime + timedSkill.Duration);
    }

    public void ActivateNetwork(int networkId)
    {
        if (_isClearing || !_owner.isActiveAndEnabled || !_owner.IsSpawned || !_owner.IsServer)
            return;
        SO_Skill skill = _skillSet.GetSkillByNetworkId(networkId);
        if (skill is not ITimedSkill timedSkill || !float.IsFinite(timedSkill.Duration)
            || timedSkill.Duration <= 0f)
            return;
        if (!timedSkill.CanReactivateWhileActive && IsActive(skill))
            return;

        double endTime = CurrentTime + timedSkill.Duration;
        for (int i = _networkSkills.Count - 1; i >= 0; i--)
        {
            if (_networkSkills[i].SkillNetworkId == networkId)
            {
                _networkSkills[i] = new NetworkTimedSkillInfo(networkId, endTime);
                return;
            }
        }
        _networkSkills.Add(new NetworkTimedSkillInfo(networkId, endTime));
    }

    private void AddActiveSkill(SO_Skill skill, ITimedSkill timedSkill, double endTime)
    {
        var info = new TimedSkillInfo(skill, endTime);
        _activeSkills.Add(info);
        try
        {
            timedSkill.StartTimedSkill(CreateContext(info, 0f));
        }
        catch (Exception exception)
        {
            // 시작이 실패한 항목을 활성 목록에 남겨 다음 프레임까지 실행하지 않는다.
            Debug.LogException(exception);
            EndSkill(info);
        }
    }

    private TimedSkillInfo FindActive(SO_Skill skill)
    {
        for (int i = 0; i < _activeSkills.Count; i++)
        {
            if (_activeSkills[i].Skill == skill)
                return _activeSkills[i];
        }
        return null;
    }

    private void EndAllSkills()
    {
        if (_isClearing)
            return;
        _isClearing = true;
        try
        {
            while (_activeSkills.Count > 0)
                EndAndRemoveAt(_activeSkills.Count - 1);
        }
        finally
        {
            _isClearing = false;
        }
    }

    private void EndAndRemoveAt(int index)
    {
        TimedSkillInfo info = _activeSkills[index];
        _activeSkills.RemoveAt(index);
        // 종료 콜백의 재진입에서도 같은 스킬을 두 번 종료하지 않는다.
        info.HasEnded = true;
        try
        {
            info.TimedSkill.EndTimedSkill(CreateContext(info, 0f));
        }
        catch (Exception exception)
        {
            // 한 스킬의 정리 실패로 나머지 버프/영역의 종료까지 중단하지 않는다.
            Debug.LogException(exception);
        }
        finally
        {
            info.RuntimeState = null;
        }
    }

    private void EndSkill(TimedSkillInfo info)
    {
        int index = _activeSkills.IndexOf(info);
        if (index >= 0)
            EndAndRemoveAt(index);
    }

    private TimedSkillContext CreateContext(TimedSkillInfo info, float deltaTime) =>
        new(_owner.transform, info, deltaTime, _clock);
}
