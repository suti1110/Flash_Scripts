using UnityEngine;

public class Mirror : MonoBehaviour
{
    [SerializeField]
    private float _reflectPower = 1.5f;

    [SerializeField, InspectorName("반사 효과음")]
    private AudioClip _reflectionAudio;

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.TryGetComponent(out IReflectable camera))
        {
            Vector3 normal = collision.contacts[0].normal;
            Vector3 direction = camera.LastVelocity.normalized;
            Vector3 reflectDirection = Vector3.Reflect(direction, normal);
            float angle = Quaternion.LookRotation(reflectDirection).eulerAngles.y;
            if (angle > 180)
                angle -= 360;
            camera.TweenCameraRotation(new Vector2(angle, 0));
            collision.rigidbody.linearVelocity =
                reflectDirection * camera.LastVelocity.magnitude * _reflectPower;
            PlayReflectionAudio(collision.contacts[0].point);
        }
    }

    public void PlayReflectionAudio(Vector3 position)
    {
        AudioManager.SfxPlayAtPoint(_reflectionAudio, position);
    }
}
// Mirror은 거울 반사 렌더링에 필요한 데이터와 렌더링 수명주기를 관리한다.
// 카메라별 반사 계산과 등록 상태를 분리하여 렌더 패스가 안정적으로 재사용되도록 한다.
