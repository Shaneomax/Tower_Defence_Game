using UnityEngine;
using UnityEngine.AI;

public class Enemy : MonoBehaviour
{
    private NavMeshAgent _agent;
    [SerializeField] private Transform[] _wayPoint;
    private int _wayPointIndex;
    private float _turnSpeed = 10f;

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _agent.updateRotation = false;
    }

    private void Update()
    {
        if(_agent.remainingDistance < 0.5f)
        {
            _agent.SetDestination(GetNextWaypoint());
        }
    }

    private Vector3 GetNextWaypoint()
    {
        if(_wayPointIndex >= _wayPoint.Length)
        {
            return transform.position;
        }

        Vector3 targetPoint = _wayPoint[_wayPointIndex].position;
        _wayPointIndex++;

        return targetPoint;
    }

    private void FaceTarget(Vector3 newTarget)
    {
        Vector3 directinToTarget = newTarget - transform.position;
        directinToTarget.y = 0;

        Quaternion newRotation = Quaternion.LookRotation(directinToTarget);
        transform.rotation = Quaternion.Lerp(transform.rotation, newRotation, _turnSpeed * Time.deltaTime);
    }

}
