using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Collect.tool
{
    /// <summary>
    /// 正弦波生成器类
    /// </summary>
    public class SinceWave
    {
        /// <summary>
        /// 频率 (Hz)
        /// </summary>
        public double Frequency { get; set; }
        
        /// <summary>
        /// 幅度
        /// </summary>
        public double Amplitude { get; set; }
        
        /// <summary>
        /// 相位 (弧度)
        /// </summary>
        public double Phase { get; set; }
        
        /// <summary>
        /// 采样率 (Hz)
        /// </summary>
        public double SamplingRate { get; set; }
        
        /// <summary>
        /// 直流偏移
        /// </summary>
        public double DC_Offset { get; set; }

        /// <summary>
        /// 默认构造函数
        /// </summary>
        public SinceWave()
        {
            Frequency = 10.0;      // 默认10Hz
            Amplitude = 0.5;       // 默认幅度1.0
            Phase = 0.0;           // 默认相位0
            SamplingRate = 1000.0; // 默认采样率1000Hz
            DC_Offset = 0.0;       // 默认无直流偏移
        }

        /// <summary>
        /// 带频率参数的构造函数
        /// </summary>
        /// <param name="frequency">频率 (Hz)</param>
        public SinceWave(double frequency)
        {
            Frequency = frequency;
            Amplitude = 1.0;
            Phase = 0.0;
            SamplingRate = 1000.0;
            DC_Offset = 0.0;
        }

        /// <summary>
        /// 完整参数构造函数
        /// </summary>
        /// <param name="frequency">频率 (Hz)</param>
        /// <param name="amplitude">幅度</param>
        /// <param name="phase">相位 (弧度)</param>
        /// <param name="samplingRate">采样率 (Hz)</param>
        /// <param name="dcOffset">直流偏移</param>
        public SinceWave(double frequency, double amplitude, double phase = 0.0, double samplingRate = 1000.0, double dcOffset = 0.0)
        {
            Frequency = frequency;
            Amplitude = amplitude;
            Phase = phase;
            SamplingRate = samplingRate;
            DC_Offset = dcOffset;
        }

        /// <summary>
        /// 生成单个正弦波样本
        /// </summary>
        /// <param name="timeIndex">时间索引</param>
        /// <returns>正弦波值</returns>
        public double GenerateSample(int timeIndex)
        {
            double time = (double)timeIndex / SamplingRate;
            double value = Amplitude * Math.Sin(2 * Math.PI * Frequency * time + Phase) + DC_Offset;
            return value;
        }

        /// <summary>
        /// 生成指定长度的正弦波数据
        /// </summary>
        /// <param name="sampleCount">样本数量</param>
        /// <returns>正弦波数据数组</returns>
        public double[] GenerateWaveform(int sampleCount)
        {
            double[] waveform = new double[sampleCount];
            
            for (int i = 0; i < sampleCount; i++)
            {
                waveform[i] = GenerateSample(i);
            }
            
            return waveform;
        }

        /// <summary>
        /// 生成指定时长的正弦波数据
        /// </summary>
        /// <param name="durationSeconds">持续时间 (秒)</param>
        /// <returns>正弦波数据数组</returns>
        public double[] GenerateWaveformByDuration(double durationSeconds)
        {
            int sampleCount = (int)(durationSeconds * SamplingRate);
            return GenerateWaveform(sampleCount);
        }

        /// <summary>
        /// 生成复合正弦波（多个频率叠加）
        /// </summary>
        /// <param name="frequencies">频率数组 (Hz)</param>
        /// <param name="amplitudes">对应幅度数组</param>
        /// <param name="phases">对应相位数组 (弧度)</param>
        /// <param name="sampleCount">样本数量</param>
        /// <returns>复合正弦波数据数组</returns>
        public static double[] GenerateCompositeWaveform(double[] frequencies, double[] amplitudes, double[] phases, int sampleCount, double samplingRate = 1000.0)
        {
            if (frequencies.Length != amplitudes.Length || frequencies.Length != phases.Length)
            {
                throw new ArgumentException("频率、幅度和相位数组长度必须相同");
            }

            double[] compositeWaveform = new double[sampleCount];
            
            for (int i = 0; i < sampleCount; i++)
            {
                double time = (double)i / samplingRate;
                double sum = 0.0;
                
                for (int j = 0; j < frequencies.Length; j++)
                {
                    sum += amplitudes[j] * Math.Sin(2 * Math.PI * frequencies[j] * time + phases[j]);
                }
                
                compositeWaveform[i] = sum;
            }
            
            return compositeWaveform;
        }

        /// <summary>
        /// 生成带噪声的正弦波
        /// </summary>
        /// <param name="sampleCount">样本数量</param>
        /// <param name="noiseAmplitude">噪声幅度</param>
        /// <returns>带噪声的正弦波数据数组</returns>
        public double[] GenerateNoisyWaveform(int sampleCount, double noiseAmplitude)
        {
            double[] waveform = GenerateWaveform(sampleCount);
            Random random = new Random();
            
            for (int i = 0; i < sampleCount; i++)
            {
                double noise = (random.NextDouble() - 0.5) * 2 * noiseAmplitude;
                waveform[i] += noise;
            }
            
            return waveform;
        }

        /// <summary>
        /// 获取正弦波的周期数
        /// </summary>
        /// <param name="sampleCount">样本数量</param>
        /// <returns>周期数</returns>
        public double GetPeriodCount(int sampleCount)
        {
            double totalTime = (double)sampleCount / SamplingRate;
            return totalTime * Frequency;
        }

        /// <summary>
        /// 设置频率并验证参数
        /// </summary>
        /// <param name="frequency">新频率 (Hz)</param>
        public void SetFrequency(double frequency)
        {
            if (frequency <= 0)
            {
                throw new ArgumentException("频率必须大于0");
            }
            
            if (frequency >= SamplingRate / 2)
            {
                throw new ArgumentException("频率不能超过奈奎斯特频率 (采样率/2)");
            }
            
            Frequency = frequency;
        }

        /// <summary>
        /// 设置采样率并验证参数
        /// </summary>
        /// <param name="samplingRate">新采样率 (Hz)</param>
        public void SetSamplingRate(double samplingRate)
        {
            if (samplingRate <= 0)
            {
                throw new ArgumentException("采样率必须大于0");
            }
            
            if (Frequency >= samplingRate / 2)
            {
                throw new ArgumentException("当前频率超过新采样率的奈奎斯特频率");
            }
            
            SamplingRate = samplingRate;
        }

        /// <summary>
        /// 获取时间轴数据
        /// </summary>
        /// <param name="sampleCount">样本数量</param>
        /// <returns>时间轴数组 (秒)</returns>
        public double[] GetTimeAxis(int sampleCount)
        {
            double[] timeAxis = new double[sampleCount];
            
            for (int i = 0; i < sampleCount; i++)
            {
                timeAxis[i] = (double)i / SamplingRate;
            }
            
            return timeAxis;
        }

        /// <summary>
        /// 转换为字符串表示
        /// </summary>
        /// <returns>字符串表示</returns>
        public override string ToString()
        {
            return $"正弦波: 频率={Frequency:F2}Hz, 幅度={Amplitude:F2}, 相位={Phase:F2}rad, 采样率={SamplingRate:F0}Hz, 直流偏移={DC_Offset:F2}";
        }
    }
}
