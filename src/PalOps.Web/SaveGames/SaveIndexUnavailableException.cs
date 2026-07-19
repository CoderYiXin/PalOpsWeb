namespace PalOps.Web.SaveGames;

/// <summary>
/// Raised when a query requires a completed local save index but none is available yet.
/// It maps to HTTP 503 so the UI can distinguish an unavailable snapshot from an
/// application defect.
/// </summary>
public sealed class SaveIndexUnavailableException(string message) : Exception(message);
