using System;
using UnityEngine;

public class TowerCrossbow : Tower
{
   [SerializeField] private Transform _gunPoint;

    protected override void Update()
    {
        base.Update();
        
        if (_currentEnemy == null) return;

        Vector3 directionToEnemy = DiretionToEnemy(_gunPoint);

        if(Physics.Raycast(_gunPoint.position, directionToEnemy, out RaycastHit hitInfo, Mathf.Infinity))
        {
            Debug.DrawLine(_gunPoint.position, hitInfo.point, Color.red);
        }
    }
}
