namespace BinPm.Core;

/// <summary>
/// The exception type thrown for all expected, user-facing failures in BinPm
/// (package not found, checksum mismatch, unsupported archive, ...).
/// </summary>
public class BinPmException : Exception
{
    public BinPmException(string message)
        : base(message)
    {
    }

    public BinPmException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
