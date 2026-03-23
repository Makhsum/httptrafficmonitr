using System.Collections.ObjectModel;
using System.Windows.Media;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using HttpTrafficMonitor.Models;

namespace HttpTrafficMonitor.ViewModels
{
    public class ComparisonViewModel : ViewModelBase
    {
        private string _leftLabel = "Request A";
        private string _rightLabel = "Request B";
        private string _selectedTab = "Headers";

        public string LeftLabel { get => _leftLabel; set => SetProperty(ref _leftLabel, value); }
        public string RightLabel { get => _rightLabel; set => SetProperty(ref _rightLabel, value); }
        public string SelectedTab { get => _selectedTab; set { if (SetProperty(ref _selectedTab, value)) RefreshDiff(); } }

        public HttpRequestEntry? Left { get; set; }
        public HttpRequestEntry? Right { get; set; }

        // Summary info
        public string LeftSummary => Left != null ? $"{Left.Method} {Left.StatusCode} - {Left.Host} ({Left.DurationFormatted})" : "";
        public string RightSummary => Right != null ? $"{Right.Method} {Right.StatusCode} - {Right.Host} ({Right.DurationFormatted})" : "";
        public string TimingComparison => Left?.Duration != null && Right?.Duration != null
            ? $"A: {Left.DurationFormatted}  |  B: {Right.DurationFormatted}  |  \u0394: {System.Math.Abs((Left.Duration.Value - Right.Duration.Value).TotalMilliseconds):F0} ms"
            : "";

        public ObservableCollection<DiffLineViewModel> DiffLines { get; } = new();

        public void LoadRequests(HttpRequestEntry a, HttpRequestEntry b)
        {
            Left = a;
            Right = b;
            LeftLabel = $"#{a.Id} {a.Method} {a.Host}";
            RightLabel = $"#{b.Id} {b.Method} {b.Host}";
            OnPropertyChanged(nameof(LeftSummary));
            OnPropertyChanged(nameof(RightSummary));
            OnPropertyChanged(nameof(TimingComparison));
            RefreshDiff();
        }

        private void RefreshDiff()
        {
            if (Left == null || Right == null) return;

            string leftText, rightText;
            switch (_selectedTab)
            {
                case "RequestHeaders":
                    leftText = Left.RequestHeaders;
                    rightText = Right.RequestHeaders;
                    break;
                case "RequestBody":
                    leftText = Left.RequestBody;
                    rightText = Right.RequestBody;
                    break;
                case "ResponseHeaders":
                    leftText = Left.ResponseHeaders ?? "";
                    rightText = Right.ResponseHeaders ?? "";
                    break;
                case "ResponseBody":
                    leftText = Left.ResponseBody ?? "";
                    rightText = Right.ResponseBody ?? "";
                    break;
                default: // "Headers" = request headers
                    leftText = Left.RequestHeaders;
                    rightText = Right.RequestHeaders;
                    break;
            }

            BuildDiff(leftText, rightText);
        }

        private void BuildDiff(string oldText, string newText)
        {
            DiffLines.Clear();

            var differ = new Differ();
            var builder = new InlineDiffBuilder(differ);
            var diff = builder.BuildDiffModel(oldText, newText);

            foreach (var line in diff.Lines)
            {
                DiffLines.Add(new DiffLineViewModel
                {
                    Text = line.Text ?? "",
                    Position = line.Position?.ToString() ?? "",
                    ChangeType = line.Type,
                    Background = line.Type switch
                    {
                        ChangeType.Inserted => new SolidColorBrush(Color.FromArgb(40, 40, 167, 69)),
                        ChangeType.Deleted => new SolidColorBrush(Color.FromArgb(40, 220, 53, 69)),
                        ChangeType.Modified => new SolidColorBrush(Color.FromArgb(40, 255, 193, 7)),
                        ChangeType.Imaginary => new SolidColorBrush(Color.FromArgb(20, 128, 128, 128)),
                        _ => Brushes.Transparent
                    },
                    Prefix = line.Type switch
                    {
                        ChangeType.Inserted => "+",
                        ChangeType.Deleted => "-",
                        ChangeType.Modified => "~",
                        _ => " "
                    }
                });
            }
        }
    }

    public class DiffLineViewModel
    {
        public string Text { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public ChangeType ChangeType { get; set; }
        public Brush Background { get; set; } = Brushes.Transparent;
        public string Prefix { get; set; } = " ";
    }
}
