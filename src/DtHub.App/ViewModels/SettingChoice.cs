namespace DtHub.App.ViewModels;

/// <summary>
/// A dropdown list choice: what is displayed, and what is stored.
///
/// The two almost always differ in the fine-tuning settings panel:
/// we show "12 Mb/s" and keep 12000, we show "H.265" and keep
/// "h265". Without this pair, a converter would be needed for every
/// list.
/// </summary>
public sealed record IntChoice(string Label, int Value);

/// <summary>The same, for a choice stored as text.</summary>
public sealed record TextChoice(string Label, string Value);

/// <summary>The same, for a choice stored as a decimal number.</summary>
public sealed record DoubleChoice(string Label, double Value);
