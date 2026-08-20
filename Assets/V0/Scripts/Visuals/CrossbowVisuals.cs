using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CrossbowVisuals : MonoBehaviour
{
    private TowerCrossbow _myTower;
    [SerializeField] private LineRenderer _attackVisuals;
    [SerializeField] private float _attackVisualDuration = .1f;
    [SerializeField] private MeshRenderer _meshRenderer;
    [SerializeField] private Material _material;
    [SerializeField] private float _currentIntensity;
    [SerializeField] private float _maxIntensity = 150f;
    [SerializeField] private Color _startColor;
    [SerializeField] private Color _endColor;

    private void Awake()
    {
        _myTower = GetComponent<TowerCrossbow>();
        _material = new Material(_meshRenderer.material);
        _meshRenderer.material = _material;
    }

    private void Update()
    {
        UpdateEmissionColour();
    }

    private void UpdateEmissionColour()
    {
        float t = _currentIntensity / _maxIntensity;
        Color currentColor = Color.Lerp(_startColor, _endColor, t);
        currentColor *= Mathf.LinearToGammaSpace(_currentIntensity);
        _material.SetColor("_EmissionColor", currentColor);
    }

    public void PlayAttackFX(Vector3 startPoint, Vector3 endPoint)
    {
        StartCoroutine(FXCoroutione(startPoint, endPoint));
    }

    public void PlayReloadFX(float duration)
    {
        StartCoroutine(ChangeEmission(duration / 2));
    } 

    private IEnumerator FXCoroutione(Vector3 startPoint, Vector3 endPoint)
    {
        _myTower.EnableRotation(false);

        _attackVisuals.enabled = true;
        _attackVisuals.SetPosition(0, startPoint);
        _attackVisuals.SetPosition(1, endPoint);

        yield return new WaitForSeconds(_attackVisualDuration);

        _attackVisuals.enabled = false;
        _myTower.EnableRotation(true);
    }

    private IEnumerator ChangeEmission(float duration)
    {
        float startTime = Time.time;
        float startIntensity = 0f;


        while(Time.time - startTime < duration)
        {
            float t = (Time.time - startTime) / duration;
            _currentIntensity = Mathf.Lerp(startIntensity, _maxIntensity, t);
            yield return null;
        }

        _currentIntensity = _maxIntensity;
    }
}
