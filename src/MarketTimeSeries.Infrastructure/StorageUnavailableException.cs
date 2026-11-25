namespace MarketTimeSeries.Infrastructure;

/// <summary>The configured store cannot be reached at startup; the message says how to fix it.</summary>
public sealed class StorageUnavailableException(string message, Exception innerException)
    : Exception(message, innerException);
