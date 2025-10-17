using System;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;

namespace Collect.FIR
{
    /// <summary>
    /// FIR滤波器类型枚举
    /// </summary>
    public enum FilterType
    {
        LowPass,    // 低通滤波器
        HighPass,   // 高通滤波器
        BandPass    // 带通滤波器
    }

    /// <summary>
    /// FIR滤波器系数生成器
    /// </summary>
    public class FIRCoefficientsGenerator
    {
        /// <summary>
        /// 生成FIR滤波器系数
        /// </summary>
        /// <param name="fs">采样率(Hz)</param>
        /// <param name="order">滤波器阶数</param>
        /// <param name="filterType">滤波器类型</param>
        /// <param name="fc1">截止频率1(Hz)</param>
        /// <param name="fc2">截止频率2(Hz)，仅带通滤波器需要</param>
        /// <returns>滤波器系数数组</returns>
        public static double[] GenerateCoefficients(double fs, int order, FilterType filterType, double fc1, double fc2 = 0)
        {
            // 确保阶数为奇数
            if (order % 2 == 0)
            {
                order += 1;
            }

            // 归一化截止频率
            double normalizedFc1 = fc1 / (fs / 2.0);
            double normalizedFc2 = fc2 / (fs / 2.0);

            // 生成Blackman窗函数
            double[] window = GenerateBlackmanWindow(order + 1);

            double[] coefficients;

            switch (filterType)
            {
                case FilterType.LowPass:
                    coefficients = GenerateLowPassCoefficients(order, normalizedFc1, window);
                    break;
                case FilterType.HighPass:
                    coefficients = GenerateHighPassCoefficients(order, normalizedFc1, window);
                    break;
                case FilterType.BandPass:
                    if (fc2 <= 0)
                        throw new ArgumentException("带通滤波器需要两个截止频率");
                    coefficients = GenerateBandPassCoefficients(order, normalizedFc1, normalizedFc2, window);
                    break;
                default:
                    throw new ArgumentException("不支持的滤波器类型");
            }

            // 应用窗函数
            for (int i = 0; i < coefficients.Length; i++)
            {
                coefficients[i] *= window[i];
            }

            return coefficients;
        }

        /// <summary>
        /// 生成Blackman窗函数
        /// </summary>
        /// <param name="length">窗长度</param>
        /// <returns>窗函数数组</returns>
        private static double[] GenerateBlackmanWindow(int length)
        {
            double[] window = new double[length];
            for (int i = 0; i < length; i++)
            {
                double x = 2.0 * Math.PI * i / (length - 1);
                window[i] = 0.42 - 0.5 * Math.Cos(x) + 0.08 * Math.Cos(2 * x);
            }
            return window;
        }

        /// <summary>
        /// 生成低通滤波器系数
        /// </summary>
        private static double[] GenerateLowPassCoefficients(int order, double normalizedFc, double[] window)
        {
            double[] coefficients = new double[order + 1];
            int center = order / 2;

            for (int i = 0; i <= order; i++)
            {
                if (i == center)
                {
                    coefficients[i] = 2.0 * normalizedFc;
                }
                else
                {
                    double x = Math.PI * (i - center);
                    coefficients[i] = Math.Sin(2.0 * normalizedFc * x) / x;
                }
            }

            return coefficients;
        }

        /// <summary>
        /// 生成高通滤波器系数
        /// </summary>
        private static double[] GenerateHighPassCoefficients(int order, double normalizedFc, double[] window)
        {
            double[] coefficients = new double[order + 1];
            int center = order / 2;

            for (int i = 0; i <= order; i++)
            {
                if (i == center)
                {
                    coefficients[i] = 1.0 - 2.0 * normalizedFc;
                }
                else
                {
                    double x = Math.PI * (i - center);
                    coefficients[i] = -Math.Sin(2.0 * normalizedFc * x) / x;
                }
            }

            return coefficients;
        }

        /// <summary>
        /// 生成带通滤波器系数
        /// </summary>
        private static double[] GenerateBandPassCoefficients(int order, double normalizedFc1, double normalizedFc2, double[] window)
        {
            double[] coefficients = new double[order + 1];
            int center = order / 2;

            for (int i = 0; i <= order; i++)
            {
                if (i == center)
                {
                    coefficients[i] = 2.0 * (normalizedFc2 - normalizedFc1);
                }
                else
                {
                    double x = Math.PI * (i - center);
                    coefficients[i] = (Math.Sin(2.0 * normalizedFc2 * x) - Math.Sin(2.0 * normalizedFc1 * x)) / x;
                }
            }

            return coefficients;
        }
    }

    /// <summary>
    /// FIR滤波器实现类
    /// </summary>
    public class FIRFilter
    {
        private double[] coefficients;
        private double[] buffer;
        private int bufferIndex;
        private int filterOrder;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="coefficients">滤波器系数</param>
        public FIRFilter(double[] coefficients)
        {
            this.coefficients = coefficients.Clone() as double[];
            this.filterOrder = coefficients.Length - 1;
            this.buffer = new double[filterOrder];
            this.bufferIndex = 0;
        }

        /// <summary>
        /// 使用系数文件创建滤波器
        /// </summary>
        /// <param name="coefficients">滤波器系数</param>
        /// <param name="order">滤波器阶数</param>
        /// <returns>FIR滤波器实例</returns>
        public static FIRFilter CreateFromCoefficients(double[] coefficients, int order)
        {
            if (order % 2 == 0)
                throw new ArgumentException("滤波器阶数必须为奇数");

            if (coefficients.Length != order + 1)
                throw new ArgumentException("系数数量与阶数不匹配");

            return new FIRFilter(coefficients);
        }

        /// <summary>
        /// 重置滤波器状态
        /// </summary>
        public void Reset()
        {
            Array.Clear(buffer, 0, buffer.Length);
            bufferIndex = 0;
        }

        /// <summary>
        /// 处理单个样本
        /// </summary>
        /// <param name="input">输入样本</param>
        /// <returns>输出样本</returns>
        public double ProcessSample(double input)
        {
            // 更新缓冲区
            buffer[bufferIndex] = input;

            // 计算卷积
            double output = 0.0;
            int coeffIndex = 0;

            for (int i = 0; i < filterOrder; i++)
            {
                int bufferPos = (bufferIndex - i + filterOrder) % filterOrder;
                output += coefficients[coeffIndex] * buffer[bufferPos];
                coeffIndex++;
            }

            // 更新缓冲区索引
            bufferIndex = (bufferIndex + 1) % filterOrder;

            return output;
        }

        /// <summary>
        /// 处理信号数组
        /// </summary>
        /// <param name="input">输入信号数组</param>
        /// <returns>输出信号数组</returns>
        public double[] ProcessSignal(double[] input)
        {
            double[] output = new double[input.Length];
            int delay = filterOrder / 2;

            // 处理信号
            for (int i = 0; i < input.Length; i++)
            {
                output[i] = ProcessSample(input[i]);
            }

            // 去除延迟部分
            double[] result = new double[input.Length - delay];
            Array.Copy(output, delay, result, 0, result.Length);

            return result;
        }

        /// <summary>
        /// 处理信号数组（保持原始长度）
        /// </summary>
        /// <param name="input">输入信号数组</param>
        /// <returns>输出信号数组</returns>
        public double[] ProcessSignalFull(double[] input)
        {
            double[] output = new double[input.Length];
            
            // 重置滤波器状态
            Reset();

            // 处理信号
            for (int i = 0; i < input.Length; i++)
            {
                output[i] = ProcessSample(input[i]);
            }

            return output;
        }

        /// <summary>
        /// 获取滤波器系数
        /// </summary>
        /// <returns>滤波器系数数组</returns>
        public double[] GetCoefficients()
        {
            return coefficients.Clone() as double[];
        }

        /// <summary>
        /// 获取滤波器阶数
        /// </summary>
        /// <returns>滤波器阶数</returns>
        public int GetOrder()
        {
            return filterOrder;
        }
    }

    /// <summary>
    /// FIR滤波器工具类
    /// </summary>
    public static class FIRFilterUtils
    {
        /// <summary>
        /// 创建低通滤波器
        /// </summary>
        /// <param name="fs">采样率(Hz)</param>
        /// <param name="order">滤波器阶数</param>
        /// <param name="cutoffFreq">截止频率(Hz)</param>
        /// <returns>FIR滤波器实例</returns>
        public static FIRFilter CreateLowPassFilter(double fs, int order, double cutoffFreq)
        {
            double[] coefficients = FIRCoefficientsGenerator.GenerateCoefficients(fs, order, FilterType.LowPass, cutoffFreq);
            return new FIRFilter(coefficients);
        }

        /// <summary>
        /// 创建高通滤波器
        /// </summary>
        /// <param name="fs">采样率(Hz)</param>
        /// <param name="order">滤波器阶数</param>
        /// <param name="cutoffFreq">截止频率(Hz)</param>
        /// <returns>FIR滤波器实例</returns>
        public static FIRFilter CreateHighPassFilter(double fs, int order, double cutoffFreq)
        {
            double[] coefficients = FIRCoefficientsGenerator.GenerateCoefficients(fs, order, FilterType.HighPass, cutoffFreq);
            return new FIRFilter(coefficients);
        }

        /// <summary>
        /// 创建带通滤波器
        /// </summary>
        /// <param name="fs">采样率(Hz)</param>
        /// <param name="order">滤波器阶数</param>
        /// <param name="lowCutoffFreq">低截止频率(Hz)</param>
        /// <param name="highCutoffFreq">高截止频率(Hz)</param>
        /// <returns>FIR滤波器实例</returns>
        public static FIRFilter CreateBandPassFilter(double fs, int order, double lowCutoffFreq, double highCutoffFreq)
        {
            double[] coefficients = FIRCoefficientsGenerator.GenerateCoefficients(fs, order, FilterType.BandPass, lowCutoffFreq, highCutoffFreq);
            return new FIRFilter(coefficients);
        }

        /// <summary>
        /// 分析滤波器频率响应
        /// </summary>
        /// <param name="coefficients">滤波器系数</param>
        /// <param name="fs">采样率</param>
        /// <param name="freqPoints">频率点数</param>
        /// <returns>频率响应数据</returns>
        public static (double[] frequencies, double[] magnitude, double[] phase) AnalyzeFrequencyResponse(double[] coefficients, double fs, int freqPoints = 1024)
        {
            double[] frequencies = new double[freqPoints];
            double[] magnitude = new double[freqPoints];
            double[] phase = new double[freqPoints];

            for (int i = 0; i < freqPoints; i++)
            {
                double freq = i * fs / (2.0 * freqPoints);
                frequencies[i] = freq;

                Complex h = 0;
                for (int j = 0; j < coefficients.Length; j++)
                {
                    double angle = -2.0 * Math.PI * freq * j / fs;
                    h += coefficients[j] * Complex.FromPolarCoordinates(1.0, angle);
                }

                magnitude[i] = h.Magnitude;
                phase[i] = h.Phase;
            }

            return (frequencies, magnitude, phase);
        }
    }
}

