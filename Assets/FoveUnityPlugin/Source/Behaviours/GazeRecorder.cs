using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using Fove.Unity;
using Fove;
using System.Text;

using Stopwatch = System.Diagnostics.Stopwatch;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class ScriptExecutionOrder : Attribute
{
    public int order;
    public ScriptExecutionOrder(int order) { this.order = order; }
}

#if UNITY_EDITOR
[InitializeOnLoad]
public class ScriptExecutionOrderManager
{
    static ScriptExecutionOrderManager()
    {
        foreach (MonoScript monoScript in MonoImporter.GetAllRuntimeMonoScripts())
        {
            if (monoScript.GetClass() == null)
                continue;

            foreach (var attr in Attribute.GetCustomAttributes(monoScript.GetClass(), typeof(ScriptExecutionOrder)))
            {
                var newOrder = ((ScriptExecutionOrder)attr).order;
                if (MonoImporter.GetExecutionOrder(monoScript) != newOrder)
                    MonoImporter.SetExecutionOrder(monoScript, newOrder);
            }
        }
    }
}
#endif

// A behaviour class which records eye gaze data (with floating-point timestamps) and writes it out to a .csv file
// for continued processing.
[ScriptExecutionOrder(1000)] // execute last so that user current frame transformations on the fove interface pose are included
public class GazeRecorder : MonoBehaviour
{
    public const string OutputFolder = "ResultData";

    public enum RecordingRate
    {
        _70FPS, // synch with vsynch
        _120FPS, // synch with eye frame
    }

    public enum CoordinateSpace
    {
        World,
        Local,
        HMD,
    }

    [Serializable]
    public class ExportSettings
    {
        [Tooltip("The time since the application started.")]
        public bool ApplicationTime = true;

        [Tooltip("Custom mark set by the user when pressing the corresponding key. ")]
        public bool UserMark = true;

        [Tooltip("The two eyes convergence info")]
        public bool GazeConvergence = true;

        [Tooltip("The gaze ray for each eye separately")]
        public bool EyeRays = true;

        [Tooltip("The open, closed or not detected status of the eyes")]
        public bool EyesState = true;

        [Tooltip("The current radius of the pupil")]
        public bool PupilsRadius = true;

        [Tooltip("The Unity object gazed by the user")]
        public bool GazedObject = true;

        [Tooltip("The torsion of the left & right eyes in degree")]
        public bool EyeTorsion = true;

        [Tooltip("The head rotation")]
        public bool HeadRotation = true;

        [Tooltip("The head Position")]
        public bool HeadPosition = true;

        [Tooltip("The pupil shape")]
        public bool PupilShape = true;

        [Tooltip("The interpupilary distance")]
        public bool IPD = true;

        [Tooltip("The iris radius")]
        public bool IrisRadius = true;

        [Tooltip("The eyes image")]
        public bool EyesImage = true;

    }

    // Require a reference (assigned via the Unity Inspector panel) to a FoveInterface object.
    // This could be either FoveInterface
    [Tooltip("This should be a reference to a FoveInterface object of the scene.")]
    public FoveInterface fove = null;

    // Pick a key (customizable via the Inspector panel) to toggle recording.
    [Tooltip("Pressing this key will toggle data recording.")]
    public KeyCode toggleRecordingKey = KeyCode.Return;

    // Pick a key (customizable via the Inspector panel) to toggle recording.
    [Tooltip("Pressing this key will add a mark in the data recording.")]
    public KeyCode markFrameKey = KeyCode.Space;

    [Tooltip("Specify the rate at which gaze sampling is performed. 70FPS samples the gaze once every frame." +
        "120FPS samples the gaze once every new incoming eye data")]
    public RecordingRate recordingRate;

    [Tooltip("Specify the coordinate space used for gaze convergence and eye vector rays.")]
    public CoordinateSpace gazeCoordinateSpace;

    // The name of the file to write our results into
    [Tooltip("The base name of the file. Don't add any extensions, as \".csv\" will be appended to whatever you put here.")]
    public string outputFileName = "fove_recorded_results";

    // Check this to overwrite existing data files rather than incrementing a value each time.
    [Tooltip("If the specified filename already exists, the recorder will increment a counter until an unused " +
             "filename is found.")]
    public bool overwriteExistingFile = false;

    [Tooltip("Specify which data fields to export to the csv file")]
    public ExportSettings exportFields;

    // The number a data to record before writing out to disk
    [Tooltip("The number of entries to store in memory before writing asynchronously to disk")]
    public int writeAtDataCount = 1000;

    //=================//
    // Private members //
    //=================//

    // An internal flag to track whether we should be recording or not
    private bool shouldRecord;

    // A struct for recording in one place all the information that needs to be recorded for each frame
    // If you need more data recorded, you can add more fields here. Just be sure to write is out as well later on.
    struct Datum
    {
        public float AppTime;
        public bool UserMark;
        public Result<string> GazedObjectName;
        public Result<Ray> GazeConvergenceRay;
        public Result<float> GazeConvergenceDepth;
        public Stereo<Result<Ray>> EyeRays;
        public Stereo<Result<EyeState>> EyesState;
        public Stereo<Result<float>> PupilsRadius;
        public Stereo<Result<float>> EyeTorsions;
        public Result<Quaternion> HeadRotation;
        public Result<Vector3> HeadPosition;
        public Stereo<Result<Fove.Unity.PupilShape>> PupilShape;
        public Result<float> IPD;
        public Stereo<Result<float>> IrisRadius;
    }

    const char CsvSeparator = ',';

    interface IDataWriter
    {
        void Append(StringBuilder stringBuilder);
    }

    class AggregatedData : List<Datum>
    {
        public AggregatedData(int reserveCount) : base(reserveCount) { }
    }

    class ConcurrentQueue<T>
    {
        Queue<T> queue = new Queue<T>();

        public bool IsEmpty 
        {  
            get
            {
                lock (queue)
                    return queue.Count == 0;
            } 
        }

        public void Enqueue(T t)
        {
            lock (queue)
                queue.Enqueue(t);
        }

        public bool TryDequeue(out T t)
        {
            lock (queue)
            {
                if (queue.Count == 0)
                {
                    t = default(T);
                    return false;
                }

                t = queue.Dequeue();
                return true;
            }
        }
    }

    // A list for storing the recorded data from many frames
    private AggregatedData dataSlice;

    // This reference to a list is used by the writing thread. Essentially, one list is being populated (above)
    // while another can be writing out to disk asynchronously (this one).
    private ConcurrentQueue<IDataWriter> dataToWrite = new ConcurrentQueue<IDataWriter>();

    private EventWaitHandle threadWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);

    // Track whether or not the write thread should live.
    private bool writeThreadShouldLive = true;

    // Track whether or not the write thread should live.
    private bool collectThreadShouldLive = true;

    // The thread object which we will call into the write thread function.
    private Thread writeThread;

    // The thread object which we will call into the write thread function.
    private Thread collectThread;

    // Fove interface transformation matrices
    private class UnityThreadData
    {
        public Matrix4x4 HMDToLocal;
        public Matrix4x4 HMDToWorld;
        public Result<string> gazedObject = new Result<string>("", ErrorCode.Data_NoUpdate);
        public bool markKeyDown;
    }
    private UnityThreadData unityThreadData = new UnityThreadData();

    private Stopwatch stopwatch = new Stopwatch(); // Unity Time.time can't be use outside of main thread.
    
    // Use this for initialization.
    void Start () 
    {
        stopwatch.Start();
        if (!Stopwatch.IsHighResolution)
            Debug.LogWarning("High precision stopwatch is not supported on this machine. Recorded frame times may not be highly accurate.");

        // Check to make sure that the FOVE interface variable is assigned. This prevents a ton of errors
        // from filling your log if you forget to assign the interface through the inspector.
        if (fove == null)
        {
            Debug.LogWarning("Forgot to assign a Fove interface to the FOVERecorder object.");
            enabled = false;
            return;
        }

        var caps = ClientCapabilities.EyeTracking;
        if (exportFields.GazeConvergence)
            caps |= ClientCapabilities.GazeDepth;
        if (exportFields.PupilsRadius)
            caps |= ClientCapabilities.PupilRadius;
        if (exportFields.GazedObject)
            caps |= ClientCapabilities.GazedObjectDetection;
        if (exportFields.EyeTorsion)
            caps |= ClientCapabilities.EyeTorsion;
        if (exportFields.PupilShape)
            caps |= ClientCapabilities.PupilShape;
        if (exportFields.IPD)
            caps |= ClientCapabilities.UserIPD;
        if (exportFields.IrisRadius)
            caps |= ClientCapabilities.IrisRadius;
        if (exportFields.EyesImage)
            caps |= ClientCapabilities.EyesImage;

        FoveManager.RegisterCapabilities(caps);

        //SR addition from FOVE ppl that will help us to disable head tracking but GET head tracking data
        FoveManager.RegisterCapabilities(ClientCapabilities.OrientationTracking | ClientCapabilities.PositionTracking);

        // We set the initial data slice capacity to the expected size + 1 so that we never waste time reallocating and
        // copying data under the hood. If the system ever requires more than a single extra entry, there is likely
        // a severe problem causing delays which should be addressed.
        dataSlice = new AggregatedData(writeAtDataCount + 1);

        // If overwrite is not set, then we need to make sure our selected file name is valid before proceeding.
        if (!Directory.Exists(OutputFolder))
            Directory.CreateDirectory(OutputFolder);
        {
            string testFileName = Path.Combine(OutputFolder, outputFileName + ".csv");
            if (!overwriteExistingFile)
            {
                int counter = 1;
                while (File.Exists(testFileName))
                {
                    testFileName = Path.Combine(OutputFolder, outputFileName + "_" + (counter++) + ".csv"); // e.g., "results_12.csv"
                }
            }
            outputFileName = testFileName;

            Debug.Log("Writing data to " + outputFileName);
        }

        dataToWrite.Enqueue(new DataHeaderSerializer(exportFields));

        // Create the write thread to call "WriteThreadFunc", and then start it.
        writeThread = new Thread(WriteThreadFunc);
        writeThread.Start();

        StartCoroutine(JobsSpawnerCoroutine());

        shouldRecord = true; // JOM: 5/31/2021 to record always from the begining
    }

    // Unity's standard Update function, here used only to listen for input to toggle data recording
    void Update()
    {
        //// If you press the assigned key, it will toggle the "recordingStopped" variable.
        //if (Input.GetKeyDown(toggleRecordingKey))
        //{
        //    shouldRecord = !shouldRecord;
        //    Debug.Log(shouldRecord ? "Starting" : "Stopping" + " data recording...");
        //}
    }

    // This is called when the program quits, or when you press the stop button in the editor (if running from there).
    void OnApplicationQuit()
    {
        shouldRecord = false;
        collectThreadShouldLive = false;
        if(collectThread != null)
            collectThread.Join(100);

        if (writeThread != null)
        {
            // Tell the thread to end, then release the wait handle so it can finish.
            writeThreadShouldLive = false; 
            flushData();

            // Wait for the write thread to end (up to 1 second).
            writeThread.Join(1000);
        }
    }
    IEnumerator JobsSpawnerCoroutine()
    {
        // ensure that the headset is connected and ready before starting any recording
        var nextFrameAwaiter = new WaitForEndOfFrame();
        while (!FoveManager.IsHardwareConnected())
            yield return nextFrameAwaiter;

        // if the recording rate is the same as the fove rendering rate,
        // we use a coroutine to be sure that recorded gazes are synchronized with frames
        if (recordingRate == RecordingRate._70FPS)
        {
            // Coroutines give us a bit more control over when the call happens, and also simplify the code
            // structure. However they are only ever called once per frame -- they processing to happen in
            // pieces, but they shouldn't be confused with threads.
            StartCoroutine(RecordDataCoroutine());
        }
        else // otherwise we just start a vsynch asynchronous thread
        {
            StartCoroutine(RecordFoveTransformCoroutine());
            collectThread = new Thread(CollectThreadFunc);
            collectThread.Start();
        }
    }

    void flushData()
    {
        if (dataSlice != null)
            dataToWrite.Enqueue(new AggregatedDataSerializer(exportFields) { Data = dataSlice });

        dataSlice = new AggregatedData(writeAtDataCount + 1);

        if (!threadWaitHandle.Set())
            Debug.LogError("Error setting the event to wake up the file writer thread on application quit");
    }

    private void UpdateFoveInterfaceMatrices(bool immediate)
    {
        var t = fove.transform;

        var markKeyDown = Input.GetKey(markFrameKey);
        var gazedObjectResult = FoveManager.GetGazedObject();
        var gazedObjectName = new Result<string>(gazedObjectResult.value? gazedObjectResult.value.name : "", gazedObjectResult.error);

        if (immediate)
        {
            // In the case of 120 FPS recording rate, we re-fetch the HMD latest pose
            // and locally recalculate the fove interface local transform
            var isStanding = fove.poseType == FoveInterface.PlayerPose.Standing;

            var hmdAdjustedPosition = FoveManager.GetHmdPosition(isStanding);
            //var localPos = fove.fetchPosition? hmdAdjustedPosition : t.position; //old
            var localPos = fove.fetchPosition ? FoveManager.GetHmdPosition(isStanding) : t.position; //new SR from FOVE advice
            var localRot = fove.fetchOrientation? FoveManager.GetHmdRotation() : t.rotation;

            var parentTransfo = t.parent != null ? t.parent.localToWorldMatrix : Matrix4x4.identity;
            var localTransfo = Matrix4x4.TRS(localPos, localRot, t.localScale);

            lock (unityThreadData)
            {
                unityThreadData.HMDToWorld = parentTransfo * localTransfo;
                unityThreadData.HMDToLocal = localTransfo;
                unityThreadData.markKeyDown = markKeyDown;
                unityThreadData.gazedObject = gazedObjectName;
            }
        }
        else
        {
            // no need to lock the object, we are in synchronize mode (access from the same thread)
            unityThreadData.HMDToWorld = t.localToWorldMatrix;
            unityThreadData.HMDToLocal = Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale);
            unityThreadData.markKeyDown = markKeyDown;
            unityThreadData.gazedObject = gazedObjectName;
        }
    }

    private void RecordDatum(bool immediate)
    {
        // If recording is stopped (which is it by default), loop back around next frame.
        if (!shouldRecord)
            return;

        if (!immediate) // we run in the same thread as unity we can update the transformations in a synchronized way
            UpdateFoveInterfaceMatrices(false);

        bool frameMarked;
        Result<string> gazedObjectName;
        Matrix4x4 transformMat;
        lock (unityThreadData)
        {
            switch (gazeCoordinateSpace)
            {
                case CoordinateSpace.World:
                    transformMat = unityThreadData.HMDToWorld;
                    break;
                case CoordinateSpace.Local:
                    transformMat = unityThreadData.HMDToLocal;
                    break;
                default:
                    transformMat = Matrix4x4.identity;
                    break;
            }
            frameMarked = unityThreadData.markKeyDown;
            gazedObjectName = unityThreadData.gazedObject;
        }

        var eyeOffsets = FoveManager.GetEyeOffsets();
        var eyeVectorL = FoveManager.GetHmdGazeVector(Eye.Left);
        var eyeVectorR = FoveManager.GetHmdGazeVector(Eye.Right);

        Stereo<Result<Ray>> eyeRays;
        eyeRays.left = Utils.CalculateWorldGazeVector(ref transformMat, ref eyeOffsets.value.left, ref eyeVectorL);
        eyeRays.right = Utils.CalculateWorldGazeVector(ref transformMat, ref eyeOffsets.value.right, ref eyeVectorR);

        var convergenceDepth = FoveManager.GetCombinedGazeDepth();
        var convergenceRay = FoveManager.GetHmdCombinedGazeRay();
        convergenceRay.value.origin = transformMat.MultiplyPoint(convergenceRay.value.origin);
        convergenceRay.value.direction = transformMat.MultiplyVector(convergenceRay.value.direction).normalized;

        var pupilRadiusLeft = FoveManager.GetPupilRadius(Eye.Left);
        var pupilRadiusRight = FoveManager.GetPupilRadius(Eye.Right);

        var eyeStateL = FoveManager.GetEyeState(Eye.Left);
        var eyeStateR = FoveManager.GetEyeState(Eye.Right);

        var eyeTorsionL = FoveManager.GetEyeTorsion(Eye.Left);
        var eyeTorsionR = FoveManager.GetEyeTorsion(Eye.Right);

        var pupilShapeL = FoveManager.GetPupilShape(Eye.Left);
        var pupilShapeR = FoveManager.GetPupilShape(Eye.Right);

        var irisRadiusL = FoveManager.GetIrisRadius(Eye.Left);
        var irisRadiusR = FoveManager.GetIrisRadius(Eye.Right);

        // If you add new fields, be sure to write them here.
        var isStanding = fove.poseType == FoveInterface.PlayerPose.Standing;
        var hmdAdjustedPosition = FoveManager.GetHmdPosition(isStanding);

        var datum = new Datum
        {
            AppTime = (float)stopwatch.Elapsed.TotalSeconds,
            UserMark = frameMarked,
            GazedObjectName = gazedObjectName,
            GazeConvergenceRay = convergenceRay,
            GazeConvergenceDepth = convergenceDepth,
            EyeRays = eyeRays,
            EyesState = new Stereo<Result<EyeState>>(eyeStateL, eyeStateR),
            PupilsRadius = new Stereo<Result<float>>(pupilRadiusLeft, pupilRadiusRight),
            EyeTorsions = new Stereo<Result<float>>(eyeTorsionL, eyeTorsionR),
            HeadRotation = new Result<Quaternion>(FoveManager.GetHmdRotation()),
            HeadPosition = new Result<Vector3>(hmdAdjustedPosition),
            PupilShape = new Stereo<Result<Fove.Unity.PupilShape>>(pupilShapeL, pupilShapeR),
            IPD = new Result<float>(FoveManager.GetUserIPD()),
            IrisRadius = new Stereo<Result<float>>(irisRadiusL, irisRadiusR)
        };
        dataSlice.Add(datum);

        if (dataSlice.Count >= writeAtDataCount) 
            flushData();
    }

    // The coroutine function which records data to the dataSlice List<> member
    IEnumerator RecordDataCoroutine()
    {
        var nextFrameAwaiter = new WaitForEndOfFrame();

        // Infinite loops are okay within coroutines because the "yield" statement pauses the function each time to
        // return control to the main program. Great for breaking tasks up into smaller chunks over time, or for doing
        // small amounts of work each frame but potentially outside of the normal Update cycle/call order.
        while (true)
        {
            // This statement pauses this function until Unity has finished rendering a frame. Inside the while loop,
            // this means that this function will resume from here every frame.
            yield return nextFrameAwaiter;

            RecordDatum(false);
        }
    }
    
    // this coroutine is used to fetch the fove interface transformation value from the unity main thread
    IEnumerator RecordFoveTransformCoroutine()
    {
        var nextFrameAwaiter = new WaitForEndOfFrame();

        while (true)
        {
            UpdateFoveInterfaceMatrices(true);
            yield return nextFrameAwaiter;
        }
    }

    // This is the collecting thread that collect and store data asynchronously from the rendering
    private void CollectThreadFunc()
    {
        Debug.Log("TEST");
        while (collectThreadShouldLive)
        {
            RecordDatum(true);

            var result = FoveManager.Headset.WaitAndFetchNextEyeTrackingData();
            if (result.Failed)
                Debug.LogError("An error happened while waiting for next eye frame. Error code:" + result.error);
        }
    }

    private void WriteDataFromThread()
    {
        IDataWriter writer;
        var builder = new StringBuilder();

        while (!dataToWrite.IsEmpty)
        {
            if (!dataToWrite.TryDequeue(out writer))
                continue;

            writer.Append(builder);
        }

        try
        {
            File.AppendAllText(outputFileName, builder.ToString());
        }
        catch (Exception e)
        {
            Debug.LogWarning("Exception writing to data file:\n" + e);
            writeThreadShouldLive = false;
        }
    }

    // This is the writing thread. By offloading file writing to a thread, we are less likely to impact perceived
    // performance inside the Unity game loop, and thus more likely to have accurate, consistent results.
    private void WriteThreadFunc()
    {
        while (writeThreadShouldLive)
        {
            if (threadWaitHandle.WaitOne())
                WriteDataFromThread();
        }

        // Try to write one last time once the thread ends to catch any missed elements
        WriteDataFromThread();
    }

    class DataHeaderSerializer : IDataWriter
    {
        private const string AppTimeHeader = "Application Time";
        private const string UserMarkHeader = "User Mark";
        private const string GazeObjectHeader = "Gazed Object";
        private const string GazeConvergenceHeader = "Gaze Convergence";
        private const string EyeRayHeader = "Eye ray";
        private const string EyeStateHeader = "Eye State";
        private const string PupilRadiusHeader = "Pupil radius (millimeters)";
        private const string EyeTorsionHeader = "Eye torsion (degrees)";
        private const string HeadRotationHeader = "Head Rotation";
        private const string PupilShapeHeader = "Pupil Shape";
        private const string IPDHeader = "IPD";
        private const string IrisRadiusHeader = "Iris Radius";
        private const string HeadPositionHeader = "Head Position";

        private readonly ExportSettings export;

        public DataHeaderSerializer(ExportSettings export)
        {
            this.export = export;
        }

        public void Append(StringBuilder builder)
        {
            Action<StringBuilder, string> append = (b, h) =>
            {
                b.Append(h);
                b.Append(CsvSeparator);
            };
            Action<StringBuilder, string> appendLeftRight = (b, h) =>
            {
                b.Append(h);
                b.Append(" left");
                b.Append(CsvSeparator);
                b.Append(h);
                b.Append(" right");
                b.Append(CsvSeparator);
            };

            Action<StringBuilder, string> appendXY = (b, h) =>
            {
                b.Append(h);
                b.Append(" x");
                b.Append(CsvSeparator);
                b.Append(h);
                b.Append(" y");
                b.Append(CsvSeparator);
            };

            Action<StringBuilder, string> appendXYZ = (b, h) =>
            {
                appendXY(b, h);
                b.Append(h);
                b.Append(" z");
                b.Append(CsvSeparator);
            };

            Action<StringBuilder, string> appendRay = (b, h) =>
            {
                appendXYZ(b, h + " pos");
                appendXYZ(b, h + " dir");
            };

            Action<StringBuilder, string> appendQuat = (b, h) =>
            {
                b.Append(h);
                b.Append(" w");
                b.Append(CsvSeparator);
                b.Append(h);
                b.Append(" x");
                b.Append(CsvSeparator);
                b.Append(h);
                b.Append(" y");
                b.Append(CsvSeparator);
                b.Append(h);
                b.Append(" z");
                b.Append(CsvSeparator);
            };

            Action<StringBuilder, string> appendVec = (b, h) =>
            {
                b.Append(h);
                b.Append(" x");
                b.Append(CsvSeparator);
                b.Append(h);
                b.Append(" y");
                b.Append(CsvSeparator);
                b.Append(h);
                b.Append(" z");
                b.Append(CsvSeparator);
            };

            Action<StringBuilder, string> appendPupShape = (b, h) =>
            {
                b.Append(h);
                b.Append(" A");
                b.Append(CsvSeparator);
                b.Append(h);
                b.Append(" B");
                b.Append(CsvSeparator);
                b.Append(h);
                b.Append(" C");
                b.Append(CsvSeparator);
                b.Append(h);
                b.Append(" D");
                b.Append(CsvSeparator);
            };

            // Append the full data header to the builder

            builder.Append(CsvSeparator); // keep the first column for the input file

            if (export.ApplicationTime)
                append(builder, AppTimeHeader);

            if (export.UserMark)
                append(builder, UserMarkHeader);

            if (export.GazeConvergence)
            {
                appendRay(builder, GazeConvergenceHeader + " ray");
                append(builder, GazeConvergenceHeader + " distance");
            }

            if (export.EyeRays)
            {
                appendRay(builder, EyeRayHeader + " left");
                appendRay(builder, EyeRayHeader + " right");
            }

            if (export.EyesState)
                appendLeftRight(builder, EyeStateHeader);

            if (export.PupilsRadius)
                appendLeftRight(builder, PupilRadiusHeader);

            if (export.GazedObject)
                append(builder, GazeObjectHeader);

            if (export.EyeTorsion)
                appendLeftRight(builder, EyeTorsionHeader);

            if (export.HeadRotation)
                appendQuat(builder, HeadRotationHeader);

            if (export.HeadPosition)
                appendVec(builder, HeadPositionHeader);

            if (export.PupilShape)
                appendPupShape(builder, PupilShapeHeader);

            if (export.IPD)
                append(builder, IPDHeader);

            if (export.IrisRadius)
                appendLeftRight(builder, IrisRadiusHeader);
            

            builder.Remove(builder.Length - 1, 1); // remove the last separator of the line
            builder.AppendLine();
        }
    }

    class AggregatedDataSerializer : IDataWriter
    {
        private readonly ExportSettings export;

        public AggregatedData Data { get; set; }

        private string timeFormat;
        private string torsionFormat;
        private string vectorFormat;
        private string eyeSizeFormat;
        private string quatFormat;

        public AggregatedDataSerializer(ExportSettings export)
        {
            this.export = export;

            // Setup the significant digits argument strings used when serializing numbers to text for the CSV
            torsionFormat = "{0:F2}";
            vectorFormat = "{0:F3}";
            timeFormat = "{0:F4}";
            eyeSizeFormat = "{0:F2}";
            quatFormat = "{0:F3}";
        }

        private void Append(StringBuilder builder, string format, float value, ErrorCode error)
        {
            builder.AppendFormat(format, value);
            if (error != ErrorCode.None)
            {
                builder.Append(" - ");
                builder.Append(error.ToString());
            }
            builder.Append(CsvSeparator);
        }

        private void Append(StringBuilder builder, string value, ErrorCode error)
        {
            builder.Append(value);
            if (error != ErrorCode.None)
            {
                builder.Append(" - ");
                builder.Append(error.ToString());
            }
            builder.Append(CsvSeparator);
        }

        private void Append(StringBuilder builder, string format, Result<float> result)
        {
            Append(builder, format, result.value, result.error);
        }

        private void Append(StringBuilder builder, Result<Vector3> result)
        {
            Append(builder, vectorFormat, result.value.x, result.error);
            Append(builder, vectorFormat, result.value.y, result.error);
            Append(builder, vectorFormat, result.value.z, result.error);
        }

        private void Append(StringBuilder builder, Result<Ray> ray)
        {
            Append(builder, new Result<Vector3>(ray.value.origin, ray.error));
            Append(builder, new Result<Vector3>(ray.value.direction, ray.error));
        }

        /// new append methods for head rotation, head position, pupil shape, IPD, and iris radius (eye shape is in here too, though not used anymore)
        private void Append(StringBuilder builder, Result<Quaternion> result, int variable1, int variable2, int variable3)
        {
            Append(builder, quatFormat, result.value.w, result.error);
            Append(builder, quatFormat, result.value.x, result.error);
            Append(builder, quatFormat, result.value.y, result.error);
            Append(builder, quatFormat, result.value.z, result.error);
        }

        private void Append(StringBuilder builder, Result<Vector3> result, int var1, int var2, int var3, int var4, int var5, int var6, int var7)
        {
            Append(builder, vectorFormat, result.value.x, result.error);
            Append(builder, vectorFormat, result.value.y, result.error);
            Append(builder, vectorFormat, result.value.z, result.error);
        }

        private void Append(StringBuilder builder, Vector2 result, ErrorCode errCde, int test, int test1, int test2)
        {
            Append(builder, vectorFormat, result.x, errCde);
            Append(builder, vectorFormat, result.y, errCde);
        }

        private void Append(StringBuilder builder, IEnumerable<Vector2> Result, ErrorCode error, int test, int test1, int test2)
        {
            foreach (Vector2 dog in Result)
            {
                Append(builder, vectorFormat, dog.x, error);
                Append(builder, vectorFormat, dog.y, error);
            }
        }

        private void Append(StringBuilder builder, Result<float> result, int variable1, int variable2, int variable3, int variable4, int variable5)
        {
            Append(builder, quatFormat, result.value, result.error);
        }

        private void Append(StringBuilder builder, Result<float> result, int variable1, int variable2, int variable3, int variable4, int variable5, int variable6)
        {
            Append(builder, torsionFormat, result.value, result.error);
        }


        public void Append(StringBuilder builder)
        {
            Debug.Log("Writing " + Data.Count + " lines");

            foreach (var datum in Data)
            {
                builder.Append(CsvSeparator);

                // This writes each element in the data list as a CSV-formatted line.
                if (export.ApplicationTime)
                    Append(builder, timeFormat, datum.AppTime, ErrorCode.None);

                if (export.UserMark)
                {
                    if (datum.UserMark) 
                        builder.Append('X');

                    builder.Append(CsvSeparator);
                }

                if (export.GazeConvergence)
                {
                    Append(builder, datum.GazeConvergenceRay);
                    Append(builder, vectorFormat, datum.GazeConvergenceDepth);
                }

                if (export.EyeRays)
                {
                    Append(builder, datum.EyeRays.left);
                    Append(builder, datum.EyeRays.right);
                }

                if (export.EyesState)
                {
                    Append(builder, datum.EyesState.left.value.ToString(), datum.EyesState.left.error);
                    Append(builder, datum.EyesState.right.value.ToString(), datum.EyesState.right.error);
                }

                if (export.PupilsRadius)
                {
                    var pupils = datum.PupilsRadius;
                    Append(builder, eyeSizeFormat, 1000 * pupils.left.value, pupils.left.error);
                    Append(builder, eyeSizeFormat, 1000 * pupils.right.value, pupils.right.error);
                }

                if (export.GazedObject)
                    Append(builder, datum.GazedObjectName.value, datum.GazedObjectName.error);

                if (export.EyeTorsion)
                {
                    Append(builder, torsionFormat, datum.EyeTorsions.left);
                    Append(builder, torsionFormat, datum.EyeTorsions.right);
                }

                if (export.HeadRotation)
                    Append(builder, datum.HeadRotation, 1, 2, 3);

                if (export.HeadPosition)
                    Append(builder, datum.HeadPosition, 1, 2, 3, 4 , 5 , 6, 7);

                if (export.PupilShape)
                {
                    Append(builder, datum.PupilShape.left.value.center, datum.PupilShape.left.error, 0, 1, 2);
                    Append(builder, datum.PupilShape.right.value.center, datum.PupilShape.right.error, 0, 1, 2);
                }

                if (export.IPD)
                    Append(builder, datum.IPD, 1,2,3,4,5);

                if (export.IrisRadius)
                    Append(builder, torsionFormat, datum.IrisRadius.left);
                    Append(builder, torsionFormat, datum.IrisRadius.right);


                if (builder.Length > 2) // remove the last "," of the line
                    builder.Remove(builder.Length - 1, 1);

                builder.AppendLine();
            }
        }

    }
}
