using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;
using HttpTrafficMonitor.Models;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace HttpTrafficMonitor.ViewModels
{
    public class GraphsViewModel : ViewModelBase, IDisposable
    {
        private readonly DispatcherTimer _updateTimer;
        private readonly List<DateTime> _requestTimestamps = new();
        private readonly Dictionary<string, long> _domainTraffic = new();
        private readonly Dictionary<string, long> _processTraffic = new();
        private readonly List<double> _responseTimes = new();
        private readonly ObservableCollection<DateTimePoint> _rpsData = new();
        private readonly ObservableCollection<DateTimePoint> _bandwidthData = new();
        private long _totalBytesInWindow;
        private readonly object _lock = new();
        private const int MaxDataPoints = 120;

        // Requests per second chart
        public ISeries[] RpsSeries { get; }
        public Axis[] RpsXAxes { get; }
        public Axis[] RpsYAxes { get; }

        // Bandwidth chart
        public ISeries[] BandwidthSeries { get; }
        public Axis[] BandwidthXAxes { get; }
        public Axis[] BandwidthYAxes { get; }

        // Domain distribution pie
        public ObservableCollection<ISeries> DomainPieSeries { get; } = new();

        // Process distribution pie
        public ObservableCollection<ISeries> ProcessPieSeries { get; } = new();

        // Legend text of both pies; its color follows the theme, see ApplyTheme
        public SolidColorPaint PieLegendTextPaint { get; } = new(SKColors.LightGray);

        // Response time histogram
        public ISeries[] HistogramSeries { get; private set; }
        public Axis[] HistogramXAxes { get; }
        public Axis[] HistogramYAxes { get; }

        public GraphsViewModel()
        {
            RpsSeries = new ISeries[]
            {
                new LineSeries<DateTimePoint>
                {
                    Values = _rpsData,
                    GeometrySize = 0,
                    LineSmoothness = 0.3,
                    Stroke = new SolidColorPaint(SKColors.DodgerBlue, 2),
                    Fill = new SolidColorPaint(SKColors.DodgerBlue.WithAlpha(40)),
                    Name = "Requests/sec"
                }
            };
            RpsXAxes = new Axis[]
            {
                new Axis
                {
                    Labeler = v => new DateTime((long)v).ToString("HH:mm:ss"),
                    UnitWidth = TimeSpan.FromSeconds(1).Ticks,
                    MinStep = TimeSpan.FromSeconds(5).Ticks,
                    TextSize = 10,
                    LabelsPaint = new SolidColorPaint(SKColors.Gray)
                }
            };
            RpsYAxes = new Axis[]
            {
                new Axis
                {
                    Name = "req/s",
                    MinLimit = 0,
                    TextSize = 10,
                    NameTextSize = 11,
                    LabelsPaint = new SolidColorPaint(SKColors.Gray),
                    NamePaint = new SolidColorPaint(SKColors.Gray)
                }
            };

            BandwidthSeries = new ISeries[]
            {
                new LineSeries<DateTimePoint>
                {
                    Values = _bandwidthData,
                    GeometrySize = 0,
                    LineSmoothness = 0.3,
                    Stroke = new SolidColorPaint(SKColors.MediumPurple, 2),
                    Fill = new SolidColorPaint(SKColors.MediumPurple.WithAlpha(40)),
                    Name = "KB/sec"
                }
            };
            BandwidthXAxes = new Axis[]
            {
                new Axis
                {
                    Labeler = v => new DateTime((long)v).ToString("HH:mm:ss"),
                    UnitWidth = TimeSpan.FromSeconds(1).Ticks,
                    MinStep = TimeSpan.FromSeconds(5).Ticks,
                    TextSize = 10,
                    LabelsPaint = new SolidColorPaint(SKColors.Gray)
                }
            };
            BandwidthYAxes = new Axis[]
            {
                new Axis
                {
                    Name = "KB/s",
                    MinLimit = 0,
                    TextSize = 10,
                    NameTextSize = 11,
                    LabelsPaint = new SolidColorPaint(SKColors.Gray),
                    NamePaint = new SolidColorPaint(SKColors.Gray)
                }
            };

            HistogramSeries = Array.Empty<ISeries>();
            HistogramXAxes = new Axis[]
            {
                new Axis
                {
                    Name = "Response Time (ms)",
                    TextSize = 10,
                    NameTextSize = 11,
                    LabelsPaint = new SolidColorPaint(SKColors.Gray),
                    NamePaint = new SolidColorPaint(SKColors.Gray)
                }
            };
            HistogramYAxes = new Axis[]
            {
                new Axis
                {
                    Name = "Count",
                    MinLimit = 0,
                    TextSize = 10,
                    NameTextSize = 11,
                    LabelsPaint = new SolidColorPaint(SKColors.Gray),
                    NamePaint = new SolidColorPaint(SKColors.Gray)
                }
            };

            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _updateTimer.Tick += OnTimerTick;
            _updateTimer.Start();
        }

        public void RecordRequest(HttpRequestEntry entry)
        {
            lock (_lock)
            {
                _requestTimestamps.Add(entry.Timestamp);

                if (!_domainTraffic.ContainsKey(entry.Host))
                    _domainTraffic[entry.Host] = 0;
                _domainTraffic[entry.Host]++;

                if (!_processTraffic.ContainsKey(entry.ProcessName))
                    _processTraffic[entry.ProcessName] = 0;
                _processTraffic[entry.ProcessName]++;
            }
        }

        public void RecordResponse(HttpRequestEntry entry)
        {
            lock (_lock)
            {
                if (entry.Duration.HasValue)
                    _responseTimes.Add(entry.Duration.Value.TotalMilliseconds);
                if (entry.ResponseSize.HasValue)
                    _totalBytesInWindow += entry.ResponseSize.Value;
            }
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            DateTime now = DateTime.Now;
            DateTime windowStart = now.AddSeconds(-1);

            int rps;
            double kbps;
            lock (_lock)
            {
                _requestTimestamps.RemoveAll(t => t < now.AddMinutes(-5));
                rps = _requestTimestamps.Count(t => t >= windowStart);
                kbps = _totalBytesInWindow / 1024.0;
                _totalBytesInWindow = 0;
            }

            _rpsData.Add(new DateTimePoint(now, rps));
            while (_rpsData.Count > MaxDataPoints) _rpsData.RemoveAt(0);

            _bandwidthData.Add(new DateTimePoint(now, kbps));
            while (_bandwidthData.Count > MaxDataPoints) _bandwidthData.RemoveAt(0);

            UpdatePieCharts();
            UpdateHistogram();
        }

        private void UpdatePieCharts()
        {
            Dictionary<string, long> domains, processes;
            lock (_lock)
            {
                domains = _domainTraffic.OrderByDescending(x => x.Value).Take(8).ToDictionary(x => x.Key, x => x.Value);
                processes = _processTraffic.OrderByDescending(x => x.Value).Take(8).ToDictionary(x => x.Key, x => x.Value);
            }

            RebuildPie(DomainPieSeries, domains);
            RebuildPie(ProcessPieSeries, processes);
        }

        private static void RebuildPie(ObservableCollection<ISeries> target, Dictionary<string, long> data)
        {
            target.Clear();
            foreach (var (name, count) in data)
            {
                // Labels on the slices pile up once a few small slices sit side by side,
                // so the name and count go to the chart's legend instead.
                target.Add(new PieSeries<long>
                {
                    Values = new[] { count },
                    Name = $"{name}: {count}"
                });
            }
        }

        private void UpdateHistogram()
        {
            double[] times;
            lock (_lock)
            {
                if (_responseTimes.Count == 0) return;
                times = _responseTimes.ToArray();
            }

            var buckets = new[] { 50, 100, 200, 500, 1000, 2000, 5000, 10000 };
            var labels = new[] { "<50", "50-100", "100-200", "200-500", "500-1s", "1-2s", "2-5s", "5-10s", ">10s" };
            var counts = new double[buckets.Length + 1];

            foreach (double t in times)
            {
                int idx = Array.FindIndex(buckets, b => t < b);
                counts[idx >= 0 ? idx : buckets.Length]++;
            }

            HistogramSeries = new ISeries[]
            {
                new ColumnSeries<double>
                {
                    Values = counts,
                    Fill = new SolidColorPaint(SKColors.CornflowerBlue),
                    Name = "Requests"
                }
            };
            HistogramXAxes[0].Labels = labels;
            OnPropertyChanged(nameof(HistogramSeries));
        }

        public void ApplyTheme(bool dark)
        {
            PieLegendTextPaint.Color = dark ? SKColors.LightGray : SKColors.DimGray;
        }

        public void Reset()
        {
            lock (_lock)
            {
                _requestTimestamps.Clear();
                _domainTraffic.Clear();
                _processTraffic.Clear();
                _responseTimes.Clear();
                _totalBytesInWindow = 0;
            }
            _rpsData.Clear();
            _bandwidthData.Clear();
            DomainPieSeries.Clear();
            ProcessPieSeries.Clear();
            HistogramSeries = Array.Empty<ISeries>();
            OnPropertyChanged(nameof(HistogramSeries));
        }

        public void Dispose()
        {
            _updateTimer.Stop();
            GC.SuppressFinalize(this);
        }
    }
}
