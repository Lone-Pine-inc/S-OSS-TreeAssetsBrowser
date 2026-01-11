using System;

namespace GeneralGame.Editor;

/// <summary>
/// Common interface for all browser panel types (tree view, icon grid, etc.)
/// </summary>
public interface IBrowserPanel
{
    /// <summary>
    /// Callback when user requests to close this panel
    /// </summary>
    Action OnCloseRequested { get; set; }

    /// <summary>
    /// Controls visibility of close button (hide if this is the only panel)
    /// </summary>
    bool ShowCloseButton { get; set; }
}
