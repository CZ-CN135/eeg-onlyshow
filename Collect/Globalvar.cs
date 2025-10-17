using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Collect
{
    static class Globalvar
    {
        public static bool issave = false;
        public static string startTime = "";
        public static string endTime = "";
        public static string version = "";
        public static string patientId = "";
        public static string recordId = "";
        public static string reserved = "";
        public static string[] labels = { "EEG" };
        public static string[] transducerTypes = { "--" };
        public static string[] physicalDimensions = { "30" };
        public static double[] physicalMinimums = { 45, 50 };
        public static double[] physicalMaximums = { 30, 45 };
        public static int[] digitalMinimums = { 1, 2, 3 };
        public static int[] digitalMaximums = { 2, 3, 4 };
        public static string[] preFilterings = { "--" };
        public static int[] numberOfSamplesPerRecord = { 23, 45 };
        public static string[] signalsReserved = { "reserverd" };
        
    }
}
