using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fove.Unity;
using UnityEngine.XR;

public class FreezeScene : MonoBehaviour
{
    public Transform FixationDot;
    public GameObject Camera;
    private const float _distance = 2.0f;
    public float rotation; 

    // Update is called once per frame
    void Update()
    {
        //transform.position = new Vector3(transform.position.x, transform.position.y, GameObject.transform.position.z);
        //var direction = transform.position - FixationDot.transform.position;
        //transform.rotation = Quaternion.LookRotation(direction);
        //transform.LookAt(direction);
        transform.LookAt(FixationDot);

        //transform.position = Camera.transform.position + Camera.transform.forward * _distance;
        //transform.rotation = Camera.transform.rotation;

        //FoveManager.GetHMDRotation();

        ////transform.rotation = Quaternion.Euler(0, 0, 0);
        ///
        /////var test = FoveManager.GetHmdRotation();
        /////transform.rotation = Quaternion.Euler(-test, -test, -test);

        /////////Vector3 relativePos = FixationDot.position - transform.position;
        /////////Quaternion rotation = Quaternion.LookRotation(relativePos, Vector3.up);
        /////////transform.rotation = rotation;

        //UnityEngine.XR.InputTracking.disablePositionalTracking = true; ////**this is "obsolete" apparently

        /// THIS IS  CLOSE I THINK!!! THAT SECOND LINE IS I MEAN.....
        //transform.rotation = Quaternion.Inverse(InputTracking.GetLocalRotation(XRNode.CenterEye));
        //transform.rotation = Quaternion.Inverse(InputDevice.TryGetFeatureValue(CommonUsages.deviceRotation, out rotation); 
    }
}