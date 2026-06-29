using UnityEngine;

public class Mirror : MonoBehaviour
{
    [SerializeField]
    private float _reflectPower = 1.5f;

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
            camera.SetCameraRotation(new Vector2(angle, 0));
            collision.rigidbody.linearVelocity =
                reflectDirection * camera.LastVelocity.magnitude * _reflectPower;
        }
    }
}
