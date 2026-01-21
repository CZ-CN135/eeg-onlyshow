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
using NWaves.Utils;
using OfficeOpenXml;
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
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using Xceed.Wpf.Toolkit;
using static Collect.Power.PowerSpectralDensityAnalyzer;
//using Collect.FIR;

namespace Collect.Plot
{
    /// <summary>
    /// EEG.xaml 的交互逻辑
    /// </summary>
    public partial class EEG : UserControl
    {
        // 调用外部dll
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

        public TCPClient client = new TCPClient();
        SerialControl ecg_control = new SerialControl();

        private const int CN = 8;
        public int PGA = 1;

        private const int NL = 5;
        private readonly double[] NUM = new double[]
        {
            0.9480807851293, -3.070235355059, 4.381800263529, -3.070235355059, 0.9480807851293
        };

        private const int DL = 5;
        private readonly double[] DEN = new double[]
        {
            1, -3.152118327708, 4.379102839633, -2.98835238241, 0.8988589941553
        };

        // 二维动态数组
        private readonly double[][] buffer_in = new double[CN][];
        private readonly double[][] buffer_out = new double[CN][];

        private short g_ecg_data = 0;
        private int g_old_ecg_time = 0;
        private double g_scale = 1;

        private string localIp = "127.0.0.1";
        private int localPort = 45555;
        private string remoteIp = "127.0.0.1";
        private int remotePort = 45552;

        private UdpClient udpsti = new UdpClient();
        private IPEndPoint remoteIpep;
        private UdpClient udpdata = new UdpClient();

        public EEG()
        {
            // 8行5列数组初始化
            for (int i = 0; i < CN; i++)
            {
                buffer_in[i] = new double[NL];
                buffer_out[i] = new double[DL];
            }

            // buffer 初始化为0（其实 new 后默认就是 0，这里保留不动）
            for (int i = 0; i < CN; i++)
            {
                for (int j = 0; j < NL; j++)
                    buffer_in[i][j] = 0;

                for (int j = 0; j < DL; j++)
                    buffer_out[i][j] = 0;
            }

            DispatcherHelper.Initialize();
            InitializeComponent();
            CreateChart();

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
                udpdata.Connect("127.0.0.1", localPort);
                IPAddress ip = IPAddress.Parse("127.0.0.1");
                IPEndPoint remoteIpep = new IPEndPoint(ip, remotePort);
                udpsti = new UdpClient(remoteIpep);
            }
            catch (Exception)
            {
                // MessageBox.Show("错误", "请检查网络");
            }

            ResetFilterState(8);
        }

        private XyDataSeries<double, double>[] lineData;
        private double g_index = 0;
        private int channel_num = 8;

        private void CreateChart()
        {
            var xAxis = new NumericAxis
            {
                AxisTitle = "Time (second)",
                VisibleRange = new DoubleRange(0, 2000)
            };

            var yAxis = new NumericAxis
            {
                AxisTitle = "Value",
                Visibility = Visibility.Visible,
                VisibleRange = new DoubleRange(-1, 1)
            };

            sciChartSurface.XAxis = xAxis;
            sciChartSurface.YAxis = yAxis;

            lineData = new XyDataSeries<double, double>[8];

            for (int i = 0; i < 8; i++)
            {
                lineData[i] = new XyDataSeries<double, double> { FifoCapacity = 5000 };
            }

            for (int i = 0; i < 8; i++)
            {
                eeg_data_buffer[i] = new double[BaoLength];
                eeg_data_buffer_baseline[i] = new short[BaoLength];
            }

            for (int i = 0; i < 500; i++)
            {
                save_data_buffer[i] = new double[8];
                save_data_buffer_original[i] = new double[8];
            }

            

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
                Colors.Brown
            };

            for (int i = 0; i < channel_num; i++)
            {
                var lineSeries = new FastLineRenderableSeries
                {
                    Stroke = colors[i],
                    StrokeThickness = 1,
                    AntiAliasing = true,
                };
                sciChartSurface.RenderableSeries.Add(lineSeries);
            }

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
                return new DoubleRange(0, WindowSize);

            return new DoubleRange(Math.Ceiling(t) - WindowSize + 5, Math.Ceiling(t) + 5);
        }

        // tcp开始停止按钮
        public bool TCP_Install_ecg(string btn_con, string ip, int port)
        {
            if (btn_con == "开始")
            {
                bool is_open = client.Start(ip, port);
                if (!is_open) return false;

                StartTimer();
                client.EcgEvent += new EcgTCPEventHandler(uav_control_CmdEvent);
                StartTempRecordingIfNeeded();
                return true;
            }
            else
            {
                client.Stop();
                StopTimer();
                client.EcgEvent -= uav_control_CmdEvent;
                StopTempRecording();
                //WriteEdf_Finish_multifile(0);
                return false;
            }
        }

        private int buffer_index = 0;
        private readonly double[][] save_data_buffer = new double[500][];
        private readonly double[][] save_data_buffer_original = new double[500][];
        private readonly List<double[][]> save_data_buffer_all = new List<double[][]>();
        private readonly List<double[][]> save_data_buffer_all_original = new List<double[][]>();

        // ======= 无限时长录制：边采集边写NS2（避免内存无限增长）=======
        private Ns2StreamWriter _recFiltered;
        private Ns2StreamWriter _recOriginal;
        private string _tempFilteredPath;
        private string _tempOriginalPath;

        private readonly short[] _rowFiltered = new short[8];
        private readonly short[] _rowOriginal = new short[8];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static short ClampToInt16Uv(double uv)
        {
            int v = (int)Math.Round(uv);
            if (v > short.MaxValue) return short.MaxValue;
            if (v < short.MinValue) return short.MinValue;
            return (short)v;
        }

        private string GetRecordDir()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "EEG_Records");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private void StartTempRecordingIfNeeded()
        {
            if (_recFiltered != null || _recOriginal != null) return;

            string ts = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string dir = GetRecordDir();

            _tempFilteredPath = Path.Combine(dir, $"Record-{ts}-filtered.tmp.ns2");
            _tempOriginalPath = Path.Combine(dir, $"Record-{ts}-original.tmp.ns2");

            int fsInt = (int)Math.Round(sampleRate <= 0 ? 1000 : sampleRate);

            _recFiltered = new Ns2StreamWriter(_tempFilteredPath, channelCount: 8, samplingRate: fsInt);
            _recOriginal = new Ns2StreamWriter(_tempOriginalPath, channelCount: 8, samplingRate: fsInt);
        }

        private void StopTempRecording()
        {
            try { _recFiltered?.Finish(); } catch { }
            try { _recOriginal?.Finish(); } catch { }

            _recFiltered = null;
            _recOriginal = null;
        }

        private readonly float[] eeg_data_float = new float[8];
        private readonly uint[] eeg_data_uint = new uint[8];
        private readonly byte[] eeg_data_byte = new byte[24];

        private int BaoLength = 50;

        private void process_eegdata(byte[] eeg_data_byte)
        {
            for (int i = 0; i < 8; i++)
            {
                eeg_data_uint[i] = Convert.ToUInt32(
                    (eeg_data_byte[0 + 3 * i] << 16) |
                    (eeg_data_byte[1 + 3 * i] << 8) |
                    eeg_data_byte[2 + 3 * i]);
            }

            for (int i = 0; i < 8; i++)
            {
                float datai;
                if ((eeg_data_uint[i] & 0x800000) != 0)
                {
                    datai = Convert.ToSingle((16777216 - eeg_data_uint[i]) * (-4500000.0) / (8388607 * PGA));
                }
                else
                {
                    datai = Convert.ToSingle((eeg_data_uint[i] * 4500000.0) / (8388607 * PGA));
                }

                eeg_data_float[i] = datai;
            }
        }

        public double fs; // 采样率Hz，根据实际情况修改

        private int buffer_save_index = 0;
        private readonly double[][] eeg_data_buffer = new double[8][];
        private readonly short[][] eeg_data_buffer_baseline = new short[8][];
        private readonly double[][] eeg_data_buffer_WaveletDenoise = new double[8][];

        /// <summary>
        /// 修改滤波顺序
        /// </summary>
        /// <param name="ch"></param>
        /// <param name="x"></param>
        /// <returns></returns>
        public double sampleRate = 1000;

        public double hpCut = 0.3;   // 高通截止 0.5 Hz
        public double hpA;

        public double lpCut = 40.0;  // 低通截止 50 Hz
        // 4阶 Butterworth 两段二阶的 Q（固定值）
        public double lpQ1 = 0.5411961;
        public double lpQ2 = 1.3065630;

        public double notchF0 = 50.0;
        public double notchQ = 20.0;  // 约等于 BW=2Hz 的量级（可按需要微调）

        // ===== 状态（8通道）=====
        double[] hp1_prevX = new double[8];
        double[] hp1_prevY = new double[8];
        double[] hp2_prevX = new double[8];
        double[] hp2_prevY = new double[8];

        double[][] medBuf = Enumerable.Range(0, 8).Select(_ => new double[5]).ToArray();
        private readonly double[][] medTmp = Enumerable.Range(0, 8).Select(_ => new double[5]).ToArray();
        int[] medCount = new int[8];
        int[] medIdx = new int[8];

        BiquadLPF[] lpf1;
        BiquadLPF[] lpf2;
        BiquadNotch[] notch1;
        BiquadNotch[] notch2;

        public void set_filter_params(double fs)
        {
            sampleRate = fs;
            hpA = Math.Exp(-2.0 * Math.PI * hpCut / sampleRate);
            lpf1 = Enumerable.Range(0, 8).Select(_ => new BiquadLPF(sampleRate, lpCut, lpQ1)).ToArray();
            lpf2 = Enumerable.Range(0, 8).Select(_ => new BiquadLPF(sampleRate, lpCut, lpQ2)).ToArray();
        }

        private void ResetFilterState(int chCount)
        {
            Array.Clear(hp1_prevX, 0, hp1_prevX.Length);
            Array.Clear(hp1_prevY, 0, hp1_prevY.Length);
            Array.Clear(hp2_prevX, 0, hp2_prevX.Length);
            Array.Clear(hp2_prevY, 0, hp2_prevY.Length);

            for (int ch = 0; ch < chCount; ch++)
            {
                Array.Clear(medBuf[ch], 0, medBuf[ch].Length);
                medCount[ch] = 0;
                medIdx[ch] = 0;
            }

            notch1 = Enumerable.Range(0, 8).Select(_ => new BiquadNotch(sampleRate, notchF0, notchQ)).ToArray();
            notch2 = Enumerable.Range(0, 8).Select(_ => new BiquadNotch(sampleRate, notchF0, notchQ)).ToArray();
        }

        private double Median5_Update(int ch, double x)
        {
            var buf = medBuf[ch];
            buf[medIdx[ch]] = x;
            medIdx[ch] = (medIdx[ch] + 1) % 5;
            if (medCount[ch] < 5) medCount[ch]++;

            int n = medCount[ch];
            if (n <= 1) return x;

            var w = medTmp[ch];
            w[0] = buf[0];
            w[1] = buf[1];
            w[2] = buf[2];
            w[3] = buf[3];
            w[4] = buf[4];

            for (int i = 1; i < n; i++)
            {
                double key = w[i];
                int j = i - 1;
                while (j >= 0 && w[j] > key)
                {
                    w[j + 1] = w[j];
                    j--;
                }
                w[j + 1] = key;
            }

            return w[n / 2];
        }

        // !!! 你原始粘贴里有一段重复的“w[j + 1] = key ... return ...”在方法外面
        // !!! 这会导致编译错误：请直接删除它（这里只是提醒，未保留那段垃圾代码）

        // ===== 癫痫检测相关 =====
        private SeizureDetector _detector;
        private BandPowerRatioDetector _stage2;

        public void InitAfterFsKnown(double fs)
        {
            _detector = new SeizureDetector(new SeizureDetector.Config
            {
                Fs = sampleRate,
                ChannelCount = 8,
                WindowMs = 200,
                StepMs = 50,
                WarmupMs = 1000,
                RmsThreshold = 80,
                LlThreshold = 2000,
                MinChannelsToTrigger = 1,
                StopAfterTrigger = false,
                Stage2LookbackMs = 400,
                Stage2WindowMs = 600,
                HistoryMs = 2000,
                Stage2EmitMinIntervalMs = 100
            });
            _detector.Start();

            _stage2 = new BandPowerRatioDetector(new BandPowerRatioDetector.Config
            {
                Fs = sampleRate,
                ChannelCount = 8,
                NumBandLow = 8,
                NumBandHigh = 13,
                DenBandLow = 0.5,
                DenBandHigh = 4,
                RatioThreshold = 2.0,
                MinChannelsToTrigger = 1
            });
            _stage2.Start();

            _detector.OnStage2WindowReady += (s, e) =>
            {
                _stage2.PushWindow(e.Window, e.WindowStartSample, e.WindowEndSample);
            };

            _stage2.OnStage2Triggered += (s, e) =>
            {
                // UI 更新要用 Dispatcher
            };
        }

        private bool _detectorInited = false;
        private int Numeeg = 0;

        private void uav_control_CmdEvent(object sender, EcgTCPEventArgs e)
        {
            Numeeg += 33;

            Buffer.BlockCopy(e.value, 2, eeg_data_byte, 0, 24);
            process_eegdata(eeg_data_byte);

        

            for (int i = 0; i < 8; i++)
            {
                double temp = Convert.ToDouble(eeg_data_float[i]);

                // 1) 一阶高通
                double yhp1 = hpA * (hp1_prevY[i] + temp - hp1_prevX[i]);
                hp1_prevX[i] = temp;
                hp1_prevY[i] = yhp1;

                // 2) 二阶高通（再一级一阶）
                double yhp2 = hpA * (hp2_prevY[i] + yhp1 - hp2_prevX[i]);
                hp2_prevX[i] = yhp1;
                hp2_prevY[i] = yhp2;

                // 3) 双陷波
                double y1 = notch1[i].Process(yhp2);
                double y2 = notch2[i].Process(y1);

                // 4) 双低通
                double ylp1 = lpf1[i].Process(y2);
                double ylp2 = lpf2[i].Process(ylp1);

                // 5) 中值
                double filterdata = Median5_Update(i, ylp2);

                eeg_data_buffer[i][buffer_index] = filterdata;

                _rowFiltered[i] = ClampToInt16Uv(filterdata);
                _rowOriginal[i] = ClampToInt16Uv(yhp1);
            }

            _recFiltered?.WriteRow(_rowFiltered);
            _recOriginal?.WriteRow(_rowOriginal);

            buffer_index++;
            buffer_index %= BaoLength;

            for (int i = 0; i < 8; i++)
            {
                lineData[i].Append(g_index, eeg_data_buffer[i][buffer_index] - i * 10000);
            }

            g_index += 0.002;

            if (Convert.ToInt32(g_index) - g_old_ecg_time > 4 || Convert.ToInt32(g_index) < WindowSize)
            {
                g_old_ecg_time = Convert.ToInt32(g_index);

                if (Convert.ToInt32(g_index) < WindowSize)
                    g_old_ecg_time = Convert.ToInt32(WindowSize - 5);

                Dispatcher.BeginInvoke((Action)delegate
                {
                    sciChartSurface.XAxis.VisibleRange = ComputeXAxisRange(g_old_ecg_time);
                });
            }
        }

        // ===== UI 勾选显示 =====
        private void channel_1_Checked(object sender, RoutedEventArgs e) => sciChartSurface.RenderableSeries[0].IsVisible = true;
        private void channel_2_Checked(object sender, RoutedEventArgs e) => sciChartSurface.RenderableSeries[1].IsVisible = true;
        private void channel_3_Checked(object sender, RoutedEventArgs e) => sciChartSurface.RenderableSeries[2].IsVisible = true;
        private void channel_4_Checked(object sender, RoutedEventArgs e) => sciChartSurface.RenderableSeries[3].IsVisible = true;
        private void channel_5_Checked(object sender, RoutedEventArgs e) => sciChartSurface.RenderableSeries[4].IsVisible = true;
        private void channel_6_Checked(object sender, RoutedEventArgs e) => sciChartSurface.RenderableSeries[5].IsVisible = true;
        private void channel_7_Checked(object sender, RoutedEventArgs e) => sciChartSurface.RenderableSeries[6].IsVisible = true;
        private void channel_8_Checked(object sender, RoutedEventArgs e) => sciChartSurface.RenderableSeries[7].IsVisible = true;

        private void channel_1_Unchecked(object sender, RoutedEventArgs e) => sciChartSurface.RenderableSeries[0].IsVisible = false;
        private void channel_2_Unchecked(object sender, RoutedEventArgs e) => sciChartSurface.RenderableSeries[1].IsVisible = false;
        private void channel_3_Unchecked(object sender, RoutedEventArgs e) => sciChartSurface.RenderableSeries[2].IsVisible = false;
        private void channel_4_Unchecked(object sender, RoutedEventArgs e) => sciChartSurface.RenderableSeries[3].IsVisible = false;
        private void channel_5_Unchecked(object sender, RoutedEventArgs e) => sciChartSurface.RenderableSeries[4].IsVisible = false;
        private void channel_6_Unchecked(object sender, RoutedEventArgs e) => sciChartSurface.RenderableSeries[5].IsVisible = false;
        private void channel_7_Unchecked(object sender, RoutedEventArgs e) => sciChartSurface.RenderableSeries[6].IsVisible = false;
        private void channel_8_Unchecked(object sender, RoutedEventArgs e) => sciChartSurface.RenderableSeries[7].IsVisible = false;

        private void TryDeleteTempFile(ref string tempPath, string tag)
        {
            if (string.IsNullOrWhiteSpace(tempPath)) return;

            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);

                tempPath = null; // 删除后把路径清空，避免再次保存还指向旧文件
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"已保存成功，但删除临时{tag}文件失败：{ex.Message}\n临时文件位置：{tempPath}");
            }
        }

        // ===== 保存相关 =====
        public void button_save_ecg_filter_ns2()
        {
            if (string.IsNullOrWhiteSpace(_tempFilteredPath) || !File.Exists(_tempFilteredPath))
            {
                System.Windows.MessageBox.Show("没有找到可保存的滤波数据文件：请先开始采集并停止后再保存。");
                return;
            }

            // 如果还在采集中，文件句柄还开着，删除会失败；而且此时保存出来的也不是最终完整文件
            if (_recFiltered != null)
            {
                System.Windows.MessageBox.Show("建议先停止采集再保存（否则文件仍在写入，且无法删除临时文件）。");
                return;
            }

            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Title = "保存滤波EEG（NS2）";
            string date = "Record-" + DateTime.Now.ToString("yyyyMMdd-HH时mm分ss秒");
            saveFileDialog.FileName = date + ".ns2";
            saveFileDialog.DefaultExt = "ns2";
            saveFileDialog.Filter = "NS2 文件 (*.ns2)|*.ns2|所有文件 (*.*)|*.*";

            if (saveFileDialog.ShowDialog() != true) return;

            try
            {
                // 防止用户把目标路径选成同一个临时文件路径
                string dst = saveFileDialog.FileName;
                if (string.Equals(Path.GetFullPath(dst), Path.GetFullPath(_tempFilteredPath), StringComparison.OrdinalIgnoreCase))
                {
                    System.Windows.MessageBox.Show("目标路径与临时文件相同，请选择其它路径/文件名。");
                    return;
                }

                File.Copy(_tempFilteredPath, dst, overwrite: true);

                // 复制成功后删除临时文件
                TryDeleteTempFile(ref _tempFilteredPath, "滤波");

                System.Windows.MessageBox.Show("NS2 已保存成功，并已删除临时文件。");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"保存 NS2 文件时出错: {ex.Message}");
            }
        }


        private const int EXCEL_MAX_ROWS = 1000000;
        private const int EXCEL_HEADER_ROWS = 1;

        public void button_save_ecg_filter_excel()
        {
            System.Windows.MessageBox.Show("数据量无限制时，不建议保存为 Excel（行数与文件大小限制、写入极慢）。\n建议：保存为 NS2，然后在 MATLAB / Python 里按需截取时间段再导出表格。");
        }

        private ExcelWorksheet CreateNewSheet(ExcelPackage package, int sheetIndex, ref int currentRow)
        {
            string sheetName = $"EEG_Data_{sheetIndex}";
            var worksheet = package.Workbook.Worksheets.Add(sheetName);

            worksheet.Cells[1, 1].LoadFromArrays(new[]
            {
                new object[] { "Ch1", "Ch2", "Ch3", "Ch4", "Ch5", "Ch6", "Ch7", "Ch8" }
            });

            currentRow = EXCEL_HEADER_ROWS + 1;
            return worksheet;
        }

        public void button_save_ecg_original_ns2()
        {
            if (string.IsNullOrWhiteSpace(_tempOriginalPath) || !File.Exists(_tempOriginalPath))
            {
                System.Windows.MessageBox.Show("没有找到可保存的原始数据文件：请先开始采集并停止后再保存。");
                return;
            }

            if (_recOriginal != null)
            {
                System.Windows.MessageBox.Show("建议先停止采集再保存（否则文件仍在写入，且无法删除临时文件）。");
                return;
            }

            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Title = "保存原始EEG（NS2）";
            string date = "Record-original-" + DateTime.Now.ToString("yyyyMMdd-HH时mm分ss秒");
            saveFileDialog.FileName = date + ".ns2";
            saveFileDialog.DefaultExt = "ns2";
            saveFileDialog.Filter = "NS2 文件 (*.ns2)|*.ns2|所有文件 (*.*)|*.*";

            if (saveFileDialog.ShowDialog() != true) return;

            try
            {
                string dst = saveFileDialog.FileName;
                if (string.Equals(Path.GetFullPath(dst), Path.GetFullPath(_tempOriginalPath), StringComparison.OrdinalIgnoreCase))
                {
                    System.Windows.MessageBox.Show("目标路径与临时文件相同，请选择其它路径/文件名。");
                    return;
                }

                File.Copy(_tempOriginalPath, dst, overwrite: true);

                TryDeleteTempFile(ref _tempOriginalPath, "原始");

                System.Windows.MessageBox.Show("NS2 已保存成功，并已删除临时文件。");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"保存 NS2 文件时出错: {ex.Message}");
            }
        }


        public void button_save_ecg_original_excel()
        {
            System.Windows.MessageBox.Show("数据量无限制时，不建议保存为 Excel（行数与文件大小限制、写入极慢）。\n建议：保存为 NS2，然后在 MATLAB / Python 里按需截取时间段再导出表格。");
        }

        private ExcelWorksheet CreateNewOriginalSheet(ExcelPackage package, int sheetIndex, ref int currentRow)
        {
            string sheetName = $"EEG_Original_{sheetIndex}";
            var worksheet = package.Workbook.Worksheets.Add(sheetName);

            worksheet.Cells[1, 1].LoadFromArrays(new[]
            {
                new object[] { "Ch1", "Ch2", "Ch3", "Ch4", "Ch5", "Ch6", "Ch7", "Ch8" }
            });

            currentRow = EXCEL_HEADER_ROWS + 1;
            return worksheet;
        }

        public void ComboBox_amplitude(int scale)
        {
            if (scale == 0) g_scale = 0.1;
            if (scale == 1) g_scale = 0.5;
            if (scale == 2) g_scale = 1.0;
            if (scale == 3) g_scale = 5.0;
            if (scale == 4) g_scale = 10.0;
            if (scale == 5) g_scale = 100.0;
        }

        public bool Isclearplot_pro { get; set; }

        public void Clear_Plot()
        {
            StopTempRecording();

            Numeeg = 0;
            _sw.Reset();
            timer = null;
            EEG_time.Text = "00:00:00";
            Isclearplot_pro = true;

            buffer_index = 0;
            buffer_save_index = 0;
            g_index = 0;
            g_old_ecg_time = 0;

            eeg_data_buffer.Clear();
            save_data_buffer.Clear();
            save_data_buffer_original.Clear();
            save_data_buffer_all.Clear();
            save_data_buffer_all_original.Clear();

            for (int i = 0; i < 8; i++)
            {
                lineData[i].Clear();
            }

            LogHelper.WriteInfoLog("数据清除成功");
            NlogHelper.WriteInfoLog("数据清除成功");
        }

        // ===== 定时器相关 =====
        private System.Windows.Threading.DispatcherTimer timer;
        private bool isTimerRunning = false;
        private readonly System.Diagnostics.Stopwatch _sw = new System.Diagnostics.Stopwatch();

        private void InitializeTimer()
        {
            timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += Timer_Tick;
            _sw.Restart();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            EEG_time.Text = _sw.Elapsed.ToString(@"hh\:mm\:ss");
            NumEEG.Text = Numeeg.ToString();
        }

        public void StartTimer()
        {
            if (timer == null) InitializeTimer();

            if (!isTimerRunning)
            {
                timer.Start();
                _sw.Start();
                isTimerRunning = true;
                LogHelper.WriteInfoLog("定时器已启动");
                NlogHelper.WriteInfoLog("定时器已启动");
            }
        }

        public void StopTimer()
        {
            if (timer != null && isTimerRunning)
            {
                timer.Stop();
                _sw.Stop();
                isTimerRunning = false;
                LogHelper.WriteInfoLog("定时器已停止");
                NlogHelper.WriteInfoLog("定时器已停止");
            }
        }

        public void SetTimerInterval(TimeSpan interval)
        {
            if (timer != null)
            {
                timer.Interval = interval;
                LogHelper.WriteInfoLog($"定时器间隔已设置为: {interval.TotalSeconds}秒");
                NlogHelper.WriteInfoLog($"定时器间隔已设置为: {interval.TotalSeconds}秒");
            }
        }

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

            for (int i = 0; i < 8; i++)
                sciChartSurface.RenderableSeries[i].IsVisible = true;
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

            for (int i = 0; i < 8; i++)
                sciChartSurface.RenderableSeries[i].IsVisible = false;
        }
    }

    /// <summary>
    /// NS2 流式写入器（chunk 1秒写一次），停止时回填 DataPoints
    /// </summary>
    public sealed class Ns2StreamWriter : IDisposable
    {
        private readonly object _lock = new object();
        private readonly FileStream _fs;
        private readonly BinaryWriter _bw;

        private readonly int _channelCount;
        private readonly int _samplingRate;

        private readonly int _rowBytesLen;

        private readonly int _chunkSamples;
        private readonly byte[] _chunkBytes;
        private int _chunkFillSamples;
        private int _chunkFillBytes;

        private long _dataPointsPos;
        private ulong _samplesWritten;
        private bool _finished;

        public string FilePath { get; }

        public Ns2StreamWriter(string filePath, int channelCount, int samplingRate)
        {
            if (channelCount <= 0) throw new ArgumentOutOfRangeException(nameof(channelCount));
            if (samplingRate <= 0) throw new ArgumentOutOfRangeException(nameof(samplingRate));

            FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            _channelCount = channelCount;
            _samplingRate = samplingRate;

            _rowBytesLen = _channelCount * 2;

            _chunkSamples = _samplingRate;
            if (_chunkSamples <= 0) _chunkSamples = 1;

            _chunkBytes = new byte[_rowBytesLen * _chunkSamples];
            _chunkFillSamples = 0;
            _chunkFillBytes = 0;

            _fs = new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            _bw = new BinaryWriter(_fs, Encoding.ASCII);

            WriteHeader();
        }

        private void WriteHeader()
        {
            const int timeResolution = 30000;
            const short minAnalog = -1000;
            const short maxAnalog = 1000;

            _bw.Write(Encoding.ASCII.GetBytes("NEURALCD"));
            _bw.Write((byte)2);
            _bw.Write((byte)3);

            long headerPos = _bw.BaseStream.Position;
            _bw.Write((uint)0);

            _bw.Write(Encoding.ASCII.GetBytes("EEG DATA".PadRight(16, '\0')));
            _bw.Write(Encoding.ASCII.GetBytes("Created by EEG acquisition system".PadRight(256, '\0')));

            _bw.Write((uint)(timeResolution / _samplingRate));
            _bw.Write((uint)timeResolution);

            DateTime now = DateTime.Now;
            _bw.Write((ushort)now.Year);
            _bw.Write((ushort)now.Month);
            _bw.Write((ushort)now.Day);
            _bw.Write((ushort)now.Hour);
            _bw.Write((ushort)now.Minute);
            _bw.Write((ushort)now.Second);
            _bw.Write((ushort)0);
            _bw.Write((ushort)0);

            _bw.Write((uint)_channelCount);

            for (int ch = 0; ch < _channelCount; ch++)
            {
                _bw.Write(Encoding.ASCII.GetBytes("CC"));
                _bw.Write((ushort)(ch + 1));
                _bw.Write(Encoding.ASCII.GetBytes($"CH{ch + 1}".PadRight(16, '\0')));
                _bw.Write((byte)('A' + ch / 32));
                _bw.Write((byte)(ch % 32));
                _bw.Write((short)-32768);
                _bw.Write((short)32767);
                _bw.Write((short)minAnalog);
                _bw.Write((short)maxAnalog);
                _bw.Write(Encoding.ASCII.GetBytes("uV".PadRight(16, '\0')));
                _bw.Write((uint)0); _bw.Write((uint)0); _bw.Write((ushort)0);
                _bw.Write((uint)0); _bw.Write((uint)0); _bw.Write((ushort)0);
            }

            long headerEnd = _bw.BaseStream.Position;
            _bw.BaseStream.Seek(headerPos, SeekOrigin.Begin);
            _bw.Write((uint)headerEnd);
            _bw.BaseStream.Seek(headerEnd, SeekOrigin.Begin);

            _bw.Write((byte)1);
            _bw.Write((uint)0);
            _dataPointsPos = _bw.BaseStream.Position;
            _bw.Write((uint)0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void FlushChunk_NoLock(bool forceFlushToDisk)
        {
            if (_chunkFillBytes <= 0) return;

            _bw.Write(_chunkBytes, 0, _chunkFillBytes);
            _samplesWritten += (ulong)_chunkFillSamples;

            _chunkFillSamples = 0;
            _chunkFillBytes = 0;

            if (forceFlushToDisk) _bw.Flush();
        }

        public void WriteRow(short[] row)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));
            if (row.Length < _channelCount) throw new ArgumentException("row length < channelCount");

            lock (_lock)
            {
                if (_finished) return;

                Buffer.BlockCopy(row, 0, _chunkBytes, _chunkFillBytes, _rowBytesLen);
                _chunkFillBytes += _rowBytesLen;
                _chunkFillSamples++;

                if (_chunkFillSamples >= _chunkSamples)
                {
                    FlushChunk_NoLock(forceFlushToDisk: false);
                }
            }
        }

        public void Flush()
        {
            lock (_lock)
            {
                if (_finished) return;
                FlushChunk_NoLock(forceFlushToDisk: true);
            }
        }

        public void Finish()
        {
            lock (_lock)
            {
                if (_finished) return;

                FlushChunk_NoLock(forceFlushToDisk: true);

                long cur = _bw.BaseStream.Position;
                _bw.BaseStream.Seek(_dataPointsPos, SeekOrigin.Begin);

                uint dp = _samplesWritten >= uint.MaxValue ? uint.MaxValue : (uint)_samplesWritten;
                _bw.Write(dp);

                _bw.BaseStream.Seek(cur, SeekOrigin.Begin);
                _bw.Flush();

                _finished = true;
            }

            try { _bw.Dispose(); } catch { }
            try { _fs.Dispose(); } catch { }
        }

        public void Dispose() => Finish();
    }

    public sealed class BiquadNotch
    {
        private double b0, b1, b2, a1, a2;
        private double z1, z2;

        public BiquadNotch(double fs, double f0, double Q)
        {
            Update(fs, f0, Q);
        }

        public void Update(double fs, double f0, double Q)
        {
            double w0 = 2.0 * Math.PI * (f0 / fs);
            double cosw = Math.Cos(w0);
            double alpha = Math.Sin(w0) / (2.0 * Q);

            double B0 = 1.0;
            double B1 = -2.0 * cosw;
            double B2 = 1.0;
            double A0 = 1.0 + alpha;
            double A1 = -2.0 * cosw;
            double A2 = 1.0 - alpha;

            b0 = B0 / A0; b1 = B1 / A0; b2 = B2 / A0;
            a1 = A1 / A0; a2 = A2 / A0;

            z1 = z2 = 0.0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double Process(double x)
        {
            double y = b0 * x + z1;
            z1 = b1 * x - a1 * y + z2;
            z2 = b2 * x - a2 * y;
            return y;
        }
    }

    public sealed class BiquadLPF
    {
        private double b0, b1, b2, a1, a2;
        private double z1, z2;

        public BiquadLPF(double fs, double fc, double Q) => Update(fs, fc, Q);

        public void Update(double fs, double fc, double Q)
        {
            double w0 = 2.0 * Math.PI * (fc / fs);
            double cosw = Math.Cos(w0);
            double sinw = Math.Sin(w0);
            double alpha = sinw / (2.0 * Q);

            double B0 = (1 - cosw) / 2.0;
            double B1 = 1 - cosw;
            double B2 = (1 - cosw) / 2.0;
            double A0 = 1 + alpha;
            double A1 = -2 * cosw;
            double A2 = 1 - alpha;

            b0 = B0 / A0; b1 = B1 / A0; b2 = B2 / A0;
            a1 = A1 / A0; a2 = A2 / A0;

            z1 = z2 = 0.0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double Process(double x)
        {
            double y = b0 * x + z1;
            z1 = b1 * x - a1 * y + z2;
            z2 = b2 * x - a2 * y;
            return y;
        }
    }
}
