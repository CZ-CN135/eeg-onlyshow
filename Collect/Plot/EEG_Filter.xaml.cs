using Collect.FIR;
using Collect.tool;
using MathNet.Filtering;
using MathNet.Filtering.FIR;
using MathNet.Filtering.IIR;
using Microsoft.Win32;
using NWaves.Audio;
using NWaves.Filters;
using NWaves.Filters.Base;
using NWaves.Filters.BiQuad;
using NWaves.Filters.Butterworth;
using NWaves.Filters.ChebyshevI;
using OfficeOpenXml;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals;
using SciChart.Charting.Visuals.Axes;
using SciChart.Charting.Visuals.RenderableSeries;
using SciChart.Data.Model;
using ScottPlot;
using ScottPlot.ArrowShapes;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Colors = System.Windows.Media.Colors;

namespace Collect.Plot
{
    /// <summary>
    /// EEG_Filter.xaml 的交互逻辑
    /// </summary>
    public partial class EEG_Filter : UserControl
    {
        private XyDataSeries<double, double>[] lineData;

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

        public bool IsFilter = false;
        public int Freq = 1000;
        public int Order = 4;
        public int EndFreq = 50;
        public EEG _eeg;
        public double[] firCoefficients;
        public FIRFilter filter;

        public EEG_Filter(EEG eeg)
        {
            _eeg=eeg;
            InitializeComponent();
           

            ch.Items.Add(new ComboBoxItem
            {
                Content = "1"
            });
            ch.Items.Add(new ComboBoxItem
            {
                Content = "2"
            });
            ch.Items.Add(new ComboBoxItem
            {
                Content = "3"
            });
            ch.Items.Add(new ComboBoxItem
            {
                Content = "4"
            });
            ch.Items.Add(new ComboBoxItem
            {
                Content = "5"
            });
            ch.Items.Add(new ComboBoxItem
            {
                Content = "6"
            });
            ch.Items.Add(new ComboBoxItem
            {
                Content = "7"
            });
            ch.Items.Add(new ComboBoxItem
            {
                Content = "8"
            });

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
                lineData[i].AcceptsUnsortedData = true;
            }

            var colors = new[]
            {
                System.Windows.Media.Colors.Red,
                Colors.Orange,
                Colors.Cyan,
                Colors.Green,
                Colors.Blue,
                Colors.Orchid,
                Colors.Purple,
                Colors.Brown,
            };
            // 添加8条曲线
            for (int i = 0; i < 8; i++)
            {
                var lineSeries = new FastLineRenderableSeries()
                {
                    Stroke = colors[i],
                    StrokeThickness = 1,
                    AntiAliasing = true,
                };
                sciChartSurface.RenderableSeries.Add(lineSeries);

            }
            // 将数据分配给8条曲线
            for (int i = 0; i < 8; i++)
            {
                sciChartSurface.RenderableSeries[i].DataSeries = lineData[i];
            }
            for (int i = 0; i < 500; i++)
            {
                //500行8列
                save_data_buffer[i] = new double[8];
            }
            channel_1.IsChecked = true;
            channel_2.IsChecked = true;
            channel_3.IsChecked = true;
            channel_4.IsChecked = true;
            channel_5.IsChecked = true;
            channel_6.IsChecked = true;
            channel_7.IsChecked = true;
            channel_8.IsChecked = true;
            _eeg.Ecg_FilterEvent += Ecg_FilterEvent;
        }
        public float[] eeg_data_float= new float[8];
        public double[] eeg_data = new double[8];
        private double g_index;
      
        public bool clear_original_filter_txt_flag=false;          
        private static int WindowSize = 10;  
   
        double[][] save_data_buffer = new double[500][];         

        /// <summary>
        /// 离线处理数据
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        //采样率
        static double sampleRate = 500;

        static double hpCut = 0.5;   // 高通截止 0.5 Hz
        static double hpA = Math.Exp(-2.0 * Math.PI * hpCut / sampleRate);

        static double lpCut = 40.0;  // 低通截止 40 Hz
        // 4阶 Butterworth 两段二阶的 Q（固定值）
        static double lpQ1 = 0.5411961;
        static double lpQ2 = 1.3065630;

        static double notchF0 = 50.0;
        static double notchQ = 25.0;  // 约等于 BW=2Hz 的量级（可按需要微调）

        // ===== 状态（8通道）=====
        double[] hp1_prevX = new double[8];
        double[] hp1_prevY = new double[8];
        double[] hp2_prevX = new double[8];
        double[] hp2_prevY = new double[8];

        double[][] medBuf = Enumerable.Range(0, 8).Select(_ => new double[5]).ToArray();
        int[] medCount = new int[8];
        int[] medIdx = new int[8];

        BiquadLPF[] lpf1= Enumerable.Range(0, 8).Select(_ => new BiquadLPF(sampleRate, lpCut, lpQ1)).ToArray();
        BiquadLPF[] lpf2= Enumerable.Range(0, 8).Select(_ => new BiquadLPF(sampleRate, lpCut, lpQ2)).ToArray();
        BiquadNotch[] notch1;


        double g_index_1 = 0;
        // ===== 每次处理前都重置（关键！）=====
        void ResetFilterState(int chCount)
        {
            // 1) 高通状态清零
            Array.Clear(hp1_prevX, 0, hp1_prevX.Length);
            Array.Clear(hp1_prevY, 0, hp1_prevY.Length);
            Array.Clear(hp2_prevX, 0, hp2_prevX.Length);
            Array.Clear(hp2_prevY, 0, hp2_prevY.Length);

            // 2) 中值滤波状态清零
            for (int ch = 0; ch < chCount; ch++)
            {
                Array.Clear(medBuf[ch], 0, medBuf[ch].Length);
                medCount[ch] = 0;
                medIdx[ch] = 0;
            }

            notch1 = Enumerable.Range(0, chCount).Select(_ => new BiquadNotch(sampleRate, notchF0, notchQ)).ToArray();

            g_index_1 = 0;
        }
        public  double[][] LoadExcelAs2DArray(string Freqtextbox_filter)
        {
            // ① 打开文件选择框
            OpenFileDialog ofd = new OpenFileDialog
            {
                Title = "选择 Excel 文件",
                Filter = "Excel 文件 (*.xlsx)|*.xlsx",
                Multiselect = false
            };

            if (ofd.ShowDialog() != true)
                return null;

            string filePath = ofd.FileName;

            // ② 设置 EPPlus 许可
            ExcelPackage.License.SetNonCommercialOrganization("My Noncommercial organization");

            var package = new ExcelPackage(new FileInfo(filePath));
            var worksheet = package.Workbook.Worksheets[0]; // 第一个 Sheet

            int colCount = worksheet.Dimension.Columns;    // 列数 = 通道数
            int rowCount = worksheet.Dimension.Rows; // 行数 = 数据点数

            ResetFilterState(colCount);
            // ③ 创建二维数组 [通道, 数据点]
            double[,] data = new double[colCount, rowCount];

            for (int col = 1; col <= colCount; col++)
            {
                for (int row = 1; row <= rowCount; row++)
                {
                    object cellValue = worksheet.Cells[row, col].Value;

                    // ⚠️ 防止空单元格 / 非数字
                    if (cellValue == null)
                        data[col - 1, row - 1] = 0;
                    else
                        data[col - 1, row - 1] = Convert.ToDouble(cellValue);
                }
            }
            double[][] eeg_data_buffer = new double[colCount][];

            for (int i = 0; i < colCount; i++)
            {
                eeg_data_buffer[i] = new double[rowCount];
                for (int m=0; m < rowCount-1; m++)
                {
                    double temp = (Convert.ToDouble(data[i,m]));

                    // ① 前置：5点中值去尖峰（防振铃）
                    double med = Median5_Update(i, temp);
                    double x = (Math.Abs(temp - med) > 800) ? med : temp;
                    // --- 第1级 一阶高通：去基线漂移 ---
                    double yhp1 = hpA * (hp1_prevY[i] + x - hp1_prevX[i]);
                    hp1_prevX[i] = x;
                    hp1_prevY[i] = yhp1;

                    // --- 第2级 一阶高通：进一步增强滚降 ---
                    double yhp2 = hpA * (hp2_prevY[i] + yhp1 - hp2_prevX[i]);
                    hp2_prevX[i] = yhp1;
                    hp2_prevY[i] = yhp2;

                    // --- 50 Hz 双级陷波1级 ---
                    double y1 = notch1[i].Process(yhp2);
                    //double y2 = notch2[i].Process(y1);


                    //---新增：40 Hz 低通（两级，等效4阶） ---
                    double ylp1 = lpf1[i].Process(y1);
                    double ylp2 = lpf2[i].Process(ylp1);
                    //double ylp3 = lpf3[i].Process(ylp2);

                    eeg_data_buffer[i][m] = ylp2;
                    lineData[i].Append(g_index, eeg_data_buffer[i][m] - i * 10000);
                    g_index += 1.0/ Convert.ToDouble(Freqtextbox_filter);
                }
                g_index = 0;

            }
            NlogHelper.WriteInfoLog($"已处理8通道每通道{{colCount}}个数据");
            return eeg_data_buffer;
        }
        public void save_offline(double[][] eeg_data_buffer)
        {
            if (eeg_data_buffer == null || eeg_data_buffer.Length == 0)
                throw new Exception("eeg_data_buffer 为空");

            for (int ch = 0; ch < eeg_data_buffer.Length; ch++)
            {
                if (eeg_data_buffer[ch] == null)
                    throw new Exception($"eeg_data_buffer[{ch}] 未初始化");
            }

            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Title = "保存文件",
                FileName = "Record-" + DateTime.Now.ToString("yyyyMMdd-HH时mm分ss秒") + ".xlsx",
                DefaultExt = "xlsx",
                Filter = "Excel 文件 (*.xlsx)|*.xlsx"
            };

            if (saveFileDialog.ShowDialog() != true)
                return;

            string filePath = saveFileDialog.FileName;
            ExcelPackage.License.SetNonCommercialOrganization("My Noncommercial organization");

            using (var package = new ExcelPackage(new FileInfo(filePath)))
            {
                var worksheet = package.Workbook.Worksheets.Add("EEG Data");

                int channelCount = eeg_data_buffer.Length;
                int sampleCount = eeg_data_buffer.Min(ch => ch.Length);

                object[][] allData = new object[sampleCount + 1][];

                // 表头
                allData[0] = new object[channelCount];
                for (int ch = 0; ch < channelCount; ch++)
                    allData[0][ch] = $"Ch{ch + 1}";

                // 数据
                for (int t = 0; t < sampleCount; t++)
                {
                    allData[t + 1] = new object[channelCount];
                    for (int ch = 0; ch < channelCount; ch++)
                    {
                        allData[t + 1][ch] = eeg_data_buffer[ch][t];
                    }
                }

                worksheet.Cells[1, 1].LoadFromArrays(allData);
                package.Save();
            }

            LogHelper.WriteInfoLog("EEG 数据保存成功");
            NlogHelper.WriteInfoLog("EEG 数据保存成功");
        }


        private void Ecg_FilterEvent(object sender, EcgFilterEventArgs e)
        {
            //EEG_Length_COUNT++;
            //if (IsFilter)
            //{
               
            //    if (clear_original_filter_txt_flag)
            //    {
            //        // 清空 Original_data.txt 文件内容
            //        //File.WriteAllText(originalDataFile, string.Empty);
            //        //File.WriteAllText(filterDataFile, string.Empty);
            //        //File.WriteAllText(originalDataNs2File, string.Empty);
            //        //File.WriteAllText(filterDataNs2File, string.Empty);
            //        clear_original_filter_txt_flag = false;
            //        IsFilter=false;
            //        //如需复位滤波器状态可在此重新new
            //        for (int k = 0; k < 8; k++)
            //        {
            //            notch1[k] = new BiquadNotch(sampleRate, f0, Q);
            //            notch2[k] = new BiquadNotch(sampleRate, f0, Q);
            //            // 新增：重置高通状态
            //            hp1_prevX[k] = hp1_prevY[k] = 0.0;
            //            hp2_prevX[k] = hp2_prevY[k] = 0.0;
            //            lpf1[k] = new BiquadLPF(sampleRate, lpCut, lpQ);
            //            lpf2[k] = new BiquadLPF(sampleRate, lpCut, lpQ);
            //            Array.Clear(medBuf[k], 0, medBuf[k].Length);
            //            medCount[k] = 0;
            //            medIdx[k] = 0;
            //        }
            //    }
               
            //    // 创建滤波器并处理该通道数据
            //    //var onlineFirFilter = OnlineFirFilter.CreateBandpass(ImpulseResponse.Finite, 1000, 0.3, 40, 1024);
            //    Buffer.BlockCopy(e.eeg_data, 0, eeg_data_float, 0, 8);
            //    //for(int i = 0; i < eeg_data_float.Length; i++)
            //    //{
            //    //    eeg_data[i] =Convert.ToDouble( eeg_data_float[i]);
            //    //}
              
            //    //if (long_filter_flag)
            //    //{
            //    //    EEG_FILTER_DATA.Add(eeg_data);
            //    //    //开始处理
            //    //    if (EEG_Length_COUNT >= EEG_Length)
            //    //    {
            //    //        // 对每个通道进行滤波处理
            //    //        for (int i = 0; i < 8; i++)
            //    //        {
            //    //            // 提取所有数组的索引[i]为一个新数组
            //    //            double[] channelData = new double[EEG_FILTER_DATA.Count];
            //    //            for (int j = 0; j < EEG_FILTER_DATA.Count; j++)
            //    //            {
            //    //                channelData[j] = Convert.ToDouble(EEG_FILTER_DATA[j][i]);
            //    //            }
            //    //            double[] filteredChannelData = onlineFirFilter.ProcessSamples(channelData);
            //    //            for (int j = 0; j < filteredChannelData.Length; j++)
            //    //            {
            //    //                lineData[i].Append(g_index, filteredChannelData[j]);
            //    //            }
            //    //            g_index += 0.002;

            //    //            //当前数据点与上次更新的数据差大于4x500个数据点或者当前数据少于WindowSizex500
            //    //            if (Convert.ToInt32(g_index) - g_old_ecg_time > 4 || Convert.ToInt32(g_index) < WindowSize)
            //    //            {
            //    //                g_old_ecg_time = Convert.ToInt32(g_index);

            //    //                if (Convert.ToInt32(g_index) < WindowSize)
            //    //                {
            //    //                    g_old_ecg_time = Convert.ToInt32(WindowSize - 5);
            //    //                }
            //    //                this.Dispatcher.BeginInvoke((Action)delegate ()
            //    //                {
            //    //                    sciChartSurface.XAxis.VisibleRange = ComputeXAxisRange(g_old_ecg_time);
            //    //                });
            //    //            }
            //    //            EEG_Length_COUNT = 0;
            //    //            // 现在filteredChannelData包含了该通道所有时间点的滤波后数据
            //    //            // 您可以在这里使用滤波后的数据进行后续处理
            //    //        }
            //    //    }

            //    //}
            //    if (single_filter_flag)
            //    {
            //        if (EEG_Length_COUNT >= 500)
            //        {

            //            for (int i = 0; i < 8; i++)
            //            {
            //                //double filterdata = onlineFirFilter.ProcessSample(Convert.ToDouble(eeg_data_float[i]));
            //                //double filterdata = notchFilter.ProcessSample(Convert.ToDouble(eeg_data_float[i]));
            //                //double x = Convert.ToDouble(eeg_data_float[i]);

            //                //// 可选：去掉直流均值，减少边缘效应（强烈建议）
            //                //const double hpTauSec = 2.0;               // 2s 的慢均值（可调）
            //                //double[] dcMean = new double[8];    // 放到类里
            //                //double alpha = 1.0 / (sampleRate * hpTauSec);
            //                //dcMean[i] += (x - dcMean[i]) * alpha;
            //                //double xAC = x - dcMean[i];

            //                //// 串两级 notch 提升抑制深度
            //                //double y = notch2.Process(notch1.Process(xAC));

            //                //// 如需保持原有直流水平，可加回去（画原始计数值时更直观）
            //                //double filterdata = y + dcMean[i];

            //                double x = eeg_data_float[i];


            //                // --- 第1级 一阶高通：去基线漂移 ---
            //                // y[n] = a * (y[n-1] + x[n] - x[n-1])
            //                double yhp1 = hpA * (hp1_prevY[i] + x - hp1_prevX[i]);
            //                hp1_prevX[i] = x;
            //                hp1_prevY[i] = yhp1;

            //                // --- 第2级 一阶高通：进一步增强滚降 ---
            //                double yhp2 = hpA * (hp2_prevY[i] + yhp1 - hp2_prevX[i]);
            //                hp2_prevX[i] = yhp1;
            //                hp2_prevY[i] = yhp2;

                       
            //                // --- 50 Hz 双级陷波 ---
            //                double y1 = notch1[i].Process(yhp2);
            //                double y2 = notch2[i].Process(y1);


            //                //---新增：40 Hz 低通（两级，等效4阶） ---
            //               double ylp1 = lpf1[i].Process(y2);
            //                double ylp2 = lpf2[i].Process(ylp1);
            //                double ylp3 = lpf3[i].Process(ylp2);



            //                //---新增：5点中值去尖峰（实时） ---
            //                double filterdata = Median5_Update(i, ylp3);

            //                lineData[i].Append(g_index, filterdata);
            //                if (i == 0)
            //                {
            //                    //File.AppendAllText(originalDataFile, x.ToString("G17") + " ");
            //                    //File.AppendAllText(filterDataFile, filterdata.ToString("G17") + " ");
            //                    File.AppendAllText(originalDataNs2File, x.ToString("G17") + " ");
            //                    File.AppendAllText(filterDataNs2File, filterdata.ToString("G17") + " ");
            //                }
            //                save_data_buffer[buffer_save_index][i] = filterdata;
            //            }
            //            g_index += 0.002;
            //            buffer_save_index++;
            //            buffer_save_index %= 500;
            //            if (buffer_save_index == 499)
            //            {
            //                // 创建一个新的数据快照（值拷贝）
            //                var copiedBuffer = new double[500][];
            //                for (int i = 0; i < 500; i++)
            //                {
            //                    copiedBuffer[i] = new double[8];
            //                    Array.Copy(save_data_buffer[i], copiedBuffer[i], 8);
            //                }

            //                save_data_buffer_all.Add(copiedBuffer);

            //                // 原始 buffer 重置
            //                save_data_buffer = new double[500][];
            //                for (int i = 0; i < 500; i++) save_data_buffer[i] = new double[8];
            //                buffer_save_index = 0;
            //                //save_data_buffer_all.Add(save_data_buffer);
            //            }

            //            //当前数据点与上次更新的数据差大于4x500个数据点或者当前数据少于WindowSizex500
            //            if (Convert.ToInt32(g_index) - g_old_ecg_time > 4 || Convert.ToInt32(g_index) < WindowSize)
            //            {
            //                g_old_ecg_time = Convert.ToInt32(g_index);

            //                if (Convert.ToInt32(g_index) < WindowSize)
            //                {
            //                    g_old_ecg_time = Convert.ToInt32(WindowSize - 5);
            //                }
            //                this.Dispatcher.BeginInvoke((Action)delegate ()
            //                {
            //                    sciChartSurface.XAxis.VisibleRange = ComputeXAxisRange(g_old_ecg_time);
            //                });
            //            }
            //        }
            //        //if (EEG_Length_COUNT >= 500)
            //        //{

            //        //    for (int i = 0; i < 8; i++)
            //        //    {
            //        //        double x = eeg_data_float[i];
            //        //        // --- 第1级 一阶高通：去基线漂移 ---
            //        //        // y[n] = a * (y[n-1] + x[n] - x[n-1])
            //        //        double yhp1 = hpA * (hp1_prevY[i] + x - hp1_prevX[i]);
            //        //        hp1_prevX[i] = x;
            //        //        hp1_prevY[i] = yhp1;

            //        //        // --- 第2级 一阶高通：进一步增强滚降 ---
            //        //        double yhp2 = hpA * (hp2_prevY[i] + yhp1 - hp2_prevX[i]);
            //        //        hp2_prevX[i] = yhp1;
            //        //        hp2_prevY[i] = yhp2;

            //        //        //double yhp2_2 = Median5_Update(i, yhp2);

            //        //        //double ylp1 = lpf1[i].Process(yhp2);
            //        //        //double ylp2 = lpf2[i].Process(ylp1);

            //        //        //double y1 = notch1[i].Process(ylp2);
            //        //        //double y2 = notch2[i].Process(y1);

            //        //        //double filterdata = Median5_Update(i, y2);
            //        //        // --- 50 Hz 双级陷波 ---
            //        //        double y1 = notch1[i].Process(yhp2);
            //        //        double y2 = notch2[i].Process(y1);

                           
            //        //        // --- 新增：40 Hz 低通（两级，等效4阶） ---
            //        //        double ylp1 = lpf1[i].Process(y2);
            //        //        double ylp2 = lpf2[i].Process(ylp1);
            //        //        double ylp3 = lpf3[i].Process(ylp2);

            //        //        //---新增：5点中值去尖峰（实时） ---
            //        //        double filterdata = Median5_Update(i, ylp3);

            //        //        lineData[i].Append(g_index, filterdata);
            //        //        if (i == 0)
            //        //        {
            //        //            File.AppendAllText(originalDataFile, x.ToString("G17") + " ");
            //        //            File.AppendAllText(filterDataFile, filterdata.ToString("G17") + " ");
            //        //            File.AppendAllText(originalDataNs2File, x.ToString("G17") + " ");
            //        //            File.AppendAllText(filterDataNs2File, filterdata.ToString("G17") + " ");
            //        //        }
            //        //        save_data_buffer[buffer_save_index][i] = filterdata;
            //        //    }
            //        //    g_index += 0.002;
            //        //    buffer_save_index++;
            //        //    buffer_save_index %= 500;
            //        //    if (buffer_save_index == 499)
            //        //    {
            //        //        save_data_buffer_all.Add(save_data_buffer);
            //        //    }

            //        //    //当前数据点与上次更新的数据差大于4x500个数据点或者当前数据少于WindowSizex500
            //        //    if (Convert.ToInt32(g_index) - g_old_ecg_time > 4 || Convert.ToInt32(g_index) < WindowSize)
            //        //    {
            //        //        g_old_ecg_time = Convert.ToInt32(g_index);

            //        //        if (Convert.ToInt32(g_index) < WindowSize)
            //        //        {
            //        //            g_old_ecg_time = Convert.ToInt32(WindowSize - 5);
            //        //        }
            //        //        this.Dispatcher.BeginInvoke((Action)delegate ()
            //        //        {
            //        //            sciChartSurface.XAxis.VisibleRange = ComputeXAxisRange(g_old_ecg_time);
            //        //        });
            //        //    }
            //        //}

            //    }





            //}

        }
       
     
        private static DoubleRange ComputeXAxisRange(double t)
        {
            if (t < WindowSize)
            {
                return new DoubleRange(0, WindowSize);
            }
            //t 值向上取整到最接近的整数
            return new DoubleRange(Math.Ceiling(t) - WindowSize + 5, Math.Ceiling(t) + 5);
        }
        private void channel_1_Checked(object sender, RoutedEventArgs e)
        {
            channel_1.IsChecked = true;
            sciChartSurface.RenderableSeries[0].IsVisible = true;
        }

        private void channel_2_Checked(object sender, RoutedEventArgs e)
        {
            channel_2.IsChecked = true;
            sciChartSurface.RenderableSeries[1].IsVisible = true;
        }

        private void channel_3_Checked(object sender, RoutedEventArgs e)
        {
            channel_3.IsChecked = true;
            sciChartSurface.RenderableSeries[2].IsVisible = true;
        }

        private void channel_4_Checked(object sender, RoutedEventArgs e)
        {
            channel_4.IsChecked = true;
            sciChartSurface.RenderableSeries[3].IsVisible = true;
        }

        private void channel_5_Checked(object sender, RoutedEventArgs e)
        {
            channel_5.IsChecked = true;
            sciChartSurface.RenderableSeries[4].IsVisible = true;
        }

        private void channel_6_Checked(object sender, RoutedEventArgs e)
        {
            channel_6.IsChecked = true;
            sciChartSurface.RenderableSeries[5].IsVisible = true;
        }

        private void channel_7_Checked(object sender, RoutedEventArgs e)
        {
            channel_7.IsChecked = true;
            sciChartSurface.RenderableSeries[6].IsVisible = true;
        }

        private void channel_8_Checked(object sender, RoutedEventArgs e)
        {
            channel_8.IsChecked = true;
            sciChartSurface.RenderableSeries[7].IsVisible = true;
        }
        private void channel_1_Unchecked(object sender, RoutedEventArgs e)
        {
            channel_1.IsChecked = false;
            sciChartSurface.RenderableSeries[0].IsVisible = false;
        }

        private void channel_2_Unchecked(object sender, RoutedEventArgs e)
        {
            channel_2.IsChecked = false;
            sciChartSurface.RenderableSeries[1].IsVisible = false;
        }

        private void channel_3_Unchecked(object sender, RoutedEventArgs e)
        {
            channel_3.IsChecked = false;
            sciChartSurface.RenderableSeries[2].IsVisible = false;
        }

        private void channel_4_Unchecked(object sender, RoutedEventArgs e)
        {
            channel_4.IsChecked = false;
            sciChartSurface.RenderableSeries[3].IsVisible = false;
        }

        private void channel_5_Unchecked(object sender, RoutedEventArgs e)
        {
            channel_5.IsChecked = false;
            sciChartSurface.RenderableSeries[4].IsVisible = false;
        }

        private void channel_6_Unchecked(object sender, RoutedEventArgs e)
        {
            channel_6.IsChecked = false;
            sciChartSurface.RenderableSeries[5].IsVisible = false;
        }

        private void channel_7_Unchecked(object sender, RoutedEventArgs e)
        {
            channel_7.IsChecked = false;
            sciChartSurface.RenderableSeries[6].IsVisible = false;
        }

        private void channel_8_Unchecked(object sender, RoutedEventArgs e)
        {
            channel_8.IsChecked = false;
            sciChartSurface.RenderableSeries[7].IsVisible = false;
        }
        // ===== 5点中值（前置）=====
        double Median5_Update(int ch, double x)
        {
            var buf = medBuf[ch];
            buf[medIdx[ch]] = x;
            medIdx[ch] = (medIdx[ch] + 1) % 5;
            if (medCount[ch] < 5) medCount[ch]++;

            int n = medCount[ch];
            if (n <= 1) return x;

            // 注意：环形缓冲直接Copy会乱序，但中值不依赖顺序，只依赖集合，因此可直接复制前n个“已写入的元素”
            // 为了更严谨：复制全5个再取前n个非0也行；这里用简单实现
            double[] w = new double[n];
            Array.Copy(buf, w, n);
            Array.Sort(w);
            return w[n / 2];
        }
        // 5点中值，实时更新：把当前样本写入环形缓冲，返回中值
        //double Median5_Update(int ch, double x)
        //{
        //    var buf = medBuf[ch];
        //    buf[medIdx[ch]] = x;
        //    medIdx[ch] = (medIdx[ch] + 1) % 5;
        //    if (medCount[ch] < 5) medCount[ch]++;

        //    // 拷贝已填元素并求中值
        //    int n = medCount[ch];
        //    if (n == 1) return x; // 初始化前期
        //    double[] w = new double[n];
        //    Array.Copy(buf, w, n);
        //    Array.Sort(w, 0, n);
        //    return w[n / 2];
        //}
       
        private void Button_Click(object sender, RoutedEventArgs e)
        {
        }
        double g_index_2 = 0;
        private void Button_Click_1(object sender, RoutedEventArgs e)
        {

           
        }

     
    }

    public  class FIRFilter
    {
        private readonly double[] coefficients;
        private readonly double[] buffer;
        private int offset;

        public FIRFilter(double[] coefficients)
        {
            this.coefficients = coefficients;
            this.buffer = new double[coefficients.Length];
            this.offset = 0;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public double Process(double input)
        {
            buffer[offset] = input;

            double output = 0.0;
            int idx = offset;
            for (int i = 0; i < coefficients.Length; i++)
            {
                output += coefficients[i] * buffer[idx];
                if (--idx < 0) idx = buffer.Length - 1;
            }

            if (++offset >= buffer.Length) offset = 0;

            return output;
        }

        public double[] Process(double[] input)
        {
            double[] output = new double[input.Length];
            for (int i = 0; i < input.Length; i++)
                output[i] = Process(input[i]);
            return output;
        }
    }


}
