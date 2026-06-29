using UnityEngine;

public class RaceFloor : MonoBehaviour
{
    [SerializeField]
    private Transform _startPos;

    private void OnTriggerEnter(Collider other)
    {
        if (other.TryGetComponent(out Player _))
        {
            other.transform.position = _startPos.position;
        }
    }
}
