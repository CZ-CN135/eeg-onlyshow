using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EDFCSharp;

namespace Collect
{
    interface IUpdate
    {
        List<Queue<double>> Datalist { get; set; }
        List<List<short>> SaveList { get; set; }
        /// <summary>
        /// /上传数据
        /// </summary>
        /// <param name="signals">信号值</param>
        /// <param name="Items">通道数</param>
        void Update(double signals, int Items);
        void Load();
        void Save(bool issave);
        bool NeedsSave { get; set; }

    }
    public class DataList : IUpdate
    {
        private bool mNeedSave = false;

        public bool NeedsSave { get { return mNeedSave; } set { mNeedSave = value; } }
        private List<Queue<double>> datalist = new List<Queue<double>>();
        private List<List<short>> savelist = new List<List<short>>();

        public List<Queue<double>> Datalist
        {
            get
            {
                return datalist;
            }
            set
            {
                datalist = value;
            }
        }

        public List<List<short>> SaveList { get { return savelist; } set { savelist = value; } }

        public void Load()
        {
            Console.WriteLine("Load");
        }

        public void Save(bool issave)
        {
            if (issave)
            {
                NeedsSave = true;
                Globalvar.startTime = System.DateTime.Now.ToString("hh_mm_ss");
            }
            else if ( NeedsSave & issave == false )
            {
                NeedsSave = false;
                Globalvar.endTime = System.DateTime.Now.ToString("hh_mm_ss");
                EDF_saver();
                savelist.Clear();

            }
            else
            {
                
            }
            
        }
        public void init()
        {
            
            while (this.datalist.Count < 50) { this.datalist.Add(new Queue<double>()); }
            while (this.savelist.Count < 50) { this.savelist.Add(new List<short>()); }
        }
        private EDFHeader EDF_header()
        {
            string Version = Globalvar.version;
            string patientId = Globalvar.patientId;
            string recordId = Globalvar.recordId;
            string recordingStartDate = Globalvar.startTime;
            string recordingStartTime = Globalvar.endTime;

            short sizeInBytes = (short)savelist[0].Count;
            string reserved = Globalvar.reserved;
            long numberOfDataRecords = (short)savelist[0].Count;
            double recordDurationInSeconds = (short)savelist[0].Count;
            short numberOfSignalsInRecord = (short)savelist[0].Count;
            string[] labels = Globalvar.labels;
            string[] transducerTypes = Globalvar.transducerTypes;
            string[] physicalDimensions = Globalvar.physicalDimensions;
            double[] physicalMinimums = Globalvar.physicalMinimums;
            double[] physicalMaximums = Globalvar.physicalMinimums;
            int[] digitalMinimums = Globalvar.digitalMaximums;
            int[] digitalMaximums = Globalvar.digitalMaximums;
            string[] preFilterings = Globalvar.preFilterings;
            int[] numberOfSamplesPerRecord = Globalvar.numberOfSamplesPerRecord;
            string[] signalsReserved = Globalvar.signalsReserved;
            var edfHeader = new EDFHeader(Version, patientId, recordId, recordingStartDate, recordingStartTime, sizeInBytes, reserved, numberOfDataRecords, recordDurationInSeconds, numberOfSignalsInRecord, labels, transducerTypes, physicalDimensions, physicalMinimums, physicalMaximums, digitalMinimums,
                digitalMaximums, preFilterings, numberOfSamplesPerRecord, signalsReserved);
            return edfHeader;
        }
        
        private void EDF_saver()
        {
            EDFHeader header = EDF_header();
            EDFSignal[] signalList = new EDFSignal[savelist.Count];
            int times = 0;
            foreach (List<short> d in savelist)
            {
                
                int index = d.Count;
                double frequency = 300;
                EDFSignal signal = new EDFSignal(index, frequency);
                signal.Samples = d;
                signalList[times] = signal;
                times++;

            }
            List<AnnotationSignal> annotationsSignals = new List<AnnotationSignal>();
            EDFFile edfFile = new EDFFile(header, signalList, annotationsSignals);
            string now = System.DateTime.Now.ToString("hh_mm_ss");
            string filename = now+"saved" + ".edf";
            EDFWriter edfWriter = new EDFWriter(File.Open(filename, FileMode.Create));
            edfWriter.WriteEDF(edfFile, filename);
        }

        public void Update(Double signals, int Items)
        {
            init();
            datalist[Items].Enqueue(signals);
            if (mNeedSave)
            {
                savelist[Items].Add((short)signals);
            }
        }
        public double DataGet(int channels)
        {
            if (Datalist[channels].Count > 0)
            {
                return Datalist[channels].Dequeue();
            }
            else
            {
                return 0;
            }
            
        }
        public void reset()
        {
            datalist.Clear();
            savelist.Clear();
        }
    }
}
