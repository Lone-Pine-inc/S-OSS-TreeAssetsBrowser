using Editor;
using Sandbox;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GeneralGame.Editor;

/// <summary>
/// Custom Asset Browser displayed as a tree view of files and folders.
/// Supports multiple side-by-side panels for comparing/browsing different locations.
/// Supports different panel types: Tree View and Icon Grid.
/// </summary>
[Dock("Editor", "Tree Asset Browser", "account_tree")]
public class TreeAssetBrowser : Widget
{
    private List<Widget> _panels = new();
    private List<Splitter> _splitters = new();
    private Widget _panelsContainer;
    private Widget _mainToolbar;

    // Selected asset callback (forwarded from panels)
    public Action<Asset> OnAssetSelected;
    public Action<string> OnFileSelected;
    public Action<string> OnFolderSelected;

    public TreeAssetBrowser(Widget parent) : base(parent)
    {
        WindowTitle = "Tree Asset Browser";
        MinimumSize = new Vector2(250, 200);
        SetSizeMode(SizeMode.CanGrow, SizeMode.CanGrow);

        CreateUI();
        AddTreePanel(); // Create initial tree panel
    }

    private void CreateUI()
    {
        Layout = Layout.Column();
        Layout.Spacing = 2;

        // Main toolbar with add panel button
        _mainToolbar = Layout.Add(new Widget(this));
        _mainToolbar.Layout = Layout.Row();
        _mainToolbar.Layout.Spacing = 4;
        _mainToolbar.Layout.Margin = 4;
        _mainToolbar.FixedHeight = 24;

        // Title label
        var titleLabel = _mainToolbar.Layout.Add(new Label("Panels:", this));
        titleLabel.SetStyles("color: #888; font-size: 11px;");

        _mainToolbar.Layout.AddStretchCell();

        // Add panel button (with dropdown menu)
        var addBtn = _mainToolbar.Layout.Add(new IconButton("add"));
        addBtn.ToolTip = "Add Browser Panel";
        addBtn.Background = Color.Transparent;
        addBtn.OnClick = ShowAddPanelMenu;

        // Panels container (horizontal layout with splitters)
        _panelsContainer = Layout.Add(new Widget(this));
        _panelsContainer.Layout = Layout.Row();
        _panelsContainer.SetSizeMode(SizeMode.CanGrow, SizeMode.CanGrow);
    }

    /// <summary>
    /// Show context menu to choose panel type
    /// </summary>
    private void ShowAddPanelMenu()
    {
        var menu = new ContextMenu(this);

        menu.AddOption("Tree View", "account_tree", AddTreePanel);
        menu.AddOption("Icon Grid", "grid_view", AddIconGridPanel);
        menu.AddSeparator();
        menu.AddOption("Cloud Assets", "cloud", AddCloudAssetPanel);

        menu.OpenAtCursor();
    }

    /// <summary>
    /// Add a new tree view panel
    /// </summary>
    public void AddTreePanel()
    {
        AddSplitterIfNeeded();

        var panel = new AssetBrowserPanel(_panelsContainer);
        panel.OnCloseRequested = () => RemovePanel(panel);

        // Forward callbacks
        panel.OnAssetSelected = (asset) => OnAssetSelected?.Invoke(asset);
        panel.OnFileSelected = (path) => OnFileSelected?.Invoke(path);
        panel.OnFolderSelected = (path) => OnFolderSelected?.Invoke(path);

        // Sync folder selection with all IconGridPanels
        panel.OnFolderClicked = OnFolderSelectedInTree;

        _panelsContainer.Layout.Add(panel);
        _panels.Add(panel);

        UpdateCloseButtons();
    }

    /// <summary>
    /// Add a new icon grid panel
    /// </summary>
    public void AddIconGridPanel()
    {
        AddSplitterIfNeeded();

        var panel = new IconGridPanel(_panelsContainer);
        panel.OnCloseRequested = () => RemovePanel(panel);

        _panelsContainer.Layout.Add(panel);
        _panels.Add(panel);

        UpdateCloseButtons();
    }

    /// <summary>
    /// Add a new cloud asset search panel
    /// </summary>
    public void AddCloudAssetPanel()
    {
        AddSplitterIfNeeded();

        var panel = new CloudAssetPanel(_panelsContainer);
        panel.OnCloseRequested = () => RemovePanel(panel);

        // Sync cloud asset selection with all CloudIconGridPanels
        panel.OnCloudAssetsLoaded = OnCloudAssetsLoaded;

        _panelsContainer.Layout.Add(panel);
        _panels.Add(panel);

        UpdateCloseButtons();
    }

    /// <summary>
    /// Add a new cloud icon grid panel for previewing cloud assets
    /// </summary>
    public void AddCloudIconGridPanel()
    {
        AddSplitterIfNeeded();

        var panel = new CloudIconGridPanel(_panelsContainer);
        panel.OnCloseRequested = () => RemovePanel(panel);

        _panelsContainer.Layout.Add(panel);
        _panels.Add(panel);

        UpdateCloseButtons();
    }

    /// <summary>
    /// Add splitter before new panel if not the first one
    /// </summary>
    private void AddSplitterIfNeeded()
    {
        if (_panels.Count > 0)
        {
            var splitter = new Splitter(_panelsContainer);
            splitter.IsVertical = true;
            _panelsContainer.Layout.Add(splitter);
            _splitters.Add(splitter);
        }
    }

    /// <summary>
    /// Called when folder is selected in any tree panel - syncs all IconGridPanels
    /// </summary>
    private void OnFolderSelectedInTree(string folderPath)
    {
        foreach (var panel in _panels.OfType<IconGridPanel>())
        {
            panel.ShowFolder(folderPath);
        }
    }

    /// <summary>
    /// Called when cloud assets are loaded in any CloudAssetPanel - syncs all CloudIconGridPanels
    /// </summary>
    private void OnCloudAssetsLoaded(List<Package> packages)
    {
        foreach (var panel in _panels.OfType<CloudIconGridPanel>())
        {
            panel.ShowPackages(packages);
        }
    }

    /// <summary>
    /// Remove a panel (minimum 1 panel must remain)
    /// </summary>
    public void RemovePanel(Widget panel)
    {
        if (_panels.Count <= 1)
            return; // Keep at least one panel

        var index = _panels.IndexOf(panel);
        if (index < 0)
            return;

        // Remove the panel from list first
        _panels.RemoveAt(index);

        // Destroy the panel widget
        panel.Destroy();

        // Remove associated splitter
        if (_splitters.Count > 0)
        {
            // If removing first panel, remove first splitter
            // If removing other panel, remove splitter before it (index - 1)
            int splitterIndex = index > 0 ? index - 1 : 0;
            if (splitterIndex < _splitters.Count)
            {
                _splitters[splitterIndex].Destroy();
                _splitters.RemoveAt(splitterIndex);
            }
        }

        UpdateCloseButtons();
    }

    /// <summary>
    /// Update close button visibility on all panels
    /// </summary>
    private void UpdateCloseButtons()
    {
        bool showClose = _panels.Count > 1;
        foreach (var panel in _panels)
        {
            if (panel is IBrowserPanel browserPanel)
            {
                browserPanel.ShowCloseButton = showClose;
            }
        }
    }

    /// <summary>
    /// Navigate to and select a specific file in the first tree panel
    /// </summary>
    public void NavigateToFile(string path)
    {
        _panels.OfType<AssetBrowserPanel>().FirstOrDefault()?.NavigateToFile(path);
    }

    /// <summary>
    /// Refresh all tree panels
    /// </summary>
    public void RefreshAll()
    {
        foreach (var panel in _panels.OfType<AssetBrowserPanel>())
        {
            panel.RefreshTree();
        }
    }

    [EditorEvent.Frame]
    public void OnFrame()
    {
        // Could add periodic refresh here if needed
    }
}
