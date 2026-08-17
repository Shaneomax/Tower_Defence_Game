using UnityEngine;

public class RotateObject : MonoBehaviour
{
   [SerializeField] private Vector3 _rotationVector;
   [SerializeField] private float _rotationSpeed;

   private void Update()
   {
      float rotationAmount = _rotationSpeed * Time.deltaTime * 100f;
      transform.Rotate(_rotationVector * rotationAmount);
   }
}
