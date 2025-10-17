using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using System.IO;
using Accord.Math;
using Accord.Statistics;

namespace Collect.Power
{
    /// <summary>
    /// 功率谱密度分析器 - 对应MATLAB的GetPSD函数和主程序
    /// </summary>
    public class PowerSpectralDensityAnalyzer
    {
        #region 常量定义

        /// <summary>
        /// 频段定义
        /// </summary>
        public enum FrequencyBand
        {
            Delta = 1,    // 1-4 Hz
            Theta = 2,    // 4-8 Hz
            Alpha = 3,    // 8-13 Hz
            Beta = 4,     // 13-30 Hz
            LGamma = 5,   // 30-45 Hz
            HGamma = 6    // 55-100 Hz
        }

        /// <summary>
        /// 频段范围定义
        /// </summary>
        private static readonly Dictionary<FrequencyBand, (double min, double max)> FrequencyRanges = 
            new Dictionary<FrequencyBand, (double min, double max)>
        {
            { FrequencyBand.Delta, (1.0, 4.0) },
            { FrequencyBand.Theta, (4.0, 8.0) },
            { FrequencyBand.Alpha, (8.0, 13.0) },
            { FrequencyBand.Beta, (13.0, 30.0) },
            { FrequencyBand.LGamma, (30.0, 45.0) },
            { FrequencyBand.HGamma, (55.0, 100.0) }
        };

        #endregion

        #region 数据结构

        /// <summary>
        /// 功率谱分析结果 - 对应MATLAB的GetPSD函数返回值
        /// </summary>
        public class PSDResult
        {
            public double[] Frequencies { get; set; }           // f: 功率谱频率范围
            public double[] PowerSpectralDensity { get; set; }  // pxx: 功率谱密度
            public double[] PowerSpectralDensityDB { get; set; } // pxxdb: 分贝形式的功率谱密度
            public double[] SmoothedPSD { get; set; }           // mpxxdb: 平滑后的功率谱密度
            public Dictionary<FrequencyBand, double> AbsolutePower { get; set; }  // absolutely_power
            public Dictionary<FrequencyBand, double> RelativePower { get; set; }  // relative_power
            public double TotalPower { get; set; }              // absolutely_power_total
        }

        /// <summary>
        /// 分析结果集合 - 对应MATLAB的cell数组
        /// </summary>
        public class AnalysisResults
        {
            public Dictionary<string, PSDResult> Results { get; set; } = new Dictionary<string, PSDResult>();
            public Dictionary<string, double[]> AbsolutePower { get; set; } = new Dictionary<string, double[]>();
            public Dictionary<string, double[]> RelativePower { get; set; } = new Dictionary<string, double[]>();
            public Dictionary<string, double> TotalPower { get; set; } = new Dictionary<string, double>();
        }

        #endregion

        #region 私有字段

        private readonly string[] channelNames = { "cortex", "hippo" };
        private readonly string[] stageNames = { "pre", "on", "after" };
        private readonly int[,] timeRanges = {
            { 400000, 700000 },    // pre阶段
            { 776819, 1676819 },   // on阶段  
            { 2400000, 2700000 }   // after阶段
        };

        #endregion

        #region 核心方法

        /// <summary>
        /// 计算功率谱密度 - 对应MATLAB的GetPSD函数
        /// [ pxx_10_mul_log10, pxx, f ] = GetPSD( ecog, srate, frequency_resolution )
        /// </summary>
        /// <param name="signal">输入信号 (ecog)</param>
        /// <param name="sampleRate">采样率 (srate)</param>
        /// <param name="frequencyResolution">频率分辨率 (frequency_resolution)</param>
        /// <returns>功率谱密度结果 [pxxdb, pxx, f]</returns>
        public PSDResult GetPSD(double[] signal, double sampleRate, double frequencyResolution = 1.0)
        {
            try
            {
                // 计算FFT长度
                int nfft = (int)(sampleRate / frequencyResolution);
                nfft = NextPowerOfTwo(nfft);

                // 应用窗函数（汉宁窗）
                double[] windowedSignal = ApplyHanningWindow(signal, nfft);

                // 执行FFT
                Complex[] fftResult = new Complex[nfft];
                for (int i = 0; i < nfft; i++)
                {
                    fftResult[i] = new Complex(windowedSignal[i], 0.0);
                }

                // 执行FFT
                FourierTransform.FFT(fftResult, FourierTransform.Direction.Forward);

                // 计算功率谱密度
                double[] psd = new double[nfft / 2];
                double[] frequencies = new double[nfft / 2];

                for (int i = 0; i < nfft / 2; i++)
                {
                    frequencies[i] = i * sampleRate / nfft;
                    psd[i] = Math.Pow(fftResult[i].Magnitude, 2) / (sampleRate * nfft);
                }

                // 转换为分贝
                double[] psdDB = psd.Select(p => 10 * Math.Log10(p)).ToArray();

                // 去除工频干扰 - 对应MATLAB的工频去除代码
                double[] filteredPSD = RemovePowerLineNoise(psdDB);

                // 平滑处理 - 对应MATLAB的smooth函数
                double[] smoothedPSD = SmoothSignal(filteredPSD, 5);

                // 计算各频段功率 - 对应MATLAB的bandpower函数
                var absolutePower = CalculateAbsolutePower(psd, frequencies);
                double totalPower = CalculateTotalPower(psd, frequencies);
                var relativePower = CalculateRelativePower(absolutePower, totalPower);

                return new PSDResult
                {
                    Frequencies = frequencies,
                    PowerSpectralDensity = psd,
                    PowerSpectralDensityDB = psdDB,
                    SmoothedPSD = smoothedPSD,
                    AbsolutePower = absolutePower,
                    RelativePower = relativePower,
                    TotalPower = totalPower
                };
            }
            catch (Exception ex)
            {
                throw new Exception($"功率谱密度计算失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 主分析函数 - 对应MATLAB主程序
        /// </summary>
        /// <param name="data">输入数据 [通道数][数据长度]</param>
        /// <param name="fileName">文件名</param>
        /// <param name="savePath">保存路径</param>
        /// <returns>分析结果</returns>
        public AnalysisResults AnalyzeEpilepsyData(double[][] data, string fileName, string savePath)
        {
            try
            {
                var results = new AnalysisResults();

                // 确保保存路径存在
                Directory.CreateDirectory(savePath);

                // 处理每个通道和阶段 - 对应MATLAB的双重循环
                for (int ch = 0; ch < 2; ch++)      // 多个通道
                {
                    for (int stage = 0; stage < 3; stage++)   // 多个阶段
                    {
                        // 提取时间段数据 - 对应MATLAB的lfp=data(ch,time1(stage)+1:time2(stage))
                        var signal = ExtractTimeRange(data[ch], timeRanges[stage, 0], timeRanges[stage, 1]);

                        // 计算功率谱密度 - 对应MATLAB的[pxxdb, pxx, f] = GetPSD(lfp,1000,1)
                        var psdResult = GetPSD(signal, 1000, 1.0);

                        // 保存结果
                        string key = $"{channelNames[ch]}_{stageNames[stage]}";
                        results.Results[key] = psdResult;

                        // 保存绝对功率 - 对应MATLAB的aabsolutely_power{stage,ch}=absolutely_power
                        double[] absolutePowerArray = new double[6];
                        for (int i = 0; i < 6; i++)
                        {
                            absolutePowerArray[i] = psdResult.AbsolutePower[(FrequencyBand)(i + 1)];
                        }
                        results.AbsolutePower[key] = absolutePowerArray;

                        // 保存相对功率 - 对应MATLAB的arelative_power{stage,ch}=relative_power
                        double[] relativePowerArray = new double[6];
                        for (int i = 0; i < 6; i++)
                        {
                            relativePowerArray[i] = psdResult.RelativePower[(FrequencyBand)(i + 1)];
                        }
                        results.RelativePower[key] = relativePowerArray;

                        // 保存总功率 - 对应MATLAB的aabsolutely_power_total{stage,ch}=absolutely_power_total
                        results.TotalPower[key] = psdResult.TotalPower;

                        // 生成功率谱图 - 对应MATLAB的plot和saveas
                        GeneratePowerSpectrumPlot(psdResult, fileName, channelNames[ch], stageNames[stage], savePath);

                        // 输出结果
                        Console.WriteLine($"通道: {channelNames[ch]}, 阶段: {stageNames[stage]}");
                        Console.WriteLine("绝对功率:");
                        foreach (var power in psdResult.AbsolutePower)
                        {
                            Console.WriteLine($"  {power.Key}: {power.Value:F4}");
                        }
                        Console.WriteLine("相对功率:");
                        foreach (var power in psdResult.RelativePower)
                        {
                            Console.WriteLine($"  {power.Key}: {power.Value:F2}%");
                        }
                        Console.WriteLine();
                    }
                }

                // 保存结果到文件 - 对应MATLAB的save函数
                SaveResults(results, fileName, savePath);

                return results;
            }
            catch (Exception ex)
            {
                throw new Exception($"癫痫数据分析失败: {ex.Message}");
            }
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 计算下一个2的幂次方
        /// </summary>
        private int NextPowerOfTwo(int length)
        {
            return (int)Math.Pow(2, Math.Ceiling(Math.Log(length, 2)));
        }

        /// <summary>
        /// 应用汉宁窗
        /// </summary>
        private double[] ApplyHanningWindow(double[] signal, int length)
        {
            double[] windowed = new double[length];
            for (int i = 0; i < Math.Min(signal.Length, length); i++)
            {
                double window = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (length - 1)));
                windowed[i] = signal[i] * window;
            }
            return windowed;
        }

        /// <summary>
        /// 去除工频干扰 - 对应MATLAB的工频去除代码
        /// </summary>
        private double[] RemovePowerLineNoise(double[] psdDB)
        {
            double[] filtered = (double[])psdDB.Clone();

            // 50Hz工频干扰去除 (45-56) - 对应MATLAB的for gp=45:56
            RemoveSpecificFrequencyRange(filtered, 45, 56);
            
            // 100Hz工频干扰去除 (95-106) - 对应MATLAB的for gp=95:106
            RemoveSpecificFrequencyRange(filtered, 95, 106);
            
            // 150Hz工频干扰去除 (145-156) - 对应MATLAB的for gp=145:156
            RemoveSpecificFrequencyRange(filtered, 145, 156);

            return filtered;
        }

        /// <summary>
        /// 去除特定频率范围 - 对应MATLAB的陷波处理
        /// </summary>
        private void RemoveSpecificFrequencyRange(double[] signal, int start, int end)
        {
            // 第一次处理 - 对应MATLAB的ft(gp)=(ft(gp-8)+ft(gp+8))/2+ft(gp)/50
            for (int i = start; i <= end && i < signal.Length; i++)
            {
                if (i - 8 >= 0 && i + 8 < signal.Length)
                {
                    signal[i] = (signal[i - 8] + signal[i + 8]) / 2 + signal[i] / 50;
                }
            }

            // 第二次处理 - 对应MATLAB的ft(gp)=(ft(gp-2)+ft(gp+2))/2
            for (int i = start; i <= end && i < signal.Length; i++)
            {
                if (i - 2 >= 0 && i + 2 < signal.Length)
                {
                    signal[i] = (signal[i - 2] + signal[i + 2]) / 2;
                }
            }
        }

        /// <summary>
        /// 信号平滑处理 - 对应MATLAB的smooth函数
        /// </summary>
        private double[] SmoothSignal(double[] signal, int windowSize)
        {
            double[] smoothed = new double[signal.Length];
            int halfWindow = windowSize / 2;

            for (int i = 0; i < signal.Length; i++)
            {
                double sum = 0;
                int count = 0;

                for (int j = Math.Max(0, i - halfWindow); 
                     j <= Math.Min(signal.Length - 1, i + halfWindow); j++)
                {
                    sum += signal[j];
                    count++;
                }

                smoothed[i] = sum / count;
            }

            return smoothed;
        }

        /// <summary>
        /// 计算绝对功率 - 对应MATLAB的bandpower函数
        /// </summary>
        private Dictionary<FrequencyBand, double> CalculateAbsolutePower(double[] psd, double[] frequencies)
        {
            var absolutePower = new Dictionary<FrequencyBand, double>();

            foreach (var band in FrequencyRanges)
            {
                double power = 0;
                for (int i = 0; i < frequencies.Length; i++)
                {
                    if (frequencies[i] >= band.Value.min && frequencies[i] <= band.Value.max)
                    {
                        power += psd[i];
                    }
                }
                absolutePower[band.Key] = power;
            }

            return absolutePower;
        }

        /// <summary>
        /// 计算总功率 - 对应MATLAB的总频段功率计算
        /// absolutely_power_total = bandpower(pxx, f, [0.5, 100], 'psd')
        /// </summary>
        private double CalculateTotalPower(double[] psd, double[] frequencies)
        {
            double totalPower = 0;
            for (int i = 0; i < frequencies.Length; i++)
            {
                if (frequencies[i] >= 0.5 && frequencies[i] <= 100)
                {
                    totalPower += psd[i];
                }
            }
            return totalPower;
        }

        /// <summary>
        /// 计算相对功率 - 对应MATLAB的相对功率计算
        /// relative_power(1,1)=( absolutely_power(1)./ absolutely_power_total )*100
        /// </summary>
        private Dictionary<FrequencyBand, double> CalculateRelativePower(
            Dictionary<FrequencyBand, double> absolutePower, double totalPower)
        {
            var relativePower = new Dictionary<FrequencyBand, double>();

            foreach (var power in absolutePower)
            {
                relativePower[power.Key] = (power.Value / totalPower) * 100;
            }

            return relativePower;
        }

        /// <summary>
        /// 提取时间段数据
        /// </summary>
        private double[] ExtractTimeRange(double[] data, int start, int end)
        {
            int length = end - start + 1;
            double[] extracted = new double[length];
            Array.Copy(data, start, extracted, 0, length);
            return extracted;
        }

        /// <summary>
        /// 生成功率谱图 - 对应MATLAB的plot和saveas
        /// </summary>
        private void GeneratePowerSpectrumPlot(PSDResult result, string fileName, string channelName, string stageName, string savePath)
        {
            try
            {
                // 保存功率谱图数据 - 对应MATLAB的plot(mpxxdb(1:100))
                string plotDataPath = Path.Combine(savePath, $"{fileName}_{channelName}_{stageName}_plotdata.txt");
                
                using (StreamWriter writer = new StreamWriter(plotDataPath))
                {
                    writer.WriteLine("Frequency(Hz),PowerSpectralDensity(dB)");
                    // 只保存前100个点，对应MATLAB的plot(mpxxdb(1:100))
                    for (int i = 0; i < Math.Min(100, result.SmoothedPSD.Length); i++)
                    {
                        writer.WriteLine($"{result.Frequencies[i]:F2},{result.SmoothedPSD[i]:F4}");
                    }
                }

                Console.WriteLine($"功率谱图数据已保存到: {plotDataPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"生成功率谱图失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 保存结果到文件 - 对应MATLAB的save函数
        /// </summary>
        private void SaveResults(AnalysisResults results, string fileName, string savePath)
        {
            try
            {
                string resultPath = Path.Combine(savePath, $"{fileName}_analysis_results.txt");
                
                using (StreamWriter writer = new StreamWriter(resultPath))
                {
                    writer.WriteLine("=== 癫痫数据功率谱密度分析结果 ===");
                    writer.WriteLine($"文件名: {fileName}");
                    writer.WriteLine($"分析时间: {DateTime.Now}");
                    writer.WriteLine();

                    foreach (var result in results.Results)
                    {
                        writer.WriteLine($"--- {result.Key} ---");
                        writer.WriteLine("绝对功率:");
                        foreach (var power in result.Value.AbsolutePower)
                        {
                            writer.WriteLine($"  {power.Key}: {power.Value:F4}");
                        }
                        writer.WriteLine("相对功率:");
                        foreach (var power in result.Value.RelativePower)
                        {
                            writer.WriteLine($"  {power.Key}: {power.Value:F2}%");
                        }
                        writer.WriteLine($"总功率: {result.Value.TotalPower:F4}");
                        writer.WriteLine();
                    }
                }

                Console.WriteLine($"分析结果已保存到: {resultPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存结果失败: {ex.Message}");
            }
        }

        #endregion

        #region 公共接口

        /// <summary>
        /// 设置时间段范围
        /// </summary>
        /// <param name="stage">阶段索引 (0=pre, 1=on, 2=after)</param>
        /// <param name="start">开始时间点</param>
        /// <param name="end">结束时间点</param>
        public void SetTimeRange(int stage, int start, int end)
        {
            if (stage >= 0 && stage < 3)
            {
                timeRanges[stage, 0] = start;
                timeRanges[stage, 1] = end;
            }
        }

        /// <summary>
        /// 获取当前时间段设置
        /// </summary>
        /// <returns>时间段数组</returns>
        public int[,] GetTimeRanges()
        {
            return (int[,])timeRanges.Clone();
        }

        /// <summary>
        /// 获取频段范围定义
        /// </summary>
        /// <returns>频段范围字典</returns>
        public static Dictionary<FrequencyBand, (double min, double max)> GetFrequencyRanges()
        {
            return new Dictionary<FrequencyBand, (double min, double max)>(FrequencyRanges);
        }

        #endregion
    }
}
