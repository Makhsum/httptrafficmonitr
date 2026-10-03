using System;
using System.Collections.Generic;

namespace HttpTrafficMonitor.Models
{
    public class TlsCertificateInfo
    {
        public string Subject { get; set; } = string.Empty;
        public string Issuer { get; set; } = string.Empty;
        public DateTime NotBefore { get; set; }
        public DateTime NotAfter { get; set; }
        public string SerialNumber { get; set; } = string.Empty;
        public string Thumbprint { get; set; } = string.Empty;
        public string SignatureAlgorithm { get; set; } = string.Empty;
        // Negotiated on the server connection; null until a response arrived over it, or when it cannot be read.
        public string? TlsVersion { get; set; }
        public string? CipherSuite { get; set; }
        public int KeySize { get; set; }
        public List<CertificateChainEntry> Chain { get; set; } = new();
        public bool HasErrors { get; set; }
        public string ErrorSummary { get; set; } = string.Empty;

        public bool IsExpired => DateTime.UtcNow > NotAfter;
        public bool IsNotYetValid => DateTime.UtcNow < NotBefore;
        public int DaysUntilExpiry => Math.Max(0, (int)(NotAfter - DateTime.UtcNow).TotalDays);
    }

    public class CertificateChainEntry
    {
        public string Subject { get; set; } = string.Empty;
        public string Issuer { get; set; } = string.Empty;
        public string Thumbprint { get; set; } = string.Empty;
        public DateTime NotAfter { get; set; }
    }
}
