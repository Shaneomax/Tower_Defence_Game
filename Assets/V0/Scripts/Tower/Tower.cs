using System.Collections.Generic;
using UnityEngine;

public class Tower : MonoBehaviour
{
    [SerializeField] private Transform _currentEnemy;
    [SerializeField] private Transform _towerHead;
    [SerializeField] private float _rotationSpeed;
    [SerializeField] private float _attackRange = 1.5f;
    [SerializeField]private LayerMask _enemyLayerMask;

    private void Update()
    {
        if(_currentEnemy == null)
        {
            _currentEnemy = FindEnemyWhithinRange();
            return;
        }
        
        if(_currentEnemy != null)
        {
            RotateTowardsEnemy();
        }
    }


    private void RotateTowardsEnemy()
    {
        Vector3 directionToEnemy = _currentEnemy.position - _towerHead.position;
        Quaternion lookRotation = Quaternion.LookRotation(directionToEnemy);
        Vector3 rotation = Quaternion.Lerp(_towerHead.rotation, lookRotation, Time.deltaTime * _rotationSpeed).eulerAngles;
        _towerHead.rotation = Quaternion.Euler(rotation);
    }

    private Transform FindEnemyWhithinRange()
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
}
