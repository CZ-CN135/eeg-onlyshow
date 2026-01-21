using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Collect.tool
{
    public class SeizureDetector
    {
        public sealed class Config
        {
            public int ChannelCount = 8;// 通道数

            public double Fs = 500;          // 采样率 Hz
            public double WindowMs = 200;    // Stage1: 200ms
            public double StepMs = 50;       // Stage1: 50ms
            public double WarmupMs = 1000;   // 前 1s 不检测

            // Stage1 阈值（单位按你的输入 μV）
            public double RmsThreshold = 80;
            public double LlThreshold = 2000;

            public int MinChannelsToTrigger = 1;

            // Stage1 触发后是否停止继续检测（一般你做二级确认时建议 false）
            public bool StopAfterTrigger = false;

            public int QueueCapacity = 5000;

            // ===== [ADD] Stage2 窗口参数 =====
            // “向前”回看多少 ms（默认 400ms）
            public double Stage2LookbackMs = 400;

            // Stage2 总窗口长度 ms（默认 600ms）
            // 若设置小于 (Stage1Window + Lookback)，会自动抬升到该最小值
            public double Stage2WindowMs = 600;

            // 历史缓冲保存时长（必须 >= Stage2WindowMs，建议给点余量）
            // 默认 2000ms（2秒），足够稳定
            public double HistoryMs = 2000;

            // Stage2 候选窗口发送最小间隔（ms），防止阈值持续满足时疯狂发任务
            // 0 表示不限制；建议 100~200ms   //当前 winEnd - 上次 winEnd < 最小间隔
            public double Stage2EmitMinIntervalMs = 100;
        }

        public sealed class DetectionEventArgs : EventArgs
        {
            public long WindowStartSample;
            public long WindowEndSample;
            public double[] RmsPerChannel;
            public double[] LlPerChannel;
            public int PassedChannels;
        }

        // ===== [ADD] Stage2 数据块事件参数 =====
        public sealed class Stage2WindowEventArgs : EventArgs
        {
            public long WindowStartSample;
            public long WindowEndSample;
            public double[][] Window;   // [ch][n]
            public int Samples;
        }

        public event EventHandler<DetectionEventArgs> OnWindowEvaluated;
        public event EventHandler<DetectionEventArgs> OnSeizureTriggered;

        // ===== [ADD] Stage1 满足阈值时，把 600ms 数据块抛给二级检测 =====
        public event EventHandler<Stage2WindowEventArgs> OnStage2WindowReady;

        private readonly Config _cfg;
        private BlockingCollection<double[]> _queue;
        private CancellationTokenSource _cts;
        private Task _worker;

        // ===== Stage1 ring：只保存最近 Nwin 个点（原逻辑不改） =====
        private double[,] _ring;          // [ch, idx] len=_nWin
        private int _ringPos;             // 0.._nWin-1

        private int _nWin, _nStep, _nWarm;
        private long _totalSamples;       // 已接收总样本数（帧数）
        private long _nextEvalEndSample;
        private volatile bool _triggered;

        // ===== [ADD] 历史 ring：只用于 Stage2 提取 600ms，不影响 Stage1 =====
        private double[,] _histRing;      // [ch, idx] len=_nHist
        private int _histPos;             // 0.._nHist-1
        private int _nHist;               // 历史 ring 长度（样本点数）
        private long _lastSampleIndex;    // 最近写入的样本序号（curSampleIndex）
        private long _lastStage2EmitSample; // 上次抛 Stage2 窗口的末尾样本（限频）

        public SeizureDetector(Config cfg)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            ValidateAndInit();
        }

        private void ValidateAndInit()
        {
            if (_cfg.ChannelCount <= 0) throw new ArgumentException("ChannelCount must be > 0");
            if (_cfg.Fs <= 0) throw new ArgumentException("Fs must be > 0");
            if (_cfg.WindowMs <= 0) throw new ArgumentException("WindowMs must be > 0");
            if (_cfg.StepMs <= 0) throw new ArgumentException("StepMs must be > 0");
            if (_cfg.WarmupMs < 0) throw new ArgumentException("WarmupMs must be >= 0");

            _nWin = (int)Math.Round(_cfg.Fs * _cfg.WindowMs / 1000.0);
            _nStep = (int)Math.Round(_cfg.Fs * _cfg.StepMs / 1000.0);
            _nWarm = (int)Math.Round(_cfg.Fs * _cfg.WarmupMs / 1000.0);

            if (_nWin < 2) _nWin = 2;
            if (_nStep < 1) _nStep = 1;

            // Stage1 ring（原本就是这个）
            _ring = new double[_cfg.ChannelCount, _nWin];
            _ringPos = 0;

            // ===== [ADD] 历史 ring 初始化 =====
            int lookback = (int)Math.Round(_cfg.Fs * _cfg.Stage2LookbackMs / 1000.0);
            int nStage2 = (int)Math.Round(_cfg.Fs * _cfg.Stage2WindowMs / 1000.0);

            // 保证 Stage2 至少覆盖 Stage1Window + lookback
            int minStage2 = _nWin + Math.Max(0, lookback);
            if (nStage2 < minStage2) nStage2 = minStage2;

            int nHistByCfg = (int)Math.Round(_cfg.Fs * _cfg.HistoryMs / 1000.0);
            if (nHistByCfg < nStage2) nHistByCfg = nStage2;
            if (nHistByCfg < 16) nHistByCfg = 16;

            _nHist = nHistByCfg;
            _histRing = new double[_cfg.ChannelCount, _nHist];
            _histPos = 0;

            _totalSamples = 0;
            _lastSampleIndex = -1;
            _lastStage2EmitSample = long.MinValue;
            _triggered = false;

            // 第一次评估：窗口末尾样本 = warmup + window - 1
            _nextEvalEndSample = _nWarm + _nWin - 1;
        }

        public void Start()
        {
            if (_worker != null) return;

            _queue = new BlockingCollection<double[]>(new ConcurrentQueue<double[]>(), _cfg.QueueCapacity);
            _cts = new CancellationTokenSource();
            _worker = Task.Run(() => WorkerLoop(_cts.Token), _cts.Token);
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
                _queue?.CompleteAdding();
                _worker?.Wait(500);
            }
            catch { }
            finally
            {
                _worker = null;
                _cts?.Dispose(); _cts = null;
                _queue?.Dispose(); _queue = null;
            }
        }

        public void PushFrame(double[] ch8)
        {
            if (ch8 == null || ch8.Length != _cfg.ChannelCount) return;
            if (_queue == null || _queue.IsAddingCompleted) return;

            _queue.TryAdd(ch8);
        }

        private void WorkerLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && !_queue.IsCompleted)
            {
                double[] frame = null;
                try
                {
                    if (!_queue.TryTake(out frame, 50, ct)) continue;
                }
                catch (OperationCanceledException) { break; }

                if (frame == null) continue;

                // 当前帧的样本序号
                long curSampleIndex = _totalSamples;

                // ===== 1) 写入 Stage1 ring（原逻辑）=====
                for (int ch = 0; ch < _cfg.ChannelCount; ch++)
                    _ring[ch, _ringPos] = frame[ch];

                _ringPos++;
                if (_ringPos >= _nWin) _ringPos = 0;

                // ===== [ADD] 写入历史 ring（仅供 Stage2 提取）=====
                for (int ch = 0; ch < _cfg.ChannelCount; ch++)
                    _histRing[ch, _histPos] = frame[ch];

                _histPos++;
                if (_histPos >= _nHist) _histPos = 0;

                _lastSampleIndex = curSampleIndex;
                _totalSamples++;

                // 触发后是否停止 Stage1 继续检测
                if (_triggered && _cfg.StopAfterTrigger) continue;

                // ===== 2) 到点评估 Stage1 =====
                if (curSampleIndex >= _nextEvalEndSample)
                {
                    long winEnd = _nextEvalEndSample;
                    long winStart = winEnd - _nWin + 1;

                    EvaluateCurrentWindow(winStart, winEnd);

                    _nextEvalEndSample += _nStep;
                }
            }
        }

        private void EvaluateCurrentWindow(long winStart, long winEnd)
        {
            var rms = new double[_cfg.ChannelCount];
            var ll = new double[_cfg.ChannelCount];

            int passed = 0;
            int oldestPos = _ringPos; // Stage1 ring 最老位置（原逻辑）

            for (int ch = 0; ch < _cfg.ChannelCount; ch++)
            {
                double sumSq = 0.0;
                double sumAbsDiff = 0.0;

                double prev = ReadRing(ch, oldestPos);
                sumSq += prev * prev;

                for (int k = 1; k < _nWin; k++)
                {
                    double x = ReadRing(ch, oldestPos + k);
                    sumSq += x * x;
                    sumAbsDiff += Math.Abs(x - prev);
                    prev = x;
                }

                double r = Math.Sqrt(sumSq / _nWin);
                double l = sumAbsDiff;

                rms[ch] = r;
                ll[ch] = l;

                if (r >= _cfg.RmsThreshold && l >= _cfg.LlThreshold)
                    passed++;
            }

            var args = new DetectionEventArgs
            {
                WindowStartSample = winStart,
                WindowEndSample = winEnd,
                RmsPerChannel = rms,
                LlPerChannel = ll,
                PassedChannels = passed
            };

            OnWindowEvaluated?.Invoke(this, args);

            // ===== [ADD] Stage1 满足阈值 → 把 600ms 数据块抛给 Stage2（不阻塞）=====
            if (passed >= _cfg.MinChannelsToTrigger)
            {
                TryEmitStage2Window(winStart, winEnd);
            }

            // 保留你原来的“一次触发”语义（如你不需要可忽略此事件）
            if (!_triggered && passed >= _cfg.MinChannelsToTrigger)
            {
                _triggered = true;
                OnSeizureTriggered?.Invoke(this, args);
            }
        }

        private void TryEmitStage2Window(long winStart, long winEnd)
        {
            // 限频（可选）
            if (_cfg.Stage2EmitMinIntervalMs > 0)
            {
                long minIntervalSamples = (long)Math.Round(_cfg.Fs * _cfg.Stage2EmitMinIntervalMs / 1000.0);
                if (_lastStage2EmitSample != long.MinValue && (winEnd - _lastStage2EmitSample) < minIntervalSamples)
                    return;
            }

            int lookback = (int)Math.Round(_cfg.Fs * _cfg.Stage2LookbackMs / 1000.0);
            int nStage2 = (int)Math.Round(_cfg.Fs * _cfg.Stage2WindowMs / 1000.0);
            int minStage2 = _nWin + Math.Max(0, lookback);
            if (nStage2 < minStage2) nStage2 = minStage2;
            if (nStage2 > _nHist) return; // 历史缓冲不够长

            // Stage2 窗口：末尾对齐 winEnd
            long s2End = winEnd;
            long s2Start = s2End - nStage2 + 1;
            if (s2Start < 0) return;

            // delta：winEnd 距离“最新样本”的偏移（如果有积压会 >0）
            long delta = _lastSampleIndex - s2End;
            if (delta < 0) return;
            if (delta >= _nHist) return; // 需要的末尾已经被覆盖

            // 历史 ring 中 s2End 对应的位置
            int endPos = _histPos - 1 - (int)delta;
            int startPos = endPos - (nStage2 - 1);

            // 拷贝数据块（冻结一份给 Stage2）
            var window = new double[_cfg.ChannelCount][];
            for (int ch = 0; ch < _cfg.ChannelCount; ch++)
            {
                window[ch] = new double[nStage2];
                for (int i = 0; i < nStage2; i++)
                {
                    window[ch][i] = ReadHist(ch, startPos + i);
                }
            }

            _lastStage2EmitSample = s2End;

            OnStage2WindowReady?.Invoke(this, new Stage2WindowEventArgs
            {
                WindowStartSample = s2Start,
                WindowEndSample = s2End,
                Window = window,
                Samples = nStage2
            });
        }

        private double ReadRing(int ch, int pos)
        {
            int p = pos % _nWin;
            if (p < 0) p += _nWin;
            return _ring[ch, p];
        }

        private double ReadHist(int ch, int pos)
        {
            int p = pos % _nHist;
            if (p < 0) p += _nHist;
            return _histRing[ch, p];
        }
    }
}
