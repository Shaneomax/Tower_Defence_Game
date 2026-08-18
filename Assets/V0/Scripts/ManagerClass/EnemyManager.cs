using UnityEngine;

public class EnemyManager : MonoBehaviour
{
    [SerializeField] private float _spawnCooldown;
    private float _spawnTimer;
    [SerializeField] private GameObject _enemies;
    [SerializeField] private Transform _respawn;

    private void Update()
    {
        _spawnTimer -= Time.deltaTime;

        if(_spawnTimer <= 0)
        {
            CreateEnemy();
            _spawnTimer = _spawnCooldown;
        }
    }

    private void CreateEnemy()
    {
        GameObject newEnemy = Instantiate(_enemies, _respawn.position, _respawn.rotation);
    }
}
