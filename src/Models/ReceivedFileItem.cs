namespace ClipShelf.Models;

/// <summary>Represents a file received via Phone Sync (web upload or Bluetooth).</summary>
public class ReceivedFileItem
{
    public string FileName { get; set; } = "";
    public string FullPath { get; set; } = "";
    public DateTime ReceivedAt { get; set; }
    public string Source { get; set; } = "Web"; // "Web" or "Bluetooth"
    public long SizeBytes { get; set; }
}
