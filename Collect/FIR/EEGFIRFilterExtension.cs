using System;
using System.Linq;
using Collect.Plot;
using System.Collections.Generic; // Added for List and Dictionary

namespace Collect.FIR
{
    /// <summary>
    /// EEG信号FIR滤波器扩展类
    /// </summary>
    public static class EEGFIRFilterExtension
    {
        /// <summary>
        /// 为EEG信号创建工频干扰滤波器（50Hz陷波器）
        /// </summary>
        /// <param name="fs">采样率</param>
        /// <param name="order">滤波器阶数</param>
        /// <returns>工频干扰滤波器</returns>
        public static FIRFilter CreatePowerLineFilter(double fs, int order = 201)
        {
            // 创建带阻滤波器，滤除50Hz工频干扰
            // 带阻范围：45-55Hz
            return FIRFilterUtils.CreateBandPassFilter(fs, order, 55.0, fs/2 - 55.0);
        }

        /// <summary>
        /// 为EEG信号创建低通滤波器（滤除高频噪声）
        /// </summary>
        /// <param name="fs">采样率</param>
        /// <param name="order">滤波器阶数</param>
        /// <param name="cutoffFreq">截止频率，默认100Hz</param>
        /// <returns>低通滤波器</returns>
        public static FIRFilter CreateEEGLowPassFilter(double fs, int order = 201, double cutoffFreq = 100.0)
        {
            return FIRFilterUtils.CreateLowPassFilter(fs, order, cutoffFreq);
        }

        /// <summary>
        /// 为EEG信号创建高通滤波器（去除基线漂移）
        /// </summary>
        /// <param name="fs">采样率</param>
        /// <param name="order">滤波器阶数</param>
        /// <param name="cutoffFreq">截止频率，默认0.5Hz</param>
        /// <returns>高通滤波器</returns>
        public static FIRFilter CreateEEGHighPassFilter(double fs, int order = 201, double cutoffFreq = 0.5)
        {
            return FIRFilterUtils.CreateHighPassFilter(fs, order, cutoffFreq);
        }

        /// <summary>
        /// 为EEG信号创建带通滤波器（提取特定频段）
        /// </summary>
        /// <param name="fs">采样率</param>
        /// <param name="order">滤波器阶数</param>
        /// <param name="lowFreq">低频截止</param>
        /// <param name="highFreq">高频截止</param>
        /// <returns>带通滤波器</returns>
        public static FIRFilter CreateEEGBandPassFilter(double fs, int order = 201, double lowFreq = 1.0, double highFreq = 50.0)
        {
            return FIRFilterUtils.CreateBandPassFilter(fs, order, lowFreq, highFreq);
        }

        /// <summary>
        /// 对EEG信号进行FIR滤波处理
        /// </summary>
        /// <param name="eegData">EEG数据</param>
        /// <param name="fs">采样率</param>
        /// <param name="filterType">滤波器类型</param>
        /// <param name="parameters">滤波器参数</param>
        /// <returns>滤波后的EEG数据</returns>
        public static double[] ApplyFIRFilter(this double[] eegData, double fs, string filterType, params double[] parameters)
        {
            try
            {
                FIRFilter filter = null;
                int order = 201; // 默认阶数

                switch (filterType.ToLower())
                {
                    case "lowpass":
                    case "low":
                        double lowCutoff = parameters.Length > 0 ? parameters[0] : 100.0;
                        order = parameters.Length > 1 ? (int)parameters[1] : 201;
                        filter = CreateEEGLowPassFilter(fs, order, lowCutoff);
                        break;

                    case "highpass":
                    case "high":
                        double highCutoff = parameters.Length > 0 ? parameters[0] : 0.5;
                        order = parameters.Length > 1 ? (int)parameters[1] : 201;
                        filter = CreateEEGHighPassFilter(fs, order, highCutoff);
                        break;

                    case "bandpass":
                    case "band":
                        double lowFreq = parameters.Length > 0 ? parameters[0] : 1.0;
                        double highFreq = parameters.Length > 1 ? parameters[1] : 50.0;
                        order = parameters.Length > 2 ? (int)parameters[2] : 201;
                        filter = CreateEEGBandPassFilter(fs, order, lowFreq, highFreq);
                        break;

                    case "powerline":
                    case "notch":
                        order = parameters.Length > 0 ? (int)parameters[0] : 201;
                        filter = CreatePowerLineFilter(fs, order);
                        break;

                    default:
                        throw new ArgumentException($"不支持的滤波器类型: {filterType}");
                }

                if (filter != null)
                {
                    return filter.ProcessSignalFull(eegData);
                }

                return eegData;
            }
            catch (Exception ex)
            {
                LogHelper.WriteErrorLog($"FIR滤波失败: {ex.Message}");
                NlogHelper.WriteErrorLog($"FIR滤波失败: {ex.Message}");
                return eegData; // 返回原始数据
            }
        }

        /// <summary>
        /// 对EEG信号进行多级FIR滤波处理
        /// </summary>
        /// <param name="eegData">EEG数据</param>
        /// <param name="fs">采样率</param>
        /// <param name="filterChain">滤波器链配置</param>
        /// <returns>滤波后的EEG数据</returns>
        public static double[] ApplyMultiStageFIRFilter(this double[] eegData, double fs, FilterChainConfig filterChain)
        {
            try
            {
                double[] filteredData = eegData.Clone() as double[];

                foreach (var filterConfig in filterChain.Filters)
                {
                    filteredData = filteredData.ApplyFIRFilter(fs, filterConfig.Type, filterConfig.Parameters);
                    
                    LogHelper.WriteInfoLog($"应用{filterConfig.Type}滤波器完成");
                    NlogHelper.WriteInfoLog($"应用{filterConfig.Type}滤波器完成");
                }

                return filteredData;
            }
            catch (Exception ex)
            {
                LogHelper.WriteErrorLog($"多级FIR滤波失败: {ex.Message}");
                NlogHelper.WriteErrorLog($"多级FIR滤波失败: {ex.Message}");
                return eegData;
            }
        }

        /// <summary>
        /// 创建标准EEG预处理滤波器链
        /// </summary>
        /// <returns>标准滤波器链配置</returns>
        public static FilterChainConfig CreateStandardEEGFilterChain()
        {
            var filterChain = new FilterChainConfig();

            // 1. 高通滤波器：去除基线漂移
            filterChain.AddFilter("highpass", 0.5, 201);

            // 2. 低通滤波器：去除高频噪声
            filterChain.AddFilter("lowpass", 100.0, 201);

            // 3. 工频陷波器：去除50Hz工频干扰
            filterChain.AddFilter("notch", 201);

            return filterChain;
        }

        /// <summary>
        /// 创建特定频段提取滤波器链
        /// </summary>
        /// <param name="lowFreq">低频截止</param>
        /// <param name="highFreq">高频截止</param>
        /// <returns>频段提取滤波器链</returns>
        public static FilterChainConfig CreateBandExtractionFilterChain(double lowFreq, double highFreq)
        {
            var filterChain = new FilterChainConfig();

            // 1. 高通滤波器：去除基线漂移
            filterChain.AddFilter("highpass", 0.5, 201);

            // 2. 带通滤波器：提取目标频段
            filterChain.AddFilter("bandpass", lowFreq, highFreq, 201);

            return filterChain;
        }
    }

    /// <summary>
    /// 滤波器配置类
    /// </summary>
    public class FilterConfig
    {
        public string Type { get; set; }
        public double[] Parameters { get; set; }

        public FilterConfig(string type, params double[] parameters)
        {
            Type = type;
            Parameters = parameters;
        }
    }

    /// <summary>
    /// 滤波器链配置类
    /// </summary>
    public class FilterChainConfig
    {
        public List<FilterConfig> Filters { get; private set; }

        public FilterChainConfig()
        {
            Filters = new List<FilterConfig>();
        }

        public void AddFilter(string type, params double[] parameters)
        {
            Filters.Add(new FilterConfig(type, parameters));
        }

        public void Clear()
        {
            Filters.Clear();
        }
    }

    /// <summary>
    /// FIR滤波器性能监控类
    /// </summary>
    public class FIRFilterMonitor
    {
        private Dictionary<string, long> processingTimes;
        private Dictionary<string, int> filterUsage;

        public FIRFilterMonitor()
        {
            processingTimes = new Dictionary<string, long>();
            filterUsage = new Dictionary<string, int>();
        }

        /// <summary>
        /// 记录滤波器处理时间
        /// </summary>
        /// <param name="filterType">滤波器类型</param>
        /// <param name="processingTime">处理时间(ms)</param>
        public void RecordProcessingTime(string filterType, long processingTime)
        {
            if (!processingTimes.ContainsKey(filterType))
            {
                processingTimes[filterType] = 0;
                filterUsage[filterType] = 0;
            }

            processingTimes[filterType] += processingTime;
            filterUsage[filterType]++;
        }

        /// <summary>
        /// 获取滤波器性能统计
        /// </summary>
        /// <returns>性能统计信息</returns>
        public Dictionary<string, object> GetPerformanceStats()
        {
            var stats = new Dictionary<string, object>();

            foreach (var filterType in processingTimes.Keys)
            {
                var avgTime = filterUsage[filterType] > 0 ? 
                    (double)processingTimes[filterType] / filterUsage[filterType] : 0;

                stats[$"{filterType}_TotalTime"] = processingTimes[filterType];
                stats[$"{filterType}_UsageCount"] = filterUsage[filterType];
                stats[$"{filterType}_AverageTime"] = avgTime;
            }

            return stats;
        }

        /// <summary>
        /// 重置性能统计
        /// </summary>
        public void Reset()
        {
            processingTimes.Clear();
            filterUsage.Clear();
        }
    }
}

