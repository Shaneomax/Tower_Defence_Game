using System;
using UnityEngine;

public class TowerCrossbow : Tower
{
   [SerializeField] private Transform _gunPoint;
   private CrossbowVisuals _visuals;

    protected override void Awake()
    {
        base.Awake();
        _visuals = GetComponentInChildren<CrossbowVisuals>();
    }

    protected override void Attack()
    {
        if (_currentEnemy == null) return;

        Vector3 directionToEnemy = DiretionToEnemy(_gunPoint);

        if(Physics.Raycast(_gunPoint.position, directionToEnemy, out RaycastHit hitInfo, Mathf.Infinity))
        {
            _towerHead.forward = directionToEnemy;
            Debug.DrawLine(_gunPoint.position, hitInfo.point, Color.red);

            _visuals.EnableAttackVisuals(_gunPoint.position, hitInfo.point);
        }
    }   
}
