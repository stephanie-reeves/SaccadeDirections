using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using Fove.Unity;
using UXF;

public class ExperimentGenerator : MonoBehaviour
{
    //    ///public GameObject RecorderObject;

    //    ///////this works, just isn't what we want
    //    //public void Generate(Session session)
    //    //{
    //    //    int numTrials = 10;
    //    //    session.CreateBlock(numTrials);

    //    //    //GameObject.Find("RecorderObject").GetComponent<GazeRecorder>().shouldRecord = true;

    //    //}


    // this is what we used for the pilot study! Works fine, just trying to make better as of 27 May
    public void Generate(Session session)
    {
        List<string> imageTypes = session.settings.GetStringList("stimulus_imageType");
        List<int> imageTiltTypes = session.settings.GetIntList("stimulus_imageTiltType");
        List<int> imageNumbers = session.settings.GetIntList("stimulus_number");
        int numRepeats = session.settings.GetInt("num_repeats");

        Block block1; //this is empty, we are just declaring
        Trial newTrial;

        //int[] imageTypes = new int[] { 1, 2 };
        //int[] imageTiltTypes = new int[] { 0, 30, -30 };
        for (int i = 0; i < numRepeats; i++)
        {
            block1 = session.CreateBlock();

            foreach (string imageType in imageTypes)
            {
                foreach (int imageTiltType in imageTiltTypes)
                {
                    foreach (int imageNumber in imageNumbers)
                    {
                        newTrial = block1.CreateTrial();
                        newTrial.settings.SetValue("stimulus_imageType", imageType);
                        newTrial.settings.SetValue("stimulus_imageTiltType", imageTiltType);
                        newTrial.settings.SetValue("stimulus_number", imageNumber);
                    }

                }
            }
            block1.trials.Shuffle();
        }
    }


    //    public void Generate(Session session)
    //    {
    //        List<string> imageTypes = session.settings.GetStringList("stimulus_imageType");
    //        List<string> imageTiltTypes = session.settings.GetStringList("stimulus_imageTiltType");
    //        List<string> imageNumbers = session.settings.GetStringList("stimulus_number");
    //        int numRepeats = session.settings.GetInt("num_repeats");

    //        // Here is an empty list that we will add to 
    //        List<string[]> trialList = new List<string[]>();

    //        //var imageTiltTypes = [-30, 0, 30];
    //        //var imageTypes = ["scenes", "fractals"];

    //        //List<int> imageNumbers = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30 };  // the number of fractals (30) which is the also the number of scenes (30)

    //        //int numRepeats = 10;

    //        for (int i = 0; i < numRepeats; i++)
    //        {
    //            foreach (string imageType in imageTypes)
    //            {
    //                foreach (string imageTiltType in imageTiltTypes)
    //                {
    //                        //Trial newTrial = block.CreateTrial();
    //                        //newTrial.settings.SetValue("stimulus_imageType", imageType);
    //                        //newTrial.settings.SetValue("stimulus_imageTiltType", imageTiltType);
    //                    //newTrial.settings.SetValue("stimulus_number", imageNumber);
    //                    string[] temp = new string[] { imageType, imageTiltType, "1"}; //the 1 is for a random number that we will fill in later.... will cycle through 1 to 30 
    //                    trialList.Add(temp);
    //                }
    //            }
    //        }
    //        trialList.Shuffle();

    //        //FYI tempList[0][2] means 0th row out of 60, and 2th col (really 3rd but C# ya know)
    //        imageNumbers.Shuffle(); // will shuffle 1 to 30

    //        var totalTrials = numRepeats * imageTiltTypes.Count * imageTypes.Count;
    //        Block theBlock = session.CreateBlock(totalTrials);
    //        var trialcounter = 0;

    //        while (trialcounter < totalTrials)
    //        {
    //            for (int i = 0; i < 30; i++) //30 bc we have 30 numImages
    //            {
    //                {
    //                    trialList[trialcounter][2] = imageNumbers[i]; //  in trialList(trialcounter) in the 2nd place, put the number of imageNumbers that is shuffled. trial counter will go up through total trials so this will make it so that image numbers will cycle through 1-30 and then after that, reset back
    //                    trialcounter = trialcounter + i;
    //                }
    //            }
    //            imageNumbers.Shuffle(); //we have to do this so that once we go through all the image numbers, we will re-shuffle so  that can still use more 
    //        }

    //        // now we somehow need to assign the trial list info to the actual trials....?
    //        for (int i = 0; i < trialList.Count; i++)
    //        {
    //            Trial newTrial = theBlock.CreateTrial();
    //        }


    //        //////// not in use currently... was just playing
    //        //Debug.Log(trialList);
    //        //foreach (string trial in trialList)
    //        //{
    //        //    Trial newTrial = theBlock.CreateTrial();
    //        //}
    //        ///session.BeginNextTrial();

    //    }
}
