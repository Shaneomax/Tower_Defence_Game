using UnityEngine;

public class TowerCrossbow : Tower
{
    override protected void Attack()
    {
        Debug.Log("Attacking enemy: " + _currentEnemy.name);
    }
}
