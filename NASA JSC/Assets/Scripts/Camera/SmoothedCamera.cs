using UnityEngine;
using UnityEditor;

public class SmoothedCamera : MonoBehaviour
{

    [SerializeField] bool shouldUpdate = true;

    [Header("Rotation Axes")]
    [SerializeField] bool updateXAxisRotation = true;

    [SerializeField] bool updateYAxisRotation = true;

    [SerializeField] bool updateZAxisRotation = false; // keep this off for most cases

 

    [Header("Smoothing Params")]
    [SerializeField][Range(0, 0.99f)] float smoothingValuePosition = 0.075f;

    [SerializeField][Range(0, 0.99f)] float smoothingValueRotation = 0.075f;

 

    Transform _mainCamTransform;

    Vector3 velocity = Vector3.zero;

    void Start()

    {

        _mainCamTransform = Camera.main.transform;

 

        transform.SetPositionAndRotation(_mainCamTransform.position, UpdateRotation(_mainCamTransform));

    }

    private Quaternion UpdateRotation(Transform camera)

    {

        return Quaternion.Euler(

                        updateXAxisRotation ? camera.eulerAngles.x : transform.eulerAngles.x,

                        updateYAxisRotation ? camera.eulerAngles.y : transform.eulerAngles.y,

                        updateZAxisRotation ? camera.eulerAngles.z : 0);

    }

 

    void Update()

    {

        if (!shouldUpdate) return;

 

        Vector3 pos = Vector3.SmoothDamp(transform.position, _mainCamTransform.position, ref velocity, smoothingValuePosition);

        Quaternion rot = Quaternion.Slerp(transform.rotation, UpdateRotation(_mainCamTransform), smoothingValueRotation);

 

        transform.SetPositionAndRotation(pos, rot);

    }

 

}