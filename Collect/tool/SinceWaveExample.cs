using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Collect.tool
{
    /// <summary>
    /// SinceWave类的使用示例
    /// </summary>
    public class SinceWaveExample
    {
        /// <summary>
        /// 演示基本用法
        /// </summary>
        public static void BasicUsageExample()
        {
            Console.WriteLine("=== 基本用法示例 ===");
            
            // 创建10Hz的正弦波
            SinceWave sineWave = new SinceWave(10.0);
            Console.WriteLine(sineWave.ToString());
            
            // 生成1000个样本点
            double[] waveform = sineWave.GenerateWaveform(1000);
            Console.WriteLine($"生成了 {waveform.Length} 个样本点");
            Console.WriteLine($"前5个值: {string.Join(", ", waveform.Take(5).Select(x => x.ToString("F3"))}");
            
            // 获取时间轴
            double[] timeAxis = sineWave.GetTimeAxis(1000);
            Console.WriteLine($"时间轴范围: {timeAxis[0]:F3}s 到 {timeAxis[999]:F3}s");
            
            // 计算周期数
            double periodCount = sineWave.GetPeriodCount(1000);
            Console.WriteLine($"包含 {periodCount:F2} 个完整周期");
        }

        /// <summary>
        /// 演示参数设置
        /// </summary>
        public static void ParameterSettingExample()
        {
            Console.WriteLine("\n=== 参数设置示例 ===");
            
            // 创建自定义参数的正弦波
            SinceWave customWave = new SinceWave(50.0, 2.5, Math.PI/4, 2000.0, 1.0);
            Console.WriteLine($"自定义正弦波: {customWave}");
            
            // 修改频率
            customWave.SetFrequency(25.0);
            Console.WriteLine($"修改频率后: {customWave}");
            
            // 修改采样率
            customWave.SetSamplingRate(1000.0);
            Console.WriteLine($"修改采样率后: {customWave}");
            
            // 生成波形并显示
            double[] waveform = customWave.GenerateWaveform(500);
            Console.WriteLine($"生成了 {waveform.Length} 个样本点");
        }

        /// <summary>
        /// 演示复合波形生成
        /// </summary>
        public static void CompositeWaveformExample()
        {
            Console.WriteLine("\n=== 复合波形示例 ===");
            
            // 定义多个频率成分
            double[] frequencies = { 10.0, 30.0, 50.0 };  // 10Hz, 30Hz, 50Hz
            double[] amplitudes = { 1.0, 0.5, 0.3 };      // 对应幅度
            double[] phases = { 0.0, Math.PI/6, Math.PI/3 }; // 对应相位
            
            // 生成复合波形
            double[] compositeWave = SinceWave.GenerateCompositeWaveform(
                frequencies, amplitudes, phases, 1000, 1000.0);
            
            Console.WriteLine($"生成了复合波形，包含 {frequencies.Length} 个频率成分");
            Console.WriteLine($"前10个值: {string.Join(", ", compositeWave.Take(10).Select(x => x.ToString("F3"))}");
        }

        /// <summary>
        /// 演示带噪声的波形
        /// </summary>
        public static void NoisyWaveformExample()
        {
            Console.WriteLine("\n=== 带噪声波形示例 ===");
            
            // 创建20Hz的正弦波
            SinceWave cleanWave = new SinceWave(20.0, 1.0);
            
            // 生成带噪声的波形
            double[] noisyWave = cleanWave.GenerateNoisyWaveform(1000, 0.1);
            
            Console.WriteLine($"生成了带噪声的波形，噪声幅度: 0.1");
            Console.WriteLine($"前10个值: {string.Join(", ", noisyWave.Take(10).Select(x => x.ToString("F3"))}");
        }

        /// <summary>
        /// 演示不同频率的波形
        /// </summary>
        public static void DifferentFrequenciesExample()
        {
            Console.WriteLine("\n=== 不同频率波形示例 ===");
            
            // 测试不同频率
            double[] testFrequencies = { 1.0, 5.0, 10.0, 25.0, 50.0 };
            
            foreach (double freq in testFrequencies)
            {
                SinceWave wave = new SinceWave(freq, 1.0);
                double[] waveform = wave.GenerateWaveform(1000);
                
                // 计算实际周期数
                double actualPeriods = wave.GetPeriodCount(1000);
                Console.WriteLine($"频率 {freq:F1}Hz: 包含 {actualPeriods:F2} 个周期");
            }
        }

        /// <summary>
        /// 演示错误处理
        /// </summary>
        public static void ErrorHandlingExample()
        {
            Console.WriteLine("\n=== 错误处理示例 ===");
            
            try
            {
                // 尝试设置无效频率
                SinceWave wave = new SinceWave(10.0);
                wave.SetFrequency(-5.0); // 负数频率
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"捕获到错误: {ex.Message}");
            }
            
            try
            {
                // 尝试设置超过奈奎斯特频率的频率
                SinceWave wave = new SinceWave(10.0, 1.0, 0.0, 1000.0);
                wave.SetFrequency(600.0); // 超过500Hz的奈奎斯特频率
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"捕获到错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 主演示方法
        /// </summary>
        public static void RunAllExamples()
        {
            BasicUsageExample();
            ParameterSettingExample();
            CompositeWaveformExample();
            NoisyWaveformExample();
            DifferentFrequenciesExample();
            ErrorHandlingExample();
        }
    }
}

