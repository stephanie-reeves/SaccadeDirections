using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UXF;
using Fove.Unity;

public class ChangeScene : MonoBehaviour
{
    public Session session;

    // define public game object variables
    public GameObject Sphere;
    public GameObject FixationDot;
    public GameObject WhiteScreen;
    public GameObject CalibrationDot;
    public GameObject FoveInterface;
    public GameObject RecorderObject;

    // reference to the materials we want to change the color of.
    Material material;
    public Material[] fractals;
    public Material[] scenes;
    public Material BaseMaterial;
    public Material PlainWhite;

    // reference to the renderers we want to manipulate.
    public Renderer rend;
    public Renderer WhiteScreenRend;
    public Renderer CalDotRend;
    
    // Detect whether calibration was selected for the FOVE
    public Toggle calibrate;

    // In order to get the Eyes Image
    private Texture2D eyeimages;
    private Texture2D texBoi;
    public RawImage rimage;

    private int imageTiltDegs;
    
    IEnumerator Wait_for_calibration()
    {
        Debug.Log("IsCal?");
        Debug.Log(FoveManager.IsEyeTrackingCalibrating());
        FoveManager.StartEyeTrackingCalibration();
        yield return new WaitUntil(() => FoveManager.IsEyeTrackingCalibrating() == false);
        Debug.Log(FoveManager.IsEyeTrackingCalibrating());
        //StartCoroutine(StartHomemadeCalibration()); doesn't work :( 
    }

    void Start()
    {
        FoveManager.TareOrientation();
        rend.enabled = true;
        WhiteScreenRend.enabled = false;
        CalDotRend.enabled = false;

        if (calibrate.isOn)
        {
            StartCoroutine(Wait_for_calibration());//FoveManager.WaitForEyeTrackingCalibrationEnd);
        }
        else
            return;        
        //var direction = MainCamera.transform.position - FixationDot.transform.position;
        //MainCamera.transform.rotation = Quaternion.LookRotation(direction);
    }

    private void Temp_Texts() //all Raul! In order to get Eyes Image.
    {
        eyeimages.LoadRawTextureData(FoveManager.theEyeImage().ImageData.data, (int)FoveManager.theEyeImage().ImageData.length);
        eyeimages.Apply();
        rimage.texture = eyeimages;
    }
    // assign this as the first element in the "On Trial Begin" event in the Session component inspector




    public void onTrialStart(Trial trial)
    {
        string imageType = trial.settings.GetString("stimulus_imageType");
        int imageTiltType = trial.settings.GetInt("stimulus_imageTiltType");
        int imageNumber =  trial.settings.GetInt("stimulus_number"); //new may 27

        if (imageTiltType == 0)
        {
            imageTiltDegs = -30;
        }
        
        if (imageTiltType == 1)
        {
            imageTiltDegs = 0;
        }

        if (imageTiltType == 2)
        {
            imageTiltDegs = 30;
        }

        // apply our tilt settings and image settings to the new object
        Sphere.transform.rotation = Quaternion.Euler(transform.rotation.x + imageTiltDegs, transform.rotation.y, transform.rotation.z);
        MeshRenderer meshRenderer = Sphere.GetComponent<MeshRenderer>();

        if (imageType == "scenes")
        {
            meshRenderer.material = scenes[imageNumber];
        }
        else //for fractals
        {
            meshRenderer.material = fractals[imageNumber];
        }
        
        // log some trial info in our excel sheet
        Material SphereSharedMaterial = GetComponent<MeshRenderer>().sharedMaterial;
        session.CurrentTrial.result["StimName"] = SphereSharedMaterial;
        Session.instance.CurrentTrial.result["Image Tilt Type"] = -imageTiltDegs;
        Session.instance.CurrentTrial.result["Image Type"] = imageType;
        Session.instance.CurrentTrial.result["Image Number"] = imageNumber;
        StartCoroutine(afterTrialStarts());
        
    }


    IEnumerator afterTrialStarts()
    {
        yield return new WaitForSeconds(session.settings.GetInt("trial_duration"));
        session.EndCurrentTrial();
        MeshRenderer meshRenderer = Sphere.GetComponent<MeshRenderer>();
        meshRenderer.material = BaseMaterial;
        rend.enabled = true;
    }
        
    public void onTrialEnd(Trial trial)
    { 
        if (trial.number % 20 == 0 || trial.number == 1)  // "if the trial number is perfectly divisable by 20 or if it's the first trial"
        {
            Debug.Log("Time for homemade calibration!");
            WhiteScreenRend.enabled = true;
            WhiteScreenRend = WhiteScreen.GetComponent<Renderer>();
            StartCoroutine(StartHomemadeCalibration());         
        }
    }

    IEnumerator StartHomemadeCalibration()
    {
        yield return new WaitUntil(() => Input.GetKeyDown(KeyCode.Space));  // this command makes it so that we wait to proceed until the space bar is pressed
        MeshRenderer meshRenderer = WhiteScreen.GetComponent<MeshRenderer>();
        meshRenderer.material = PlainWhite;
        CalDotRend.enabled = true;
        yield return new WaitForSeconds(2f);
        CalibrationDot.transform.position = new Vector3(5, 0, 0);
        yield return new WaitForSeconds(2f);
        CalibrationDot.transform.position = new Vector3(5, 0, 1.5f);
        yield return new WaitForSeconds(2f);
        CalibrationDot.transform.position = new Vector3(5, 0, 0);
        yield return new WaitForSeconds(2f);
        CalibrationDot.transform.position = new Vector3(5, 0, -1.5f);
        yield return new WaitForSeconds(2f);
        CalibrationDot.transform.position = new Vector3(5, 0, 0);
        yield return new WaitForSeconds(2f);
        CalibrationDot.transform.position = new Vector3(5, -1.5f, 0);
        yield return new WaitForSeconds(2f);
        CalibrationDot.transform.position = new Vector3(5, 0, 0);
        yield return new WaitForSeconds(2f);
        CalibrationDot.transform.position = new Vector3(5, 1.5f, 0);
        yield return new WaitForSeconds(2f);
        CalibrationDot.transform.position = new Vector3(5, 0, 0);
        yield return new WaitForSeconds(2f);
        CalDotRend.enabled = false;
        WhiteScreenRend.enabled = false;       
    }


    void Update()
    {
        var rotation = FoveManager.GetHmdRotation();
        //Debug.Log(rotation.value); // FOVE stores information in a data structure called Result, and so to get the actual values, you need to put ".value" otherwise this won't work

        var thetaRad = Mathf.Atan2(2 * (rotation.value[1] * rotation.value[2] - rotation.value[0] * rotation.value[3]), (1.0f - 2.0f * (rotation.value[2] * rotation.value[2] + rotation.value[3] * rotation.value[3])));
        var thetaDeg = thetaRad * Mathf.Rad2Deg; // in degrees

        var phiRad = -Mathf.Asin(2 * (rotation.value[1] * rotation.value[3] - rotation.value[0] * rotation.value[2]));
        var phiDeg = phiRad * Mathf.Rad2Deg;

        var psiRad = Mathf.Atan2(2 * (rotation.value[0] * rotation.value[1] - rotation.value[2] * rotation.value[3]), (1.0f - 2.0f * (rotation.value[1] * rotation.value[1] + rotation.value[2] * rotation.value[2])));
        var psiDeg = psiRad * Mathf.Rad2Deg;

        Debug.Log("theta:" + thetaDeg + "   phi:" + phiDeg + "   psi:" + psiDeg);
        // in real time, it looks like theta = pitch, phi = yaw, psi = roll..... but this is not what it looks like in matlab (in matlab, theta is roll)


        if (Input.GetKeyDown(KeyCode.Alpha1) & !session.InTrial)
        {
            rend = FixationDot.GetComponent<Renderer>();
            rend.enabled = false; 
            session.BeginNextTrial();
        } 
    }

    private void Awake() //all Raul for EyesImage! 
    {
        InvokeRepeating("Temp_Texts", 2f, 0.5f); // means that every .5 s, the image will get recorded

        texBoi = new Texture2D(480, 190);
        for (int i = 0; i < 480; i++)
            for (int j = 0; j < 190; j++)
                texBoi.SetPixel(i, j, Color.red);
        texBoi.Apply();
        rimage.texture = texBoi;

        eyeimages = new Texture2D(640, 240, TextureFormat.RGB24, false);
    }


    // OLD (but maybe useful later??)
    // These two commands are what freezes the scene regardless of head movement.... they produce a lot of jitter :/
    // Sphere.transform.parent = FoveInterface.transform;
    // FixationDot.transform.parent = FoveInterface.transform;
    //
    // OLD way of randomizing 
    //else if (imageType == "2")
    //{
    //    if (FractalMatList.Count == 0)
    //    {
    //        //if the list is empty, add things to it
    //        FractalMatList.Add(1);
    //        FractalMatList.Add(2);
    //        FractalMatList.Add(3);
    //        FractalMatList.Add(4);
    //        FractalMatList.Add(5);
    //        FractalMatList.Add(6);
    //        FractalMatList.Add(7);
    //        FractalMatList.Add(8);
    //        FractalMatList.Add(9);
    //        FractalMatList.Add(10);
    //        FractalMatList.Add(11);
    //        FractalMatList.Add(12);
    //        FractalMatList.Add(13);
    //        FractalMatList.Add(14);
    //        FractalMatList.Add(15);
    //        FractalMatList.Add(16);
    //        FractalMatList.Add(17);
    //        FractalMatList.Add(18);
    //        FractalMatList.Add(19);
    //        FractalMatList.Add(20);
    //        FractalMatList.Add(21);
    //        FractalMatList.Add(22);
    //        FractalMatList.Add(23);
    //        FractalMatList.Add(24);
    //        FractalMatList.Add(25);
    //        FractalMatList.Add(26);
    //        FractalMatList.Add(27);
    //        FractalMatList.Add(28);
    //        FractalMatList.Add(29);
    //        FractalMatList.Add(30);
    //    }

    //    // using lists to make the stimuli randomized and evenly/equally used
    //    int randIndex = Random.Range(0, FractalMatList.Count);  // get a random index
    //    int getVal = FractalMatList[randIndex];  // get the actual value of that index
    //    Debug.Log(randIndex);
    //    Debug.Log(getVal);
    //    meshRenderer.material = fractals[getVal - 1];  // set the material to be the actual value (minus 1 bc indexing is annoying in unity)
    //    FractalMatList.RemoveAt(randIndex);  // remove the element in the list that is the indexed numbered
    //    Debug.Log("here's the list after deletion...");
    //    foreach (var x in FractalMatList) Debug.Log(x.ToString());  // this prints out the list 

    //    // log some trial info in our excel sheet
    //    Material SphereSharedMaterial = GetComponent<MeshRenderer>().sharedMaterial;
    //    session.CurrentTrial.result["StimName"] = SphereSharedMaterial;
    //    session.CurrentTrial.result["ImageType"] = "Fractal";
    //    session.CurrentTrial.result["TiltType"] = -(imageTiltType);
    //    //session.CurrentTrial.result["HeadRotation"] = TheHeadRotation;
    //    StartCoroutine(afterTrialStarts());
    //// Make lists to help with keeping track of images (scenes + fractals) that are shown 
    //    List<int> SceneMatList = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30 };  // the numbers here should match how many scene stimuli you have in the "scenes" material array
    //    List<int> FractalMatList = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30 };  // same as above, but for fractals
    //
    //private string TheHeadRotation = "straight"; // change this depending on the head tilt of the subject; can be "straight", "tiltLeft", or "tiltRight"

}