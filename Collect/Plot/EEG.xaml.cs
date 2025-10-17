using Accord.Audio.Filters;
using Accord.Math;
using Accord.Statistics.Kernels;
using Collect.EEG;
using Collect.Power;
using Collect.tool;
using GalaSoft.MvvmLight.Threading;
using MathNet.Filtering;
using MathNet.Filtering.FIR;
using Microsoft.Win32;
using NWaves.Filters.BiQuad;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals.Axes;
using SciChart.Charting.Visuals.RenderableSeries;
using SciChart.Data.Model;
using SkiaSharp.Views.Desktop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using Xceed.Wpf.Toolkit;
using static Collect.Power.PowerSpectralDensityAnalyzer;
//using Collect.FIR;

namespace Collect.Plot
{
    public delegate void Ecg_ProEventHandler(object sender, EcgPowerEventArgs e);
    public delegate void EcgFilterEventHandler(object sender, EcgFilterEventArgs e);

    /// <summary>
    /// EEG.xaml 的交互逻辑
    /// </summary>
    public partial class EEG : UserControl
    {
        public event Ecg_ProEventHandler Ecg_ProEvent;
        public event EcgFilterEventHandler Ecg_FilterEvent;
        public event SpectrumAnalysisEventHandler SpectrumAnalysisEvent;
        //调用外部dll
        [DllImport("SciChart.Show.dll")]
        public static extern int WriteEdf_File_multifile(int id, string filename, int number_of_signals, int number_of_each_data_record);
        [DllImport("SciChart.Show.dll")]
        public static extern unsafe int WriteEdf_WriteData_multifile(int id, double* data);
        [DllImport("SciChart.Show.dll")]
        public static extern int WriteEdf_Finish_multifile(int id);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void DeleRecvFun(int count);

        [DllImport("DeviceSetting.dll")]
        private static extern int OpenDevice(DeleRecvFun fun, int value);
        [DllImport("DeviceSetting.dll")]
        private static extern int CloseDevice();
        [DllImport("DeviceSetting.dll")]
        private static extern int SetValue(int value);

        private static DeleRecvFun drf;

        int g_channels = 8;

        public TCPClient client = new TCPClient();
        SerialControl ecg_control = new SerialControl();

        const int CN = 8;
        public int PGA = 1;
        const int NL = 5;
        double[] NUM = new double[] { 0.9480807851293,   -3.070235355059,    4.381800263529,   -3.070235355059,
     0.9480807851293 };
        const int DL = 5;
        double[] DEN = new double[] { 1,   -3.152118327708,    4.379102839633,    -2.98835238241,
     0.8988589941553 };

        //二维动态数组,5行的数组
        double[][] buffer_in = new double[CN][];
        double[][] buffer_out = new double[CN][];


        short g_ecg_data = 0;
        int g_old_ecg_time = 0;
        double g_scale = 1;
        string localIp = "127.0.0.1";
        int localPort = 45555;
        string remoteIp = "127.0.0.1";
        int remotePort = 45552;
        UdpClient udpsti = new UdpClient();
        //存储远程端点
        IPEndPoint remoteIpep;
        UdpClient udpdata = new UdpClient();
        public EEG()
        {
            //8行5列数组,CN=8,DN=5
            for (int i = 0; i < CN; i++)
            {
                buffer_in[i] = new double[NL];
                buffer_out[i] = new double[DL];
            }
            //buffer_in所有元素初始化为0
            for (int i = 0; i < CN; i++)
            {
                for (int j = 0; j < NL; j++)
                    buffer_in[i][j] = 0;
                for (int j = 0; j < DL; j++)
                    buffer_out[i][j] = 0;
            }

            //用于初始化与UI线程相关，用于管理UI线程的类
            DispatcherHelper.Initialize();
            InitializeComponent();
            CreateChart();

            // 初始化定时器
            InitializeTimer();

            channel_1.IsChecked = true;
            channel_2.IsChecked = true;
            channel_3.IsChecked = true;
            channel_4.IsChecked = true;
            channel_5.IsChecked = true;
            channel_6.IsChecked = true;
            channel_7.IsChecked = true;
            channel_8.IsChecked = true;
            try
            {
                //将udpdata绑定到本地IP地址127.0.0.1和端口localPort
                udpdata.Connect("127.0.0.1", localPort);
                //解析字符串"127.0.0.1"为IP地址
                IPAddress ip = IPAddress.Parse("127.0.0.1");
                //定义由IP地址和端口号组成的网络端点
                IPEndPoint remoteIpep = new IPEndPoint(ip, remotePort);
                udpsti = new UdpClient(remoteIpep);



            }
            catch (Exception)
            {
                //MessageBox.Show("错误", "请检查网络");
            }


        }
        XyDataSeries<double, double>[] lineData;
        private double g_index = 0;
        private int channel_num = 8;

        private void CreateChart()
        {
            var xAxis = new NumericAxis() { AxisTitle = "Time (second)", VisibleRange = new DoubleRange(0, 2000) };
            var yAxis = new NumericAxis() { AxisTitle = "Value", Visibility = Visibility.Visible, VisibleRange = new DoubleRange(-1, 1) };

            sciChartSurface.XAxis = xAxis;
            sciChartSurface.YAxis = yAxis;


            // 创建 XyDataSeries 来托管图表的数据
            lineData = new XyDataSeries<double, double>[8];

            for (int i = 0; i < 8; i++)
            {
                //据系列可以存储的最大数据点数为5000，先进先出（FIFO）
                lineData[i] = new XyDataSeries<double, double>() { FifoCapacity = 5000 };
                //lineData[i].AcceptsUnsortedData = true;
            }

            for (int i = 0; i < 8; i++)
            {
                //8行100列
                //eeg_data_buffer[i] = new short[BaoLength];
                eeg_data_buffer[i] = new double[BaoLength];
                eeg_data_buffer_baseline[i] = new short[BaoLength];
            }
            for (int i = 0; i < 500; i++)
            {
                //500行8列
                save_data_buffer[i] = new double[9];
            }

            // 初始化频谱分析器
            psdAnalyzer = new PowerSpectralDensityAnalyzer();

            // 初始化小波去噪缓冲区
            for (int i = 0; i < 8; i++)
            {
                eeg_data_buffer_WaveletDenoise[i] = new double[BaoLength];
            }

            var colors = new[]
            {
                Colors.Red,
                Colors.Orange,
                Colors.Cyan,
                Colors.Green,
                Colors.Blue,
                Colors.Orchid,
                Colors.Purple,
                Colors.Brown,


            };
            // 添加8条曲线
            for (int i = 0; i < channel_num; i++)
            {
                var lineSeries = new FastLineRenderableSeries()
                {
                    Stroke = colors[i],
                    StrokeThickness = 1,
                    AntiAliasing = true,
                };
                sciChartSurface.RenderableSeries.Add(lineSeries);

            }
            //XyScatterRenderableSeries ScatterSeries = new XyScatterRenderableSeries();
            // 将数据分配给8条曲线
            for (int i = 0; i < channel_num; i++)
            {
                sciChartSurface.RenderableSeries[i].DataSeries = lineData[i];
            }

            sciChartSurface.XAxis.VisibleRange = ComputeXAxisRange(0);

        }
        private const double WindowSize = 10;
        private static DoubleRange ComputeXAxisRange(double t)
        {
            if (t < WindowSize)
            {
                return new DoubleRange(0, WindowSize);
            }
            //t 值向上取整到最接近的整数
            return new DoubleRange(Math.Ceiling(t) - WindowSize + 5, Math.Ceiling(t) + 5);
        }

        //tcp开始停止按钮
        public bool TCP_Install_ecg(string btn_con, string ip, int port)
        {

            if (btn_con == "开始")
            {
                bool is_open = client.Start(ip, port);
                if (is_open == false)
                {
                    return false;
                }
                else
                {
                    client.EcgEvent += new EcgTCPEventHandler(uav_control_CmdEvent);
                    return true;
                }

            }
            else
            {
                client.Stop();
                client.EcgEvent -= uav_control_CmdEvent;
                WriteEdf_Finish_multifile(0);
                return false;
            }

        }
        public bool Serial_install_ecg(string btn_con, string com)
        {
            if (btn_con == "开始")
            {
                if (com != null)
                {
                    //comboBox_gyroscope1.SelectedItem.ToString()
                    bool is_open = ecg_control.Start(com);
                    if (is_open == false)
                    {
                        return false;
                    }

                    else
                    {
                        string filePath = "setting.ini";
                        try
                        {
                            using (StreamWriter writer = new StreamWriter(filePath))
                            {
                                writer.WriteLine(com);
                            }
                        }
                        catch (Exception ex)
                        {
                            LogHelper.WriteErrorLog(ex.Message);
                            NlogHelper.WriteErrorLog(ex.Message);
                        }
                        ecg_control.EcgEvent += new EcgEventHandler(uav_control_CmdEvent);
                        return true;
                    }


                }
                else
                {
                    return false;
                }
            }
            else
            {
                ecg_control.Stop();
                ecg_control.EcgEvent -= uav_control_CmdEvent;

                WriteEdf_Finish_multifile(0);
                return false;
            }

        }


        double[] g_correct = new double[] { -23.581712, -20.9964171, -25.14858766, -20.86474412, -17.23367506, -23.1232183,
            -23.07801493, -22.6183719, -27.07592633, -21.00005965, -20.46724625, -20.74835414, -18.00185734, -21.13283581,
            -20.07800238, -20.26183012, -25.32642914, -1.094870871, -25.87697525, -29.91714316, -26.12149297, -16.10396041,
            -21.42748673, -22.36342259, -25.73750051, -24.72209132, -27.51651762, -24.88180281, -20.63316769, -20.24555302,
            -23.13314301, -29.52454332 };
        int buffer_index = 0;
        double[][] save_data_buffer = new double[500][];
        List<double[][]> save_data_buffer_all = new List<double[][]>();
        //short[] eeg_data = new short[8];
        short[] eeg_data = new short[8];
        short[] eeg_data_2th = new short[16];
        float[] eeg_data_float = new float[8];
        uint[] eeg_data_uint = new uint[8];
        byte[] eeg_data_byte = new byte[24];
        byte[] eeg_data_byte_8 = new byte[16];

        int BaoLength = 50;
        //pga1版本
        //void process_eegdata(byte[] eeg_data_byte)
        //{
        //    for (int i = 0; i < 8; i++)
        //    {
        //        eeg_data_uint[i] = Convert.ToUInt32((eeg_data_byte[0 + 3 * i] << 16) | (eeg_data_byte[1 + 3 * i] << 8) | eeg_data_byte[2 + 3 * i]);
        //    }
        //    for (int i = 0; i < 8; i++)
        //    {
        //        float datai = 0;
        //        if ((eeg_data_uint[i] & 0x800000) != 0)
        //        {
        //            datai = Convert.ToSingle((16777216 - eeg_data_uint[i]) * (-4500000.0) / (8388607));
        //            eeg_data_float[i] = datai;
        //        }
        //        else
        //        {
        //            datai = Convert.ToSingle((eeg_data_uint[i] * 4500000.0) / (8388607));
        //            eeg_data_float[i] = datai;
        //        }
        //    }

        //    for (int i = 0; i < 8; i++)
        //    {
        //        short data = (short)eeg_data_float[i];
        //        byte[] floatAsInt = BitConverter.GetBytes(data);
        //        eeg_data_byte_8[0 + 2 * i] = floatAsInt[0];
        //        eeg_data_byte_8[1 + 2 * i] = floatAsInt[1];
        //    }

        //}
        //不压缩版本
        void process_eegdata(byte[] eeg_data_byte)
        {
            for (int i = 0; i < 8; i++)
            {
                eeg_data_uint[i] = Convert.ToUInt32((eeg_data_byte[0 + 3 * i] << 16) | (eeg_data_byte[1 + 3 * i] << 8) | eeg_data_byte[2 + 3 * i]);
            }
            for (int i = 0; i < 8; i++)
            {
                float datai = 0;
                if ((eeg_data_uint[i] & 0x800000) != 0)
                {
                    datai = Convert.ToSingle((16777216 - eeg_data_uint[i]) * (-4500000.0) / (8388607* PGA));
                    eeg_data_float[i] = datai;
                }
                else
                {
                    datai = Convert.ToSingle((eeg_data_uint[i] * 4500000.0) / (8388607* PGA));
                    eeg_data_float[i] = datai;
                }
            }


        }
        //PGA24版本
        //void process_eegdata(byte[] eeg_data_byte)
        //{
        //    for (int i = 0; i < 8; i++)
        //    {
        //        eeg_data_uint[i] = Convert.ToUInt32((eeg_data_byte[0 + 3 * i] << 16) | (eeg_data_byte[1 + 3 * i] << 8) | eeg_data_byte[2 + 3 * i]);
        //    }
        //    for (int i = 0; i < 8; i++)
        //    {
        //        float datai = 0;
        //        if ((eeg_data_uint[i] & 0x800000) != 0)
        //        {
        //            datai = Convert.ToSingle((16777216 - eeg_data_uint[i]) * (-4500000.0) / (8388607*24));
        //            eeg_data_float[i] = datai;
        //        }
        //        else
        //        {
        //            datai = Convert.ToSingle((eeg_data_uint[i] * 4500000.0) / (8388607 * 24));
        //            eeg_data_float[i] = datai;
        //        }
        //    }

        //    for (int i = 0; i < 8; i++)
        //    {
        //        short data = (short)eeg_data_float[i];
        //        byte[] floatAsInt = BitConverter.GetBytes(data);
        //        eeg_data_byte_8[0 + 2 * i] = floatAsInt[0];
        //        eeg_data_byte_8[1 + 2 * i] = floatAsInt[1];
        //    }


        //}
        double fs = 1000; // 采样率Hz，根据实际情况修改

        int buffer_save_index = 0;
        //short[][] eeg_data_buffer = new short[8][];
        double[][] eeg_data_buffer = new double[8][];
        short[][] eeg_data_buffer_baseline = new short[8][];
        double[][] eeg_data_buffer_WaveletDenoise = new double[8][];

        // 频谱分析相关
        private PowerSpectralDensityAnalyzer psdAnalyzer;
        private string[] channelNames = { "Ch1", "Ch2", "Ch3", "Ch4", "Ch5", "Ch6", "Ch7", "Ch8" };
        private int spectrumAnalysisCounter = 0;
        private const int SPECTRUM_ANALYSIS_INTERVAL = 10; // 每10个数据包进行一次频谱分析

        // FIR滤波器相关
        //private FIRFilter[] firFilters = new FIRFilter[8];
        //private FilterChainConfig standardFilterChain;
        //private FIRFilterMonitor filterMonitor;
        static string filename = Directory.GetCurrentDirectory();
        string originalDataFile = System.IO.Path.Combine(filename, "Original_data.txt");
        private bool enableFIRFilter = true;

        //工频干扰
        static double sampleRate = 500;
        static double notchFreg = 50;
        static double notchBandwidth = 2;

        //1 计算带照上下腿
        static double fLow = notchFreg - notchBandwidth;
        static double fHigh = notchFreg + notchBandwidth;

        OnlineFilter notchFilter = OnlineFirFilter.CreateBandstop(
            ImpulseResponse.Finite,
            sampleRate,
            fLow,
            fHigh,
            256
        );

        
        void uav_control_CmdEvent(object sender, EcgTCPEventArgs e)
        {

            Numeeg += 33;
            //if (Ecg_ProEvent != null)
            //    Ecg_ProEvent(this, new EcgEventArgs("com", 1, e.value));

            //16原始版本
            //Buffer.BlockCopy(e.value, 0, eeg_data, 0, 16);

            //32数据包版本
            Buffer.BlockCopy(e.value, 2, eeg_data_byte, 0, 24);
            process_eegdata(eeg_data_byte);
            //Buffer.BlockCopy(eeg_data_byte_8, 0, eeg_data, 0, 16);
            //Buffer.BlockCopy(eeg_data_float, 0, eeg_data, 0, 16);

            if (Ecg_FilterEvent != null)
                Ecg_FilterEvent(this, new EcgFilterEventArgs("com", 8, eeg_data_float));


            //File.AppendAllText(originalDataFile, Convert.ToDouble(eeg_data_float[0]).ToString() + " ");

            //Array.ConstrainedCopy(eeg_data_2th, 0+q*8, eeg_data, 0, 8);
            //校正数据
            for (int i = 0; i < 8; i++)
            {
                    //double temp = (Convert.ToDouble(eeg_data[i]));
                    double temp = (Convert.ToDouble(eeg_data_float[i]));
                //double temp = (Convert.ToDouble(eeg_data[i])) - g_correct[i] * 10;
                //if (temp > 32767)
                //        temp = 32767;
                //    if (temp < -32768)
                //        temp = -32768;
                //遍历i行buffer_index列数据，8行
                //eeg_data_buffer[i][buffer_index] = Convert.ToInt16(temp);
                eeg_data_buffer[i][buffer_index] = temp;
                save_data_buffer[buffer_save_index][i] = temp - g_correct[i] * 10;
            }

            ////去除50hz
            //for (int ch = 0; ch < 8; ch++)
            //{
            //    eeg_data_buffer[ch][buffer_index] = notchFilter.ProcessSample(eeg_data_buffer[ch][buffer_index]);
            //    save_data_buffer[buffer_save_index][ch] = notchFilter.ProcessSample(save_data_buffer[buffer_save_index][ch]);
            //}
           
            buffer_index++;
            buffer_save_index++;
            //eeg_data_buffer为8行50列时，buffer_index 的值限制在 0 到 49 ，取余数
            buffer_index %= BaoLength;

            //滤波
            //if (buffer_index == BaoLength - 1)
            //{
            //    //去基线
            //    for (int ch = 0; ch < 8; ch++)
            //    {
            //        double mean = eeg_data_buffer[ch].Select(x => (double)x).Average();
            //        for (int i = 0; i < eeg_data_buffer[ch].Length; i++)
            //            eeg_data_buffer_baseline[ch][i] = (short)(eeg_data_buffer[ch][i] - mean);
            //    }

            //    //小波分析
            //    for (int ch = 0; ch < 8; ch++)
            //    {
            //        eeg_data_buffer_WaveletDenoise[ch] = WaveletDenoise(eeg_data_buffer_baseline[ch].ToDouble());
            //    }

            //    // 频谱分析
            //    PerformSpectrumAnalysis();
            //}
            //if (buffer_index == 19)
            //{
            //    for (int j = 0; j < 20; j++)
            //    {
            //        for (int i = 0; i < 8; i++)
            //        {
            //            // Updates the Y value at index i将不同行的数据进行垂直偏移，以便在可视化时能够区分不同的行
            //            lineData[i].Append(g_index, eeg_data_buffer[i][j]);//.Update(i, Math.Sin(i * 0.1 + phase)+j);

            //        }
            //        g_index += 0.002;
            //    }

            //}
            //for (int i = 0; i < 8; i++)
            //{
            //    //lineData[i].Append(g_index, eeg_data_buffer[i][buffer_index] * 0.05 * g_scale  - i * 100);//.Update(i, Math.Sin(i * 0.1 + phase)+j);
            //    lineData[i].Append(g_index, eeg_data_buffer[i][buffer_index]);//.Update(i, Math.Sin(i * 0.1 + phase)+j);

            //}
            //NlogHelper.WriteInfoLog(eeg_data_buffer[0][buffer_index].ToString());
            //g_index += 0.002;

            for (int i = 0; i < 8; i++)
            {
                // Updates the Y value at index i将不同行的数据进行垂直偏移，以便在可视化时能够区分不同的行
                //lineData[i].Append(g_index, eeg_data_buffer[i][buffer_index] * 0.1 * g_scale + 2000 - i * 100);//.Update(i, Math.Sin(i * 0.1 + phase)+j);
                lineData[i].Append(g_index, eeg_data_buffer[i][buffer_index]-i*30000);//.Update(i, Math.Sin(i * 0.1 + phase)+j);
            }
            g_index += 0.002;

            //接收到50个8通道数据时
            // if (buffer_index == 49)
            // {
            //     for (int j = 0; j < 50; j++)
            //     {
            //         for (int i = 0; i < 8; i++)
            //         {
            //             // Updates the Y value at index i将不同行的数据进行垂直偏移，以便在可视化时能够区分不同的行
            //             lineData[i].Append(g_index, eeg_data_buffer[i][j] * 0.05 * g_scale + 2000 - i * 100);//.Update(i, Math.Sin(i * 0.1 + phase)+j);

            //         }
            //         g_index += 0.002;
            //     }

            // }
            //buffer_save_index为500行8列
            buffer_save_index %= 500;
                if (buffer_save_index == 499)
                {
                    save_data_buffer_all.Add(save_data_buffer);
                }
                //当前数据点与上次更新的数据差大于4x500个数据点或者当前数据少于WindowSizex500
                if (Convert.ToInt32(g_index) - g_old_ecg_time > 4 || Convert.ToInt32(g_index) < WindowSize)
                {
                    g_old_ecg_time = Convert.ToInt32(g_index);

                    if (Convert.ToInt32(g_index) < WindowSize)
                    {
                        g_old_ecg_time = Convert.ToInt32(WindowSize - 5);
                    }
                    this.Dispatcher.BeginInvoke((Action)delegate ()
                    {
                        sciChartSurface.XAxis.VisibleRange = ComputeXAxisRange(g_old_ecg_time);
                    });
                }
                this.Dispatcher.BeginInvoke((Action)delegate ()
                {
                    NumEEG.Text = Numeeg.ToString();
                });

            
            

        }
        int Numeeg = 0;
        void uav_control_CmdEvent(object sender, EcgEventArgs e)
        {
            Numeeg += 22;

            //if (Ecg_ProEvent != null)
            //    Ecg_ProEvent(this, new EcgEventArgs("com", 1, e.value));
            if (e.type == 0xf1)
            {
                udpdata.Send(e.value, e.value.Length);
                Buffer.BlockCopy(e.value, 4, eeg_data, 0, 16);
                //校正数据
                for (int i = 0; i < 8; i++)
                {
                    double temp = (Convert.ToDouble(eeg_data[i])) - g_correct[i] * 10;
                    if (temp > 32767)
                        temp = 32767;
                    if (temp < -32768)
                        temp = -32768;
                    //遍历i行buffer_index列数据，8行
                    eeg_data_buffer[i][buffer_index] = Convert.ToInt16(temp);
                    save_data_buffer[buffer_save_index][i] = temp - g_correct[i] * 10;
                }


                buffer_index++;
                buffer_save_index++;
                //eeg_data_buffer为8行50列时，buffer_index 的值限制在 0 到 49 ，取余数
                buffer_index %= 50;
                //接收到50个8通道数据时
                if (buffer_index == 49)
                {
                    for (int j = 0; j < 50; j++)
                    {
                        for (int i = 0; i < 8; i++)
                        {
                            // Updates the Y value at index i将不同行的数据进行垂直偏移，以便在可视化时能够区分不同的行
                            lineData[i].Append(g_index, eeg_data_buffer[i][j] * 0.1 * g_scale  - i * 100);//.Update(i, Math.Sin(i * 0.1 + phase)+j);

                        }
                        g_index += 0.002;
                    }

                }
                //buffer_save_index为500行8列
                buffer_save_index %= 500;
                if (buffer_save_index == 499)
                {
                    save_data_buffer_all.Add(save_data_buffer);

                }
                //当前数据点与上次更新的数据差大于4x500个数据点或者当前数据少于WindowSizex500
                if (Convert.ToInt32(g_index) - g_old_ecg_time > 4 || Convert.ToInt32(g_index) < WindowSize)
                {
                    g_old_ecg_time = Convert.ToInt32(g_index);

                    if (Convert.ToInt32(g_index) < WindowSize)
                    {
                        g_old_ecg_time = Convert.ToInt32(WindowSize - 5);
                    }
                    this.Dispatcher.BeginInvoke((Action)delegate ()
                    {
                        NumEEG.Text = Numeeg.ToString();
                        sciChartSurface.XAxis.VisibleRange = ComputeXAxisRange(g_old_ecg_time);
                    });
                }
                this.Dispatcher.BeginInvoke((Action)delegate ()
                {
                    NumEEG.Text = Numeeg.ToString();

                });

            }

        }

        private void channel_1_Checked(object sender, RoutedEventArgs e)
        {
            sciChartSurface.RenderableSeries[0].IsVisible = true;
        }

        private void channel_2_Checked(object sender, RoutedEventArgs e)
        {
            sciChartSurface.RenderableSeries[1].IsVisible = true;
        }

        private void channel_3_Checked(object sender, RoutedEventArgs e)
        {
            sciChartSurface.RenderableSeries[2].IsVisible = true;
        }

        private void channel_4_Checked(object sender, RoutedEventArgs e)
        {
            sciChartSurface.RenderableSeries[3].IsVisible = true;
        }

        private void channel_5_Checked(object sender, RoutedEventArgs e)
        {
            sciChartSurface.RenderableSeries[4].IsVisible = true;
        }

        private void channel_6_Checked(object sender, RoutedEventArgs e)
        {
            sciChartSurface.RenderableSeries[5].IsVisible = true;
        }

        private void channel_7_Checked(object sender, RoutedEventArgs e)
        {
            sciChartSurface.RenderableSeries[6].IsVisible = true;
        }

        private void channel_8_Checked(object sender, RoutedEventArgs e)
        {
            sciChartSurface.RenderableSeries[7].IsVisible = true;
        }
        private void channel_1_Unchecked(object sender, RoutedEventArgs e)
        {
            sciChartSurface.RenderableSeries[0].IsVisible = false;
        }

        private void channel_2_Unchecked(object sender, RoutedEventArgs e)
        {
            sciChartSurface.RenderableSeries[1].IsVisible = false;
        }

        private void channel_3_Unchecked(object sender, RoutedEventArgs e)
        {
            sciChartSurface.RenderableSeries[2].IsVisible = false;
        }

        private void channel_4_Unchecked(object sender, RoutedEventArgs e)
        {
            sciChartSurface.RenderableSeries[3].IsVisible = false;
        }

        private void channel_5_Unchecked(object sender, RoutedEventArgs e)
        {
            sciChartSurface.RenderableSeries[4].IsVisible = false;
        }

        private void channel_6_Unchecked(object sender, RoutedEventArgs e)
        {
            sciChartSurface.RenderableSeries[5].IsVisible = false;
        }

        private void channel_7_Unchecked(object sender, RoutedEventArgs e)
        {
            sciChartSurface.RenderableSeries[6].IsVisible = false;
        }

        private void channel_8_Unchecked(object sender, RoutedEventArgs e)
        {
            sciChartSurface.RenderableSeries[7].IsVisible = false;
        }

        public void button_save_ecg()
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Title = "保存文件";
            string date = "Record-" + DateTime.Now.Year.ToString("0000") + DateTime.Now.Month.ToString("00") + DateTime.Now.Day.ToString("00") + "-" + DateTime.Now.Hour.ToString("00") + "时" + DateTime.Now.Minute.ToString("00") + "分" + DateTime.Now.Second.ToString("00") + "秒";
            saveFileDialog.FileName = date + ".edf";
            saveFileDialog.DefaultExt = "edf";
            saveFileDialog.Filter = "EDF 文件 (*.edf)|*.edf|所有文件 (*.*)|*.*";

            if (saveFileDialog.ShowDialog() == true)
            {
                string filePath = saveFileDialog.FileName;

                // 初始化EDF文件
                WriteEdf_File_multifile(0, filePath, g_channels + 1, 500);

                unsafe
                {
                    for (int i = 0; i < save_data_buffer_all.Count; i++)
                    {
                        for (int k = 0; k < 500; k++)
                        {
                            fixed (double* p = save_data_buffer_all[i][k])
                            {

                                if (0 != WriteEdf_WriteData_multifile(0, p))
                                {
                                    // 写文件失败，可以在这里添加错误处理逻辑
                                    LogHelper.WriteErrorLog("原始数据写入文件失败！");
                                    NlogHelper.WriteErrorLog("原始数据写入文件失败！");
                                }
                            }
                        }
                    }
                    LogHelper.WriteInfoLog("原始数据保存成功");
                    NlogHelper.WriteInfoLog("原始数据保存成功");
                }

            }

        }

        public void ComboBox_amplitude(int scale)
        {
            if (scale == 0)
                g_scale = 0.1;
            if (scale == 1)
                g_scale = 0.5;
            if (scale == 2)
                g_scale = 1.0;
            if (scale == 3)
                g_scale = 5.0;
            if (scale == 4)
                g_scale = 10.0;
            if (scale == 5)
                g_scale = 100.0;

        }
        public bool Isclearplot_pro { get; set; }
        public void Clear_Plot()
        {
            Numeeg = 0;
            File.WriteAllText(originalDataFile, string.Empty);
            Isclearplot_pro = true;
            buffer_index = 0;
            buffer_save_index = 0;
            g_index = 0;
            g_old_ecg_time = 0;
            eeg_data_buffer.Clear();
            save_data_buffer.Clear();
            save_data_buffer_all.Clear();

            //sciChartSurface.XAxis.VisibleRange = new DoubleRange(0, 10);
            for (int i = 0; i < 8; i++)
            {
                lineData[i].Clear();
            }
            LogHelper.WriteInfoLog("数据清除成功");
            NlogHelper.WriteInfoLog("数据清除成功");
        }





        /// <summary>
        /// 对单通道信号进行简单的移动平均滤波
        /// </summary>
        /// <param name="signal">输入信号</param>
        /// <param name="windowSize">窗口大小</param>
        /// <returns>滤波后的信号</returns>
        public static double[] MovingAverageFilter(double[] signal, int windowSize = 5)
        {
            try
            {
                if (windowSize <= 1 || windowSize > signal.Length)
                    return signal;

                double[] filtered = new double[signal.Length];
                int halfWindow = windowSize / 2;

                // 处理边界
                for (int i = 0; i < halfWindow; i++)
                {
                    filtered[i] = signal[i];
                    filtered[signal.Length - 1 - i] = signal[signal.Length - 1 - i];
                }

                // 应用移动平均
                for (int i = halfWindow; i < signal.Length - halfWindow; i++)
                {
                    double sum = 0;
                    for (int j = -halfWindow; j <= halfWindow; j++)
                    {
                        sum += signal[i + j];
                    }
                    filtered[i] = sum / windowSize;
                }

                return filtered;
            }
            catch (Exception ex)
            {
                LogHelper.WriteErrorLog($"移动平均滤波失败: {ex.Message}");
                NlogHelper.WriteErrorLog($"移动平均滤波失败: {ex.Message}");
                return signal;
            }
        }

        /// <summary>
        /// 对单通道信号进行小波去噪（简化版本）
        /// </summary>
        /// <param name="signal">输入信号</param>
        /// <param name="waveletName">小波类型，如 "haar", "db4" 等</param>
        /// <param name="level">分解层数</param>
        /// <returns>去噪后的信号</returns>
        public static double[] WaveletDenoise(double[] signal, string waveletName = "db4", int level = 4)
        {
            try
            {
                // 简化的小波去噪实现 - 基于统计方法
                // 1. 计算信号的统计特性
                double mean = signal.Average();
                double variance = signal.Select(x => Math.Pow(x - mean, 2)).Average();
                double stdDev = Math.Sqrt(variance);

                // 2. 设置阈值
                double threshold = stdDev * Math.Sqrt(2 * Math.Log(signal.Length));

                // 3. 软阈值去噪
                double[] denoised = new double[signal.Length];
                for (int i = 0; i < signal.Length; i++)
                {
                    double value = signal[i] - mean; // 去均值
                    if (Math.Abs(value) < threshold)
                        denoised[i] = mean; // 小于阈值设为均值
                    else
                        denoised[i] = mean + Math.Sign(value) * (Math.Abs(value) - threshold);
                }

                return denoised;
            }
            catch (Exception ex)
            {
                // 如果处理失败，返回原始信号
                LogHelper.WriteErrorLog($"小波去噪失败: {ex.Message}");
                NlogHelper.WriteErrorLog($"小波去噪失败: {ex.Message}");
                return signal;
            }
        }

        /// <summary>
        /// 统计去噪方法
        /// </summary>
        /// <param name="signal">输入信号</param>
        /// <returns>去噪后的信号</returns>
        public static double[] StatisticalDenoise(double[] signal)
        {
            try
            {
                // 计算信号的统计特性
                double mean = signal.Average();
                double variance = signal.Select(x => Math.Pow(x - mean, 2)).Average();
                double stdDev = Math.Sqrt(variance);

                // 设置阈值
                double threshold = stdDev * Math.Sqrt(2 * Math.Log(signal.Length));

                // 软阈值去噪
                double[] denoised = new double[signal.Length];
                for (int i = 0; i < signal.Length; i++)
                {
                    double value = signal[i] - mean; // 去均值
                    if (Math.Abs(value) < threshold)
                        denoised[i] = mean; // 小于阈值设为均值
                    else
                        denoised[i] = mean + Math.Sign(value) * (Math.Abs(value) - threshold);
                }

                return denoised;
            }
            catch (Exception ex)
            {
                LogHelper.WriteErrorLog($"统计去噪失败: {ex.Message}");
                NlogHelper.WriteErrorLog($"统计去噪失败: {ex.Message}");
                return signal;
            }
        }

        /// <summary>
        /// 执行频谱分析 - 使用Power文件夹中的PowerSpectralDensityAnalyzer
        /// </summary>
        private void PerformSpectrumAnalysis()
        {
            try
            {
                // 对每个通道进行频谱分析
                for (int ch = 0; ch < 8; ch++)
                {
                    // 获取小波去噪后的数据
                    double[] signal = eeg_data_buffer_WaveletDenoise[ch];

                    // 使用Power文件夹中的PowerSpectralDensityAnalyzer进行频谱分析
                    var psdResult = psdAnalyzer.GetPSD(signal, fs, 1.0);


                    // 同时触发原有的Ecg_ProEvent事件，传递频谱分析结果
                    if (Ecg_ProEvent != null)
                    {
                        // 创建包含频谱分析结果的事件参数
                        var ecgEventArgs = new EcgPowerEventArgs("spectrum", ch, psdResult);
                        Ecg_ProEvent(this, ecgEventArgs);
                    }

                    // 输出频谱分析结果到日志
                    LogHelper.WriteInfoLog($"通道{ch + 1}频谱分析完成 - 总功率: {psdResult.TotalPower:F4}");
                    NlogHelper.WriteInfoLog($"通道{ch + 1}频谱分析完成 - 总功率: {psdResult.TotalPower:F4}");

                    //// 输出各频段功率信息
                    //foreach (var power in psdResult.AbsolutePower)
                    //{
                    //    NlogHelper.WriteInfoLog($"  频段{power.Key}: 绝对功率={power.Value:F4}, 相对功率={psdResult.RelativePower[power.Key]:F2}%");
                    //}
                }

            }
            catch (Exception ex)
            {
                LogHelper.WriteErrorLog($"频谱分析失败: {ex.Message}");
                NlogHelper.WriteErrorLog($"频谱分析失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取频谱分析结果
        /// </summary>
        /// <param name="channelIndex">通道索引</param>
        /// <returns>功率谱密度分析结果</returns>
        public PowerSpectralDensityAnalyzer.PSDResult GetSpectrumAnalysisResult(int channelIndex)
        {
            try
            {
                if (channelIndex >= 0 && channelIndex < 8)
                {
                    double[] signal = eeg_data_buffer_WaveletDenoise[channelIndex];
                    return psdAnalyzer.GetPSD(signal, fs, 1.0);
                }
                return null;
            }
            catch (Exception ex)
            {
                LogHelper.WriteErrorLog($"获取频谱分析结果失败: {ex.Message}");
                NlogHelper.WriteErrorLog($"获取频谱分析结果失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 获取所有通道的频谱分析结果
        /// </summary>
        /// <returns>所有通道的频谱分析结果</returns>
        public PowerSpectralDensityAnalyzer.PSDResult[] GetAllSpectrumAnalysisResults()
        {
            var results = new PowerSpectralDensityAnalyzer.PSDResult[8];

            for (int ch = 0; ch < 8; ch++)
            {
                results[ch] = GetSpectrumAnalysisResult(ch);
            }

            return results;
        }

        List<byte[]> packets = new List<byte[]>();

        // 添加定时器相关字段
        private System.Windows.Threading.DispatcherTimer timer;
        private bool isTimerRunning = false;

        // 初始化定时器
        private void InitializeTimer()
        {
            timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(2); // 每5秒执行一次，可以根据需要调整
            timer.Tick += Timer_Tick;
        }

        // 定时器事件处理
        private void Timer_Tick(object sender, EventArgs e)
        {
            // 执行Button_Click的逻辑
            ProcessDebugHexFiles();
            //for (int n = 0; n < packets.Count; n++)
            //{

            //    byte[] packet = packets[n];

            //    // 提取EEG数据（第3-26字节）
            //    byte[] eegData = new byte[24];

            //    Buffer.BlockCopy(packet, 2, eegData, 0, 24);
            //    // 处理EEG数据
            //    process_eegdata(eegData);
            //    Buffer.BlockCopy(eeg_data_byte_8, 0, eeg_data, 0, 16);

            //    for (int i = 0; i < 8; i++)
            //    {
            //        double temp = (Convert.ToDouble(eeg_data[i])) - g_correct[i] * 10;
            //        if (temp > 32767)
            //            temp = 32767;
            //        if (temp < -32768)
            //            temp = -32768;
            //        //遍历i行buffer_index列数据，8行
            //        eeg_data_buffer[i][buffer_index] = Convert.ToInt16(temp);

            //    }
            //    buffer_index++;
            //    buffer_index %= BaoLength;
            //    //滤波
            //    if (buffer_index == BaoLength - 1)
            //    {
            //        //去基线
            //        for (int ch = 0; ch < 8; ch++)
            //        {
            //            double mean = eeg_data_buffer[ch].Select(x => (double)x).Average();
            //            for (int i = 0; i < eeg_data_buffer[ch].Length; i++)
            //                eeg_data_buffer_baseline[ch][i] = (short)(eeg_data_buffer[ch][i] - mean);
            //        }

            //        //小波分析
            //        for (int ch = 0; ch < 8; ch++)
            //        {
            //            eeg_data_buffer_WaveletDenoise[ch] = WaveletDenoise(eeg_data_buffer_baseline[ch].ToDouble());
            //        }

            //        // 频谱分析
            //        PerformSpectrumAnalysis();
            //    }

            //    for (int m = 0; m < 8; m++)
            //    {
            //        lineData[m].Append(g_index, eeg_data_buffer[m][buffer_index] * 0.5 * g_scale + 2000 - m * 100);
            //    }
            //    g_index += 0.002;
            //}
            //for (int n = 0; n < packets.Count; n++)
            //{

            //    byte[] packet = packets[n];

            //    // 提取EEG数据（第3-26字节）
            //    byte[] eegData = new byte[24];

            //    Buffer.BlockCopy(packet, 2, eegData, 0, 24);
            //    // 处理EEG数据
            //    process_eegdata(eegData);
            //    Buffer.BlockCopy(eeg_data_byte_8, 0, eeg_data, 0, 16);

            //    if (Ecg_FilterEvent != null)
            //    {
            //        Ecg_FilterEvent(this, new EcgFilterEventArgs("eeg", 0, eeg_data));
            //    }

            //    for (int i = 0; i < 8; i++)
            //    {
            //        double temp = Convert.ToDouble(eeg_data[i]) - g_correct[i] * 10;
            //        if (temp > 32767)
            //            temp = 32767;
            //        if (temp < -32768)
            //            temp = -32768;
            //        //遍历i行buffer_index列数据，8行
            //        eeg_data_buffer[i][buffer_index] = Convert.ToInt16(temp);

            //    }
            //    buffer_index++;
            //    buffer_index %= BaoLength;
            //    //滤波
            //    // if (buffer_index == BaoLength - 1)
            //    // {
            //    //     //去基线
            //    //     for (int ch = 0; ch < 8; ch++)
            //    //     {
            //    //         double mean = eeg_data_buffer[ch].Select(x => (double)x).Average();
            //    //         for (int i = 0; i < eeg_data_buffer[ch].Length; i++)
            //    //             eeg_data_buffer_baseline[ch][i] = (short)(eeg_data_buffer[ch][i] - mean);
            //    //     }

            //    //     //小波分析
            //    //     for (int ch = 0; ch < 8; ch++)
            //    //     {
            //    //         eeg_data_buffer_WaveletDenoise[ch] = WaveletDenoise(eeg_data_buffer_baseline[ch].ToDouble());
            //    //     }

            //    //     // 频谱分析
            //    //     PerformSpectrumAnalysis();
            //    // }

            //    for (int m = 0; m < 8; m++)
            //    {
            //        lineData[m].Append(g_index, eeg_data_buffer[m][buffer_index] * 0.5 * g_scale + 2000 - m * 100);
            //    }
            //    g_index += 0.002;
            //}

            //当前数据点与上次更新的数据差大于4x500个数据点或者当前数据少于WindowSizex500
            if (Convert.ToInt32(g_index) - g_old_ecg_time > 4 || Convert.ToInt32(g_index) < WindowSize)
            {
                g_old_ecg_time = Convert.ToInt32(g_index);

                if (Convert.ToInt32(g_index) < WindowSize)
                {
                    g_old_ecg_time = Convert.ToInt32(WindowSize - 5);
                }
                this.Dispatcher.BeginInvoke((Action)delegate ()
                {
                    sciChartSurface.XAxis.VisibleRange = ComputeXAxisRange(g_old_ecg_time);
                });
            }
            this.Dispatcher.BeginInvoke((Action)delegate ()
            {
                NumEEG.Text = Numeeg.ToString();
            });
        }

        // 启动定时器
        public void StartTimer()
        {
            if (timer == null)
            {
                InitializeTimer();
            }

            if (!isTimerRunning)
            {
                timer.Start();
                isTimerRunning = true;
                LogHelper.WriteInfoLog("定时器已启动");
                NlogHelper.WriteInfoLog("定时器已启动");
            }
        }

        // 停止定时器
        public void StopTimer()
        {
            if (timer != null && isTimerRunning)
            {
                timer.Stop();
                isTimerRunning = false;
                LogHelper.WriteInfoLog("定时器已停止");
                NlogHelper.WriteInfoLog("定时器已停止");
            }
        }

        // 设置定时器间隔
        public void SetTimerInterval(TimeSpan interval)
        {
            if (timer != null)
            {
                timer.Interval = interval;
                LogHelper.WriteInfoLog($"定时器间隔已设置为: {interval.TotalSeconds}秒");
                NlogHelper.WriteInfoLog($"定时器间隔已设置为: {interval.TotalSeconds}秒");
            }
        }

        //private void Button_Click(object sender, RoutedEventArgs e)
        //{
        //    ProcessDebugHexFiles();
        //    //for (int n = 0; n < packets.Count; n++)
        //    //{

        //    //    byte[] packet = packets[n];

        //    //    // 提取EEG数据（第3-26字节）
        //    //    byte[] eegData = new byte[24];

        //    //    Buffer.BlockCopy(packet, 2, eegData, 0, 24);
        //    //    // 处理EEG数据
        //    //    process_eegdata(eegData);
        //    //    Buffer.BlockCopy(eeg_data_byte_8, 0, eeg_data, 0, 16);

        //    //    if (Ecg_FilterEvent != null)
        //    //    {
        //    //        Ecg_FilterEvent(this, new EcgFilterEventArgs("eeg", 0, eeg_data));
        //    //    }

        //    //    for (int i = 0; i < 8; i++)
        //    //    {
        //    //        double temp = Convert.ToDouble(eeg_data[i]) - g_correct[i] * 10;
        //    //        if (temp > 32767)
        //    //            temp = 32767;
        //    //        if (temp < -32768)
        //    //            temp = -32768;
        //    //        //遍历i行buffer_index列数据，8行
        //    //        eeg_data_buffer[i][buffer_index] = Convert.ToInt16(temp);

        //    //    }
        //    //    buffer_index++;
        //    //    buffer_index %= BaoLength;
        //    //    //滤波
        //    //    // if (buffer_index == BaoLength - 1)
        //    //    // {
        //    //    //     //去基线
        //    //    //     for (int ch = 0; ch < 8; ch++)
        //    //    //     {
        //    //    //         double mean = eeg_data_buffer[ch].Select(x => (double)x).Average();
        //    //    //         for (int i = 0; i < eeg_data_buffer[ch].Length; i++)
        //    //    //             eeg_data_buffer_baseline[ch][i] = (short)(eeg_data_buffer[ch][i] - mean);
        //    //    //     }

        //    //    //     //小波分析
        //    //    //     for (int ch = 0; ch < 8; ch++)
        //    //    //     {
        //    //    //         eeg_data_buffer_WaveletDenoise[ch] = WaveletDenoise(eeg_data_buffer_baseline[ch].ToDouble());
        //    //    //     }

        //    //    //     // 频谱分析
        //    //    //     PerformSpectrumAnalysis();
        //    //    // }

        //    //    for (int m = 0; m < 8; m++)
        //    //    {
        //    //        lineData[m].Append(g_index, eeg_data_buffer[m][buffer_index] * 0.5 * g_scale + 2000 - m * 100);
        //    //    }
        //    //    g_index += 0.002;
        //    //}

        //    //当前数据点与上次更新的数据差大于4x500个数据点或者当前数据少于WindowSizex500
        //    if (Convert.ToInt32(g_index) - g_old_ecg_time > 4 || Convert.ToInt32(g_index) < WindowSize)
        //    {
        //        g_old_ecg_time = Convert.ToInt32(g_index);

        //        if (Convert.ToInt32(g_index) < WindowSize)
        //        {
        //            g_old_ecg_time = Convert.ToInt32(WindowSize - 5);
        //        }
        //        this.Dispatcher.BeginInvoke((Action)delegate ()
        //        {
        //            sciChartSurface.XAxis.VisibleRange = ComputeXAxisRange(g_old_ecg_time);
        //        });
        //    }
        //    this.Dispatcher.BeginInvoke((Action)delegate ()
        //    {
        //        NumEEG.Text = Numeeg.ToString();
        //    });
        //    //// 切换定时器状态
        //    //if (isTimerRunning)
        //    //{
        //    //    StopTimer();
        //    //    // 可以在这里更新按钮文本，比如从"停止"改为"开始"
        //    //    if (sender is Button button)
        //    //    {
        //    //        button.Content = "开始定时处理";
        //    //    }
        //    //}
        //    //else
        //    //{
        //    //    StartTimer();
        //    //    // 可以在这里更新按钮文本，比如从"开始"改为"停止"
        //    //    if (sender is Button button)
        //    //    {
        //    //        button.Content = "停止定时处理";
        //    //    }
        //    //}
        //}
        public void ProcessDebugHexFiles()
        {
            // 查找debug目录下的txt文件
            string debugPath = System.IO.Path.Combine(Directory.GetCurrentDirectory());
            if (!Directory.Exists(debugPath))
            {
                Console.WriteLine($"Debug目录不存在: {debugPath}");
                return;
            }

            string[] txtFiles = Directory.GetFiles(debugPath, "*.txt");
            if (txtFiles.Length == 0)
            {
                Console.WriteLine("Debug目录下没有找到txt文件");
                return;
            }

            HexDataProcessor processor = new HexDataProcessor();

            foreach (string filePath in txtFiles)
            {
                //Console.WriteLine($"\n处理文件: {Path.GetFileName(filePath)}");

                // 处理16进制文本文件
                packets = processor.ProcessHexFile(filePath);


            }

        }

        //private void btn_sincewave_Click(object sender, RoutedEventArgs e)
        //{
        //    freq1.Text = "50";
        //    freq2.Text = "150";
        //    SinceWave sinceWave1 = new SinceWave(Convert.ToDouble( freq1.Text));
        //    SinceWave sinceWave2 = new SinceWave(Convert.ToDouble(freq2.Text));
        //    double[] waveform1 = sinceWave1.GenerateWaveform(1000);
        //    double[] waveform2 = sinceWave2.GenerateWaveform(1000);
        //    double[] waveform = waveform1.Zip(waveform2, (a, b) => a + b).ToArray();
        //    //List<Double> doubles = new List<Double>();
        //    //doubles.AddRange(waveform1);
        //    //doubles.AddRange(waveform2);
        //    for (int i = 0; i < waveform.Length; i++)
        //    {
        //        lineData[0].Append(i, waveform[i]);
        //    }
        //}

        //private void btn_clear_Click(object sender, RoutedEventArgs e)
        //{
        //    lineData[0].Clear();
        //}

        private void channel_1_8_Checked(object sender, RoutedEventArgs e)
        {
            channel_1.IsChecked = true;
            channel_2.IsChecked = true;
            channel_3.IsChecked = true;
            channel_4.IsChecked = true;
            channel_5.IsChecked = true;
            channel_6.IsChecked = true;
            channel_7.IsChecked = true;
            channel_8.IsChecked = true;
            sciChartSurface.RenderableSeries[0].IsVisible = true;
            sciChartSurface.RenderableSeries[1].IsVisible = true;
            sciChartSurface.RenderableSeries[2].IsVisible = true;
            sciChartSurface.RenderableSeries[3].IsVisible = true;
            sciChartSurface.RenderableSeries[4].IsVisible = true;
            sciChartSurface.RenderableSeries[5].IsVisible = true;
            sciChartSurface.RenderableSeries[6].IsVisible = true;
            sciChartSurface.RenderableSeries[7].IsVisible = true;
        }

        private void channel_1_8_Unchecked(object sender, RoutedEventArgs e)
        {
            channel_1.IsChecked = false;
            channel_2.IsChecked = false;
            channel_3.IsChecked = false;
            channel_4.IsChecked = false;
            channel_5.IsChecked = false;
            channel_6.IsChecked = false;
            channel_7.IsChecked = false;
            channel_8.IsChecked = false;
            sciChartSurface.RenderableSeries[0].IsVisible = false;
            sciChartSurface.RenderableSeries[1].IsVisible = false;
            sciChartSurface.RenderableSeries[2].IsVisible = false;
            sciChartSurface.RenderableSeries[3].IsVisible = false;
            sciChartSurface.RenderableSeries[4].IsVisible = false;
            sciChartSurface.RenderableSeries[5].IsVisible = false;
            sciChartSurface.RenderableSeries[6].IsVisible = false;
            sciChartSurface.RenderableSeries[7].IsVisible = false;
        }
    }
    public class EcgPowerEventArgs : EventArgs
    {
        //命令与值
        //1、表示原始数据 0x80
        //2、代表算好的心率 0x03
        //3、表示信号质量 0x02
        public EcgPowerEventArgs(string com, int ch, PSDResult psdresult)
        {
            this.com = com;
            this.ch = ch;
            this.psdResult = psdresult;
        }
        public string com;
        public int ch;
        public PSDResult psdResult;
    }
    public class EcgFilterEventArgs : EventArgs
    {
        //命令与值
        //1、表示原始数据 0x80
        //2、代表算好的心率 0x03
        //3、表示信号质量 0x02
        public EcgFilterEventArgs(string com, int ch, float[] eeg_data)
        {
            this.com = com;
            this.ch = ch;
            this.eeg_data = eeg_data;
        }

        public string com;
        public int ch;
        public float[] eeg_data;
    }
}
