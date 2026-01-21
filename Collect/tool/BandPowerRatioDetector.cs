using System;
using System.Collections.Concurrent;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace Collect.tool
{
    public class BandPowerRatioDetector
    {
        public sealed class Config
        {
            public int ChannelCount = 8;
            public double Fs = 500;

            // 分子频带
            public double NumBandLow = 8;
            public double NumBandHigh = 13;

            // 分母频带
            public double DenBandLow = 0.5;
            public double DenBandHigh = 4;

            public double RatioThreshold = 2.0;
            public int MinChannelsToTrigger = 1;

            public int QueueCapacity = 32;
            public bool StopAfterTrigger = false;
        }

        public sealed class ResultEventArgs : EventArgs
        {
            public long WindowStartSample;
            public long WindowEndSample;
            public double[] RatioPerChannel;
            public int PassedChannels;
        }

        public event EventHandler<ResultEventArgs> OnStage2Evaluated;
        public event EventHandler<ResultEventArgs> OnStage2Triggered;

        private readonly Config _cfg;
        private BlockingCollection<Job> _queue;
        private CancellationTokenSource _cts;
        private Task _worker;
        private volatile bool _triggered;

        private sealed class Job
        {
            public double[][] Window; // [ch][n]
            public long Start;
            public long End;
        }

        public BandPowerRatioDetector(Config cfg)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            if (_cfg.ChannelCount <= 0) throw new ArgumentException("ChannelCount must be > 0");
            if (_cfg.Fs <= 0) throw new ArgumentException("Fs must be > 0");
        }

        public void Start()
        {
            if (_worker != null) return;
            _queue = new BlockingCollection<Job>(new ConcurrentQueue<Job>(), _cfg.QueueCapacity);
            _cts = new CancellationTokenSource();
            _triggered = false;
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

        public void PushWindow(double[][] window, long startSample, long endSample)
        {
            if (window == null || window.Length != _cfg.ChannelCount) return;
            if (_queue == null || _queue.IsAddingCompleted) return;

            // 冻结数据块一般已在 Stage1 做了，这里直接入队
            _queue.TryAdd(new Job { Window = window, Start = startSample, End = endSample });
        }

        private void WorkerLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && !_queue.IsCompleted)
            {
                Job job = null;
                try
                {
                    if (!_queue.TryTake(out job, 100, ct)) continue;
                }
                catch (OperationCanceledException) { break; }

                if (job?.Window == null) continue;
                if (_triggered && _cfg.StopAfterTrigger) continue;

                var ratios = new double[_cfg.ChannelCount];
                int passed = 0;

                for (int ch = 0; ch < _cfg.ChannelCount; ch++)
                {
                    var x = job.Window[ch];
                    if (x == null || x.Length < 8)
                    {
                        ratios[ch] = 0;
                        continue;
                    }

                    double pNum = BandPowerFFT(x, _cfg.NumBandLow, _cfg.NumBandHigh, _cfg.Fs);
                    double pDen = BandPowerFFT(x, _cfg.DenBandLow, _cfg.DenBandHigh, _cfg.Fs);

                    double ratio = (pDen <= 1e-12) ? 0.0 : (pNum / pDen);
                    ratios[ch] = ratio;

                    if (ratio >= _cfg.RatioThreshold) passed++;
                }

                var args = new ResultEventArgs
                {
                    WindowStartSample = job.Start,
                    WindowEndSample = job.End,
                    RatioPerChannel = ratios,
                    PassedChannels = passed
                };

                OnStage2Evaluated?.Invoke(this, args);

                if (!_triggered && passed >= _cfg.MinChannelsToTrigger)
                {
                    _triggered = true;
                    OnStage2Triggered?.Invoke(this, args);
                }
            }
        }

        // ===== 带功率：Hann窗 + 零填充到2^k + FFT + 频带积分 =====
        private static double BandPowerFFT(double[] x, double fLow, double fHigh, double fs)
        {
            int n = x.Length;
            int nfft = NextPow2(n);
            if (nfft < 64) nfft = 64;

            // 去均值
            double mean = 0;
            for (int i = 0; i < n; i++) mean += x[i];
            mean /= n;

            var buf = new Complex[nfft];

            // Hann 窗
            for (int i = 0; i < n; i++)
            {
                double w = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / (n - 1));
                buf[i] = new Complex((x[i] - mean) * w, 0.0);
            }
            for (int i = n; i < nfft; i++) buf[i] = Complex.Zero;

            FFT(buf, inverse: false);

            // bin 对应
            int k1 = (int)Math.Floor(fLow * nfft / fs);
            int k2 = (int)Math.Ceiling(fHigh * nfft / fs);
            int kMax = nfft / 2;

            if (k1 < 0) k1 = 0;
            if (k2 > kMax) k2 = kMax;
            if (k2 < k1) return 0;

            double sum = 0.0;
            for (int k = k1; k <= k2; k++)
            {
                double re = buf[k].Real;
                double im = buf[k].Imaginary;
                sum += (re * re + im * im);
            }

            return sum;
        }

        private static int NextPow2(int n)
        {
            int p = 1;
            while (p < n) p <<= 1;
            return p;
        }

        // 标准 Cooley-Tukey radix-2 FFT
        private static void FFT(Complex[] a, bool inverse)
        {
            int n = a.Length;

            // bit reversal
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;

                if (i < j)
                {
                    var tmp = a[i];
                    a[i] = a[j];
                    a[j] = tmp;
                }
            }

            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = 2.0 * Math.PI / len * (inverse ? 1 : -1);
                Complex wlen = new Complex(Math.Cos(ang), Math.Sin(ang));

                for (int i = 0; i < n; i += len)
                {
                    Complex w = Complex.One;
                    int half = len >> 1;
                    for (int j = 0; j < half; j++)
                    {
                        Complex u = a[i + j];
                        Complex v = a[i + j + half] * w;
                        a[i + j] = u + v;
                        a[i + j + half] = u - v;
                        w *= wlen;
                    }
                }
            }

            if (inverse)
            {
                for (int i = 0; i < n; i++) a[i] /= n;
            }
        }
    }
}
