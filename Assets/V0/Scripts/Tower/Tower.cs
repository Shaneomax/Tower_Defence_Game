using UnityEngine;

public class Tower : MonoBehaviour
{
    [SerializeField] private Transform _currentEnemy;
    [SerializeField] private Transform _towerHead;
    [SerializeField]private float _rotationSpeed;

    private void Update()
    {
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
}
