using UnityEngine;
using UnityEngine.AI;
using DG.Tweening;

public class Enemy : MonoBehaviour
{
    private NavMeshAgent _agent;
    [SerializeField] private Transform[] _wayPoints;
    private int _wayPointIndex;
    private float _turnSpeed = 10f;
    private Tween _rotationTween;

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _agent.updateRotation = false;
        _agent.avoidancePriority =(int)_agent.speed * 10;
    }

    private void Start()
    {
       _wayPoints = FindFirstObjectByType<WaypointManager>().GetWaypoints();
    }

    private void Update()
    {
        if(_agent.remainingDistance < 0.5f)
        {
            _agent.SetDestination(GetNextWaypoint());
        }

        FaceTarget(_agent.steeringTarget);
    }

    private Vector3 GetNextWaypoint()
    {
        if(_wayPointIndex >= _wayPoints.Length)
        {
            return transform.position;
        }

        Vector3 targetPoint = _wayPoints[_wayPointIndex].position;
        _wayPointIndex++;

        return targetPoint;
    }


    private void FaceTarget(Vector3 newTarget)
    {
        Vector3 lookTarget = new Vector3(newTarget.x, transform.position.y, newTarget.z);
        if((lookTarget - transform.position).sqrMagnitude < 0.001f) return;

        if(_rotationTween != null && _rotationTween.IsActive())
            return;

        _rotationTween = transform.DOLookAt(lookTarget, 1f / _turnSpeed).SetEase(Ease.Linear);
    }

    private void OnDestroy()
    {
        if(_rotationTween != null)
        {
            _rotationTween.Kill();
            _rotationTween = null;
        }
    }
}
