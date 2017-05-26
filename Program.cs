using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Xml;
using System.Text.RegularExpressions;

namespace ModifyMpd
{
    class Program
    {
        private static int totalFrames = 0;
        private static double maxImportance = 0;
        private static List<Tuple<int, int>> shotInfoList = new List<Tuple<int, int>>();
        private static Dictionary<int, double> importanceInfoDict = new Dictionary<int, double>();
        private static Dictionary<int, double> shotImportanceDict = new Dictionary<int, double>();
        private static Dictionary<string, Tuple<double, double>> segDurationDic = new Dictionary<string, Tuple<double, double>>();
        private static string segContextPattern = @"\/(?<se>[\S]+\.m4s=[\S]+@)";
        private static string segNamePattern = @"(?<na>[\S]+)=";
        private static string segStartPattern = @"=(?<st>[0-9]+)-";
        private static string segDurationPattern = @"=[0-9]+-(?<du>[0-9]+)@";

        private static List<string> computedSceneList = new List<string>();
        private static List<string> computedImportanceList = new List<string>();

        static void Main(string[] args)
        {
            var dashctx = @"D:\WorkItems\DarlingDash\ModifyMPD\dashctx.txt";
            var mpdFile = @"D:\WorkItems\DarlingDash\ModifyMPD\testvideo.mpd";
            var modifiedMpd = @"D:\WorkItems\DarlingDash\ModifyMPD\testvideo_modified.mpd";
            var shotInfo = @"D:\WorkItems\DarlingDash\ModifyMPD\shotsInfo.txt";
            var importanceInfo = @"D:\WorkItems\DarlingDash\ModifyMPD\silencyInfo.txt";
            var savedSceneInfo = @"D:\WorkItems\DarlingDash\ModifyMPD\savedSceneInfo.txt";
            var savedImportanceInfo = @"D:\WorkItems\DarlingDash\ModifyMPD\savedImportanceInfo.txt";

            if (InitSegDurationDictionary(dashctx))
            {
                Console.WriteLine("Sucessfully init segement duration using dashctx.");
            }

            if (!File.Exists(mpdFile))
            {
                Console.WriteLine("mpd file does not exist. exit with error!!!");
                return;
            }

            // should init importance before shot
            if (!InitImportanceInfoList(importanceInfo))
            {
                Console.WriteLine("init importance info failed. exit with error!!!");
                return;
            }
            if (!InitShotInfoList(shotInfo))
            {
                Console.WriteLine("init shot info failed. exit with error!!!");
                return;
            }

            XmlDocument mpdDoc = new XmlDocument();
            mpdDoc.Load(mpdFile);

            bool isInfoSaved = false;

            XmlNodeList reps = mpdDoc.GetElementsByTagName("Representation");
            foreach (XmlNode rep in reps)
            {
                var type = rep.Attributes.GetNamedItem("mimeType").Value;
                if (type == "video/mp4")
                {
                    //var frameRate = Int32.Parse(rep.Attributes.GetNamedItem("frameRate").Value);
                    var frameRate = GetFrameRate(rep.Attributes.GetNamedItem("frameRate").Value);
                    XmlNode segList = rep.FirstChild;

                    var timescale = double.Parse(segList.Attributes.GetNamedItem("timescale").Value);
                    var duration = double.Parse(segList.Attributes.GetNamedItem("duration").Value);
                    var segDuration = duration / timescale;

                    XmlNodeList segs = segList.ChildNodes;

                    // refine framesPerSeg to ignore duration error
                    var framesPerSeg = (int)(totalFrames / segs.Count);

                    int startFrame = 0;
                    int endFrame = 0;
                    int segIndex = 0;
                    foreach (XmlNode seg in segs)
                    {
                        var curMedia = seg.Attributes.GetNamedItem("media").Value;
                        GetStartAndEndFrame(frameRate, curMedia, segIndex, framesPerSeg, out startFrame, out endFrame);

                        var shot = GetShotString(startFrame, endFrame);
                        //var importance = GetImportanceString(startFrame, endFrame);
                        var importance = GetImportanceStringForShot(startFrame, endFrame);

                        if (!isInfoSaved)
                        {
                            computedSceneList.Add(shot);
                            computedImportanceList.Add(importance);
                        }

                        XmlAttribute sceneAttr = mpdDoc.CreateAttribute("scene");
                        sceneAttr.InnerText = shot;
                        XmlAttribute importanceAttr = mpdDoc.CreateAttribute("importance");
                        importanceAttr.InnerText = importance;

                        seg.Attributes.Append(sceneAttr);
                        seg.Attributes.Append(importanceAttr);

                        segIndex++;
                    }

                    isInfoSaved = true;
                }
            }

            mpdDoc.Save(modifiedMpd);
            SaveComputedImportanceInfo(savedImportanceInfo);
            SaveComputedSceneInfo(savedSceneInfo);

            Console.WriteLine("finished successfully!!");
        }

        private static void SaveComputedSceneInfo(string filePath)
        {
            var output = File.CreateText(filePath);
            foreach (var scene in computedSceneList)
            {
                output.WriteLine(scene);
            }
            output.Close();   
        }

        private static void SaveComputedImportanceInfo(string filePath)
        {
            var output = File.CreateText(filePath);
            int a = 0;
            foreach (var importance in computedImportanceList)
            {
                a++;
                output.WriteLine(importance);
            }
            output.Close();
        }

        private static bool InitSegDurationDictionary(string dashctxFile)
        {
            if (!File.Exists(dashctxFile))
            {
                return false;
            }

            var dashctx = File.ReadAllText(dashctxFile);
            var groups = Regex.Matches(dashctx, segContextPattern);

            foreach (Match g in groups)
            {
                var s = g.Groups["se"].Value;

                var n = Regex.Match(s, segNamePattern).Groups["na"].Value;
                var st = Regex.Match(s, segStartPattern).Groups["st"].Value;
                var du = Regex.Match(s, segDurationPattern).Groups["du"].Value;

                double stInMs, duInMs;
                if (double.TryParse(st, out stInMs) && double.TryParse(du, out duInMs))
                {
                    segDurationDic.Add(n, new Tuple<double, double>(stInMs / 1000.0, duInMs / 1000.0));
                }
            }

            return true;
        }

        private static bool InitShotInfoList(string shotInfoFile)
        {
            if (!File.Exists(shotInfoFile))
                return false;

            var shotNum = 0;
            foreach (var line in File.ReadAllLines(shotInfoFile))
            {
                var nums = line.Split();
                var begin = int.Parse(nums[0]);
                var end = int.Parse(nums[1]);
                var shotImportance = 0.0;

                for (int i = begin; i <= end; i++)
                {
                    shotInfoList.Add(new Tuple<int, int>(i, shotNum));
                    shotImportance += importanceInfoDict[i];
                }

                shotImportance /= (end - begin + 1);
                shotImportanceDict.Add(shotNum, shotImportance);
                shotNum++;
            }

            totalFrames = shotInfoList.OrderByDescending(s => s.Item1).First().Item1;
            maxImportance = shotImportanceDict.Max(s => s.Value);

            return true;
        }

        private static bool InitImportanceInfoList(string importanceInfoFile)
        {
            if (!File.Exists(importanceInfoFile))
                return false;

            var frameNum = 1;
            foreach (var line in File.ReadAllLines(importanceInfoFile))
            {
                double sal = 0;
                if (double.TryParse(line, out sal))
                {
                    importanceInfoDict.Add(frameNum, sal);
                }
                frameNum++;
            }

            return true;
        }

        private static double GetFrameRate(string fString)
        {
            double framerate = 24.0;

            if (fString.Contains('/'))
            {
                var exp = fString.Split('/');
                double e0, e1;
                double.TryParse(exp[0], out e0);
                double.TryParse(exp[1], out e1);
                framerate = e0 / e1;
            }
            else
            {
                double.TryParse(fString, out framerate);
            }

            return framerate;
        }

        private static string GetShotString(int startFrame, int endFrame)
        {
            startFrame = Math.Max(startFrame, 0);
            endFrame = Math.Min(endFrame, totalFrames);

            if (startFrame > endFrame)
            {
                return "shotUncertain";
            }

            var curFrames = shotInfoList.Where(s => s.Item1 >= startFrame && s.Item1 <= endFrame);
            var groups = curFrames.GroupBy(s => s.Item2).Select(g => new { shot = g.Key, count = g.Count() }).OrderByDescending(r => r.count);
            return string.Format("shot{0}", groups.First().shot);
        }

        private static string GetImportanceStringForShot(int startFrame, int endFrame)
        {
            startFrame = Math.Max(startFrame, 0);
            endFrame = Math.Min(endFrame, totalFrames);

            var shotNum =
                shotInfoList.Where(s => s.Item1 >= startFrame && s.Item1 <= endFrame)
                    .GroupBy(s => s.Item2)
                    .Select(g => new { shot = g.Key, count = g.Count() })
                    .OrderByDescending(r => r.count).First().shot;

            return GetUnifiedImportance(shotImportanceDict[shotNum]);
        }

        private static string GetImportanceString(int startFrame, int endFrame)
        {
            startFrame = Math.Max(startFrame, 0);
            endFrame = Math.Min(endFrame, totalFrames);

            double totalImportance = 0.0;

            for (int i = startFrame; i <= endFrame; i++)
            {
                if (importanceInfoDict.ContainsKey(i))
                {
                    totalImportance += importanceInfoDict[i];
                }
            }

            return GetUnifiedImportance(totalImportance / (endFrame - startFrame + 1));
        }

        private static string GetUnifiedImportance(double importance)
        {
            var weighedImportance = importance / maxImportance;
            return ((int)(weighedImportance * 10)).ToString();
        }

        private static void GetStartAndEndFrame(double frameRate, string mediaUrl, int segIndex, int framesPerSeg, out int start, out int end)
        {
            if (segDurationDic.ContainsKey(mediaUrl))
            {
                var segInfo = segDurationDic[mediaUrl];
                start = (int)(segInfo.Item1 * frameRate);
                end = start + (int)(segInfo.Item2 * frameRate);
            }
            else
            {
                start = segIndex * framesPerSeg;
                end = start + framesPerSeg;
            }

            start = Math.Max(0, start);
            end = Math.Min(totalFrames, end);
        }
    }
}
