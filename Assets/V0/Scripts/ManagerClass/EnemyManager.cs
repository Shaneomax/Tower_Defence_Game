using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class WaveDetails
{
    public int BasicEnemy;
    public float FastEnemy;
}

public class EnemyManager : MonoBehaviour
{
    [SerializeField] private WaveDetails _currentWave;
    [SerializeField] private List<GameObject> _enemiesToCreate;
    [SerializeField] private float _spawnCooldown;
    private float _spawnTimer;
    [SerializeField] private GameObject _basicEnemies;
    [SerializeField] private GameObject _fastEnemies;
    [SerializeField] private Transform _respawn;

    private void Start()
    {
        _enemiesToCreate = NewEnemyWave();
    }

    private void Update()
    {
        _spawnTimer -= Time.deltaTime;

        if(_spawnTimer <= 0 && _enemiesToCreate.Count > 0)
        {
            CreateEnemy();
            _spawnTimer = _spawnCooldown;
        }
    }

    private void CreateEnemy()
    {
        GameObject randomEnemy = GetRandomEnemy();
        GameObject newEnemy = Instantiate(randomEnemy, _respawn.position, _respawn.rotation);
    }

    private GameObject GetRandomEnemy()
    {
        int randomIndex = Random.Range(0, _enemiesToCreate.Count);
        GameObject randomEnemy = _enemiesToCreate[randomIndex];
        _enemiesToCreate.RemoveAt(randomIndex);

        return randomEnemy;
    }

    private List<GameObject> NewEnemyWave()
    {
        List<GameObject> newEnemyList = new List<GameObject>();

        for(int i = 0; i < _currentWave.BasicEnemy; i++)
        {
            newEnemyList.Add(_basicEnemies);
        }

        for(int i = 0; i < _currentWave.FastEnemy; i++)
        {
            newEnemyList.Add(_fastEnemies);
        }

        return newEnemyList;
    }
}
