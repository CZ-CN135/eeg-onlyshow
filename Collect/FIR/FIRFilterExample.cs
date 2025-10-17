using System;
using System.IO;
using System.Linq;

namespace Collect.FIR
{
    /// <summary>
    /// FIR滤波器使用示例
    /// </summary>
    public class FIRFilterExample
    {
        /// <summary>
        /// 主示例方法
        /// </summary>
        public static void RunExample()
        {
            Console.WriteLine("=== FIR滤波器C#实现示例 ===\n");

            // 参数设置
            double fs = 1000.0;        // 采样率 1000Hz
            int order = 1201;          // 滤波器阶数
            double cutoffFreq = 200.0; // 截止频率 200Hz

            // 生成测试信号
            Console.WriteLine("1. 生成测试信号...");
            double[] testSignal = GenerateTestSignal(fs);
            Console.WriteLine($"   信号长度: {testSignal.Length}");
            Console.WriteLine($"   采样率: {fs} Hz");

            // 创建低通滤波器
            Console.WriteLine("\n2. 创建低通滤波器...");
            FIRFilter lowPassFilter = FIRFilterUtils.CreateLowPassFilter(fs, order, cutoffFreq);
            Console.WriteLine($"   滤波器阶数: {lowPassFilter.GetOrder()}");
            Console.WriteLine($"   截止频率: {cutoffFreq} Hz");

            // 应用滤波器
            Console.WriteLine("\n3. 应用滤波器...");
            double[] filteredSignal = lowPassFilter.ProcessSignalFull(testSignal);
            Console.WriteLine($"   滤波后信号长度: {filteredSignal.Length}");

            // 分析滤波器特性
            Console.WriteLine("\n4. 分析滤波器频率响应...");
            AnalyzeFilterResponse(lowPassFilter.GetCoefficients(), fs);

            // 保存结果
            Console.WriteLine("\n5. 保存结果...");
            SaveResults(testSignal, filteredSignal, fs);

            Console.WriteLine("\n=== 示例完成 ===");
        }

        /// <summary>
        /// 生成测试信号（模拟MATLAB示例）
        /// </summary>
        /// <param name="fs">采样率</param>
        /// <returns>测试信号</returns>
        private static double[] GenerateTestSignal(double fs)
        {
            int numSamples = 4096;
            double duration = (double)numSamples / fs;
            double[] time = new double[numSamples];

            for (int i = 0; i < numSamples; i++)
            {
                time[i] = i / fs;
            }

            // 生成三个不同频率的正弦波
            double f1 = 100.0;   // 100Hz
            double f2 = 800.0;   // 800Hz
            double f3 = 1500.0;  // 1500Hz

            double[] s1 = new double[numSamples];
            double[] s2 = new double[numSamples];
            double[] s3 = new double[numSamples];

            for (int i = 0; i < numSamples; i++)
            {
                s1[i] = Math.Sin(2.0 * Math.PI * f1 * time[i]);
                s2[i] = Math.Sin(2.0 * Math.PI * f2 * time[i]);
                s3[i] = Math.Sin(2.0 * Math.PI * f3 * time[i]);
            }

            // 合成信号
            double[] compositeSignal = new double[numSamples];
            for (int i = 0; i < numSamples; i++)
            {
                compositeSignal[i] = s1[i] + s2[i] + s3[i];
            }

            return compositeSignal;
        }

        /// <summary>
        /// 分析滤波器频率响应
        /// </summary>
        /// <param name="coefficients">滤波器系数</param>
        /// <param name="fs">采样率</param>
        private static void AnalyzeFilterResponse(double[] coefficients, double fs)
        {
            var (frequencies, magnitude, phase) = FIRFilterUtils.AnalyzeFrequencyResponse(coefficients, fs, 512);

            // 找到-3dB点
            double maxMagnitude = magnitude.Max();
            double threshold = maxMagnitude / Math.Sqrt(2.0); // -3dB点

            int cutoffIndex = 0;
            for (int i = 0; i < magnitude.Length; i++)
            {
                if (magnitude[i] <= threshold)
                {
                    cutoffIndex = i;
                    break;
                }
            }

            Console.WriteLine($"   滤波器系数数量: {coefficients.Length}");
            Console.WriteLine($"   最大增益: {maxMagnitude:F4}");
            Console.WriteLine($"   -3dB频率: {frequencies[cutoffIndex]:F1} Hz");
            Console.WriteLine($"   直流增益: {magnitude[0]:F4}");
        }

        /// <summary>
        /// 保存结果到文件
        /// </summary>
        /// <param name="originalSignal">原始信号</param>
        /// <param name="filteredSignal">滤波后信号</param>
        /// <param name="fs">采样率</param>
        private static void SaveResults(double[] originalSignal, double[] filteredSignal, double fs)
        {
            try
            {
                string filename = $"FIR_Filter_Results_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                using (StreamWriter writer = new StreamWriter(filename))
                {
                    writer.WriteLine("FIR滤波器结果");
                    writer.WriteLine($"采样率: {fs} Hz");
                    writer.WriteLine($"信号长度: {originalSignal.Length}");
                    writer.WriteLine();
                    writer.WriteLine("时间(s)\t原始信号\t滤波后信号");
                    writer.WriteLine("----------------------------------------");

                    for (int i = 0; i < Math.Min(originalSignal.Length, filteredSignal.Length); i++)
                    {
                        double time = i / fs;
                        writer.WriteLine($"{time:F4}\t{originalSignal[i]:F6}\t{filteredSignal[i]:F6}");
                    }
                }

                Console.WriteLine($"   结果已保存到: {filename}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   保存结果失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 演示不同滤波器类型
        /// </summary>
        public static void DemonstrateFilterTypes()
        {
            Console.WriteLine("\n=== 不同滤波器类型演示 ===\n");

            double fs = 1000.0;
            int order = 201;
            double[] testSignal = GenerateTestSignal(fs);

            // 低通滤波器
            Console.WriteLine("1. 低通滤波器 (截止频率: 200Hz)");
            FIRFilter lowPass = FIRFilterUtils.CreateLowPassFilter(fs, order, 200.0);
            double[] lowPassOutput = lowPass.ProcessSignalFull(testSignal);
            Console.WriteLine($"   输出信号长度: {lowPassOutput.Length}");

            // 高通滤波器
            Console.WriteLine("\n2. 高通滤波器 (截止频率: 500Hz)");
            FIRFilter highPass = FIRFilterUtils.CreateHighPassFilter(fs, order, 500.0);
            double[] highPassOutput = highPass.ProcessSignalFull(testSignal);
            Console.WriteLine($"   输出信号长度: {highPassOutput.Length}");

            // 带通滤波器
            Console.WriteLine("\n3. 带通滤波器 (300-700Hz)");
            FIRFilter bandPass = FIRFilterUtils.CreateBandPassFilter(fs, order, 300.0, 700.0);
            double[] bandPassOutput = bandPass.ProcessSignalFull(testSignal);
            Console.WriteLine($"   输出信号长度: {bandPassOutput.Length}");

            // 计算信号能量
            Console.WriteLine("\n4. 信号能量分析:");
            double originalEnergy = testSignal.Sum(x => x * x);
            double lowPassEnergy = lowPassOutput.Sum(x => x * x);
            double highPassEnergy = highPassOutput.Sum(x => x * x);
            double bandPassEnergy = bandPassOutput.Sum(x => x * x);

            Console.WriteLine($"   原始信号能量: {originalEnergy:F4}");
            Console.WriteLine($"   低通滤波后能量: {lowPassEnergy:F4} ({lowPassEnergy/originalEnergy*100:F1}%)");
            Console.WriteLine($"   高通滤波后能量: {highPassEnergy:F4} ({highPassEnergy/originalEnergy*100:F1}%)");
            Console.WriteLine($"   带通滤波后能量: {bandPassEnergy:F4} ({bandPassEnergy/originalEnergy*100:F1}%)");
        }

        /// <summary>
        /// 性能测试
        /// </summary>
        public static void PerformanceTest()
        {
            Console.WriteLine("\n=== 性能测试 ===\n");

            double fs = 1000.0;
            int[] orders = { 51, 101, 201, 501, 1001 };
            double[] testSignal = GenerateTestSignal(fs);

            foreach (int order in orders)
            {
                Console.WriteLine($"测试滤波器阶数: {order}");
                
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                
                FIRFilter filter = FIRFilterUtils.CreateLowPassFilter(fs, order, 200.0);
                double[] output = filter.ProcessSignalFull(testSignal);
                
                stopwatch.Stop();
                
                Console.WriteLine($"   处理时间: {stopwatch.ElapsedMilliseconds} ms");
                Console.WriteLine($"   输出信号长度: {output.Length}");
                Console.WriteLine();
            }
        }
    }
}

