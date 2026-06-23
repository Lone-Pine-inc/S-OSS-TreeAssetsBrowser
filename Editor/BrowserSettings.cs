using Editor;
using Label = Editor.Label;
using Sandbox;
using System;

namespace GeneralGame.Editor;

// Global, persisted settings for the asset browser, edited from the toolbar settings button.
public static class BrowserSettings
{
    private const string ShowTreeIconsKey = "TreeAssetBrowser.Settings.ShowTreeIcons";
    private const string BackwardCompatibilityKey = "TreeAssetBrowser.Settings.BackwardCompatibility";

    // Fired whenever a setting changes so open panels can repaint.
    public static event Action OnChanged;

    private static bool _loaded;
    private static bool _showTreeIcons;
    private static bool _backwardCompatibility;

    public static bool ShowTreeIcons
    {
        get { EnsureLoaded(); return _showTreeIcons; }
        set
        {
            EnsureLoaded();
            if (_showTreeIcons == value) return;
            _showTreeIcons = value;
            ProjectCookie.Set(ShowTreeIconsKey, value);
            OnChanged?.Invoke();
        }
    }

    public static bool BackwardCompatibility
    {
        get { EnsureLoaded(); return _backwardCompatibility; }
        set
        {
            EnsureLoaded();
            if (_backwardCompatibility == value) return;
            _backwardCompatibility = value;
            ProjectCookie.Set(BackwardCompatibilityKey, value);
            OnChanged?.Invoke();
        }
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        _showTreeIcons = ProjectCookie.Get(ShowTreeIconsKey, false);
        _backwardCompatibility = ProjectCookie.Get(BackwardCompatibilityKey, false);
    }
}

// Dropdown opened from the browser toolbar settings button.
internal class BrowserSettingsPopup : PopupWidget
{
    public BrowserSettingsPopup() : base(null)
    {
        Layout = Layout.Column();
        Layout.Margin = 12;
        Layout.Spacing = 10;
        MinimumWidth = 260;

        var title = Layout.Add(new Label("Browser Settings", this));
        title.SetStyles("font-weight: 600; font-size: 13px;");

        // Show asset thumbnails next to the type icons in the tree view
        var iconsCheck = Layout.Add(new Checkbox("Show icons in Tree View", this));
        iconsCheck.Value = BrowserSettings.ShowTreeIcons;
        iconsCheck.StateChanged = (_) => BrowserSettings.ShowTreeIcons = iconsCheck.Value;

        // When moving a single asset, offer to scan the project and fix references to its old path
        var backwardCompatCheck = Layout.Add(new Checkbox("Backward Compatibility", this));
        backwardCompatCheck.ToolTip = "When moving a single asset, ask whether to update references to it in scenes, resources and materials.";
        backwardCompatCheck.Value = BrowserSettings.BackwardCompatibility;
        backwardCompatCheck.StateChanged = (_) => BrowserSettings.BackwardCompatibility = backwardCompatCheck.Value;
    }
}
