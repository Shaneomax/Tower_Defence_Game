using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

public class Tower : MonoBehaviour
{
    [SerializeField] protected float _attackCooldown;
    protected float _lastTimeAttacked;
    [SerializeField] protected Transform _currentEnemy;
    [SerializeField] protected Transform _towerHead;
    [SerializeField] protected float _rotationSpeed;
    [SerializeField] protected float _attackRange = 1.5f;
    [SerializeField] protected LayerMask _enemyLayerMask;
    private Tween _rotationTween;

    protected virtual void Update()
    {
        if(_currentEnemy == null)
        {
            _currentEnemy = FindEnemyWhithinRange();
            return;
        }

        if(Vector3.Distance(transform.position, _currentEnemy.position) > _attackRange)
        {
            KillRotationTween();
            _currentEnemy = null;
            return;
        }

        RotateTowardsEnemy();

        if(CanAttack())
        {
            Attack();
        }
    }
    
    protected virtual void Attack()
    {
        Debug.Log("Attacking enemy");
    }

    protected virtual bool CanAttack()
    {
        if(Time.time > _lastTimeAttacked + _attackCooldown)
        {
            _lastTimeAttacked = Time.time;
            return true;
        }
        return false;
    }

    protected void RotateTowardsEnemy()
    {
        if(_currentEnemy == null || _towerHead == null) 
           return;

        Vector3 direction = _currentEnemy.position - _towerHead.position;

        if(direction.sqrMagnitude < 0.001f)
            return;

        if(_rotationTween != null && _rotationTween.IsActive())
            return;

        _rotationTween = _towerHead.DOLookAt(_currentEnemy.position, 1f / _rotationSpeed).SetEase(Ease.Linear);
    }


    private void KillRotationTween()
    {
        if(_rotationTween != null)
        {
            _rotationTween.Kill();
            _rotationTween = null;
        }
    }

    protected virtual void OnDestroy()
    {
        KillRotationTween();
    }

    protected Vector3 DiretionToEnemy(Transform startPoint)
    {
        return (_currentEnemy.position - startPoint.position).normalized;
    }

    protected virtual Transform FindEnemyWhithinRange()
    {
        List<Transform> possibleTargets = new List<Transform>();
        Collider[] enemiesAround = Physics.OverlapSphere(transform.position, _attackRange, _enemyLayerMask);
        
        foreach(Collider enemy in enemiesAround)
        {
            possibleTargets.Add(enemy.transform);
        }

        if (possibleTargets.Count == 0)
        {
            return null;        
        }

        int randomIndex = Random.Range(0, possibleTargets.Count);
        
        return possibleTargets[randomIndex];
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, _attackRange);
    }
}
