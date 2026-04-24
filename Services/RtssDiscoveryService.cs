using System.IO;
using FellowOakDicom;

namespace MeshGenLite.Services;

/// <summary>
/// Discovers DICOM RT Structure Set files by scanning a directory and its subdirectories.
/// Separated per SRP — keeps file-system scanning logic out of the ViewModel layer.
/// </summary>
public interface IRtssDiscoveryService
{
    /// <summary>
    /// Scans <paramref name="directoryPath"/> and its subdirectories for .dcm files
    /// whose Modality is RTSTRUCT. Returns the first match or <c>null</c>.
    /// </summary>
    string? FindRtssFile(string directoryPath);
}

public sealed class RtssDiscoveryService : IRtssDiscoveryService
{
    public string? FindRtssFile(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return null;
        }

        foreach (var path in Directory.EnumerateFiles(directoryPath, "*.dcm", SearchOption.AllDirectories))
        {
            try
            {
                var dataset = DicomFile.Open(path).Dataset;
                var modality = dataset.GetSingleValueOrDefault(DicomTag.Modality, string.Empty);
                if (string.Equals(modality, "RTSTRUCT", StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }
            }
            catch
            {
                // Skip files that cannot be parsed as DICOM.
            }
        }

        return null;
    }
}
