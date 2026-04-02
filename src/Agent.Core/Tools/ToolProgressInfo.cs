namespace Agent.Core.Tools;

public sealed record ToolProgressInfo(string CallId, string Message, double? PercentComplete = null);
