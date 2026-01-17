using System;
using System.Text.Json.Serialization;

namespace DCM.Core.Models
{
    public class UploadHistoryEntry
    {
        public Guid Id { get; init; } = Guid.NewGuid();

        public PlatformType Platform { get; init; }

        public string VideoTitle { get; init; } = string.Empty;

        public Uri? VideoUrl { get; init; }

        public DateTimeOffset DateTime { get; init; }

        public bool Success { get; init; }

        public string? ErrorMessage { get; init; }

        [JsonIgnore]
        public string Status => Success ? "Erfolg" : "Fehler";
    }
}
