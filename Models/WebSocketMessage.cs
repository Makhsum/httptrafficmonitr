using System;

namespace HttpTrafficMonitor.Models
{
    public class WebSocketMessage
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Direction { get; set; } = "Sent";
        public string FrameType { get; set; } = "Text";
        public string Payload { get; set; } = string.Empty;
        public long PayloadLength { get; set; }
        public int ParentRequestId { get; set; }

        public string TimestampFormatted => Timestamp.ToString("HH:mm:ss.fff");
        public string DirectionArrow => Direction == "Sent" ? "\u2191" : "\u2193";

        public string PayloadSizeFormatted
        {
            get
            {
                if (PayloadLength < 1024) return $"{PayloadLength} B";
                if (PayloadLength < 1048576) return $"{PayloadLength / 1024.0:F1} KB";
                return $"{PayloadLength / 1048576.0:F1} MB";
            }
        }
    }
}
