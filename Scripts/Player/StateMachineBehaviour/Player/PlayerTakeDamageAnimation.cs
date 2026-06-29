using UnityEngine;

namespace StateMachine
{
    public class PlayerTakeDamageAnimation : AnimationOnThisLayer
    {
        private IDamagable _damagable;

        [SerializeField]
        private float _exitTime;

        public IDamagable GetDamagable(Animator animator) =>
            _damagable ??= animator.GetComponentInParent<IDamagable>(true);

        public override void OnStateUpdate(
            Animator animator,
            AnimatorStateInfo stateInfo,
            int layerIndex
        )
        {
            if (stateInfo.normalizedTime >= _exitTime)
            {
                animator.SetBool(PlayerAnimationHash.IsTakingDamage, false);
            }
        }

        public override void OnStateExit(
            Animator animator,
            AnimatorStateInfo stateInfo,
            int layerIndex
        )
        {
            GetDamagable(animator).DeathCheck();
            base.OnStateExit(animator, stateInfo, layerIndex);
        }
    }
}
