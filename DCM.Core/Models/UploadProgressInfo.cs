namespace DCM.Core.Models;

public sealed class UploadProgressInfo
{
    public UploadProgressInfo(double percent, string? message = null, bool isIndeterminate = false)
    {
        Percent = percent;
        Message = message;
        IsIndeterminate = isIndeterminate;
    }

    public double Percent { get; }
    public string? Message { get; }
    public bool IsIndeterminate { get; }
}