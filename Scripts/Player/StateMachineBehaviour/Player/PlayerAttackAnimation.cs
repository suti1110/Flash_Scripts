using UnityEngine;

namespace StateMachine
{
    public class PlayerAttackAnimation : AnimationOnOtherLayer
    {
        [Header("Attack Animation")]
        [SerializeField]
        private float _attackTiming = 0.3f;

        [SerializeField]
        private float _exitTime;

        private IAttackable _attackable;

        private IAttackable GetAttack(Animator animator) =>
            _attackable ??= animator.GetComponentInParent<IAttackable>(true);

        private bool _isAttacked = false;

        public override void OnStateEnter(
            Animator animator,
            AnimatorStateInfo stateInfo,
            int layerIndex
        )
        {
            base.OnStateEnter(animator, stateInfo, layerIndex);
            _isAttacked = false;
        }

        public override void OnStateUpdate(
            Animator animator,
            AnimatorStateInfo stateInfo,
            int layerIndex
        )
        {
            if (!_isAttacked && stateInfo.normalizedTime >= _attackTiming)
            {
                _isAttacked = true;
                GetAttack(animator).AttackHit();
            }

            if (stateInfo.normalizedTime >= _exitTime)
            {
                animator.SetBool(PlayerAnimationHash.IsAttacking, false);
            }
        }
    }
}
