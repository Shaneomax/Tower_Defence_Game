using UnityEngine;

public class WaypointManager : MonoBehaviour
{
    [SerializeField] private Transform[] _waypoints;

    public Transform[] GetWaypoints()
    {
        return _waypoints;
    }
}
