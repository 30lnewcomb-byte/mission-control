namespace MissionControl.Desktop;

/// <summary>
/// A visual-only chat line. The separate conversation history sent to Ollama
/// contains only actual user and assistant messages, not UI status messages.
/// </summary>
public sealed record ChatLine(string RoleLabel, string Content);
