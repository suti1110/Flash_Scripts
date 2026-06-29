using UnityEngine;

namespace StateMachine
{
    public class AnimationOnOtherLayer : StateMachineBehaviour
    {
        [Header("Layer Convert")]
        [SerializeField]
        private float _transitionDuration = 0.1f;

        private readonly PlayerStateManager _state = PlayerStateManager.Instance;

        private GameObject _mainGameObject;

        protected GameObject GetGameObject(Animator animator)
        {
            if (_mainGameObject == null)
            {
                // 플레이어에게는 반드시 Rigidbody가 존재하므로 Rigidbody를 통해 부모 GameObject를 가져옵니다.
                _mainGameObject = animator.GetComponentInParent<Rigidbody>().gameObject;
            }
            return _mainGameObject;
        }

        public override void OnStateEnter(
            Animator animator,
            AnimatorStateInfo stateInfo,
            int layerIndex
        )
        {
            animator.SetLayerWeight(layerIndex, 1f, _transitionDuration);
        }

        public override void OnStateExit(
            Animator animator,
            AnimatorStateInfo stateInfo,
            int layerIndex
        )
        {
            animator.SetLayerWeight(layerIndex, 0f, _transitionDuration);
            _state[GetGameObject(animator)].State = PlayerState.Idle;
        }
    }
}
