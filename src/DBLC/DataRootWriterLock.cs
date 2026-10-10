using System.IO;

namespace DBLC;

/// <summary>The open handle owns the data root; Windows releases it after process termination.</summary>
public sealed class DataRootWriterLock : IDisposable
{
    private readonly FileStream _handle;

    public DataRootWriterLock(string dataRoot)
    {
        var root = Path.GetFullPath(dataRoot);
        Directory.CreateDirectory(root);
        _handle = new FileStream(Path.Combine(root, ".writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    public void Dispose() => _handle.Dispose();
}
