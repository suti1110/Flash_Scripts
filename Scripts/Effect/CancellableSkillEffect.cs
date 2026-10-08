using UnityEngine;

// 시전자에게 묶인 지속 이펙트는 같은 종료 규칙을 먼저 검사한 뒤 고유 동작을 실행한다.
public abstract class CancellableSkillEffect : MonoBehaviour
{
    private IPlayerSkillStatus _casterStatus;

    protected void BindCaster(GameObject caster)
    {
        _casterStatus = caster != null ? caster.GetComponent<IPlayerSkillStatus>() : null;
    }

    private void Update()
    {
        if (_casterStatus != null && _casterStatus.IsSilenced)
        {
            Destroy(gameObject);
            return;
        }

        UpdateActiveEffect();
    }

    protected abstract void UpdateActiveEffect();
}
