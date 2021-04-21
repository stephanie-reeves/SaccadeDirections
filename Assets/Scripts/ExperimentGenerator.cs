using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using Fove.Unity;
using UXF;

public class ExperimentGenerator : MonoBehaviour
{
    public GameObject RecorderObject;

    ///////this works, just isn't what we want
    //public void Generate(Session session)
    //{
    //    int numTrials = 10;
    //    session.CreateBlock(numTrials);

    //    //GameObject.Find("RecorderObject").GetComponent<GazeRecorder>().shouldRecord = true;

    //}
    public void Generate(Session session)
    {
        int[] imageTypes = new int[] { 1, 2 };
        int[] imageTiltTypes = new int[] { 0, 30, -30 };

        Block block1 = session.CreateBlock();

        int numRepeats = 20;
        foreach (int imageType in imageTypes)
        {
            foreach (int imageTiltType in imageTiltTypes)
            {
                for (int i = 0; i < numRepeats; i++)
                {
                    Trial newTrial = block1.CreateTrial();
                    newTrial.settings.SetValue("stimulus_imageType", imageType);
                    newTrial.settings.SetValue("stimulus_imageTiltType", imageTiltType);
                }
            }
        }
        block1.trials.Shuffle();
    }

    //public void Generate(Session session)
    //{
    //    int[] imageTypes = session.settings.GetValue("stimulus_imageType");
    //    int[] imageTiltTypes = session.settings.GetValue("stimulus_imageTiltType");

    //    int numRepeats = 10;
    //    for (int i = 0; i < numRepeats; i++)
    //    {
    //        Block block = session.CreateBlock();

    //        foreach (int imageType in imageTypes)
    //        {
    //            foreach (int imageTiltType in imageTiltTypes)
    //            {
    //                Trial newTrial = block.CreateTrial();
    //                newTrial.settings.SetValue("stimulus_imageType", imageType);
    //                newTrial.settings.SetValue("stimulus_imageTiltType", imageTiltType);
    //            }
    //        }
    //        block.trials.Shuffle();
    //    }
    //}
}
