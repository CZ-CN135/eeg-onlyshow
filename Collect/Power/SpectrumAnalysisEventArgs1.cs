using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Collect.Power
{
    public  class SpectrumAnalysisEventArgs1: EventArgs
    {
        /// <summary>
        /// 通道索引
        /// </summary>
        public int ChannelIndex { get; set; }

        /// <summary>
        /// 通道名称
        /// </summary>
        public string ChannelName { get; set; }

        /// <summary>
        /// 频率数组
        /// </summary>
        public double[] Frequencies { get; set; }

        /// <summary>
        /// 功率谱密度数组
        /// </summary>
        public double[] PowerSpectralDensity { get; set; }

        /// <summary>
        /// 平滑后的功率谱密度
        /// </summary>
        public double[] SmoothedPSD { get; set; }

        /// <summary>
        /// 绝对功率字典
        /// </summary>
        public Dictionary<PowerSpectralDensityAnalyzer.FrequencyBand, double> AbsolutePower { get; set; }

        /// <summary>
        /// 相对功率字典
        /// </summary>
        public Dictionary<PowerSpectralDensityAnalyzer.FrequencyBand, double> RelativePower { get; set; }

        /// <summary>
        /// 总功率
        /// </summary>
        public double TotalPower { get; set; }

        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public SpectrumAnalysisEventArgs1(int channelIndex, string channelName, 
            double[] frequencies, double[] psd, double[] smoothedPSD,
            Dictionary<PowerSpectralDensityAnalyzer.FrequencyBand, double> absolutePower,
            Dictionary<PowerSpectralDensityAnalyzer.FrequencyBand, double> relativePower,
            double totalPower)
        {
            ChannelIndex = channelIndex;
            ChannelName = channelName;
            Frequencies = frequencies;
            PowerSpectralDensity = psd;
            SmoothedPSD = smoothedPSD;
            AbsolutePower = absolutePower;
            RelativePower = relativePower;
            TotalPower = totalPower;
            Timestamp = DateTime.Now;
        }
    }
    /// <summary>
    /// 频谱分析事件委托
    /// </summary>
    public delegate void SpectrumAnalysisEventHandler(object sender, SpectrumAnalysisEventArgs1 e);
}
