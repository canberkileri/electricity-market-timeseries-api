namespace MarketTimeSeries.Domain;

/// <summary>What a time series measures.</summary>
public enum TimeSeriesType
{
    Production,
    Consumption,

    /// <summary>Flow from <c>BiddingZone</c> to <c>ToBiddingZone</c>; a negative value means the reverse direction.</summary>
    CrossBorderFlow,
}
