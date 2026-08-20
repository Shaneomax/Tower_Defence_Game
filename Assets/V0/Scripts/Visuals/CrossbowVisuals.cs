using System.Collections;
using Unity.VisualScripting;
using UnityEngine;

public class CrossbowVisuals : MonoBehaviour
{
    [SerializeField] private LineRenderer _attackVisuals;
    [SerializeField] private float _attackVisualsDuration = 0.1f;

    public void EnableAttackVisuals(Vector3 startPosition, Vector3 endPosition)
    {
        _attackVisuals.SetPosition(0, startPosition);
        _attackVisuals.SetPosition(1, endPosition);
        _attackVisuals.enabled = true;

        StartCoroutine(DisableAttackVisualsAfterDelay());
    }

    private IEnumerator DisableAttackVisualsAfterDelay()
    {
        yield return new WaitForSeconds(_attackVisualsDuration);
        _attackVisuals.enabled = false;
    }
}
