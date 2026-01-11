using Editor;
using Sandbox;
using System;
using System.IO;
using System.Linq;

namespace GeneralGame.Editor;

/// <summary>
/// Individual asset browser panel with its own toolbar and tree view.
/// Multiple panels can be displayed side by side in TreeAssetBrowser.
/// </summary>
public class AssetBrowserPanel : Widget, IBrowserPanel
{
    private TreeView _treeView;
    private LineEdit _searchEdit;
    private string _searchFilter = "";
    private Widget _toolbar;
    private IconButton _closeBtn;

    private string _assetsPath;
    private string _codePath;

    /// <summary>
    /// Callback when user requests to close this panel
    /// </summary>
    public Action OnCloseRequested { get; set; }

    /// <summary>
    /// Selected asset callback
    /// </summary>
    public Action<Asset> OnAssetSelected;
    public Action<string> OnFileSelected;
    public Action<string> OnFolderSelected;

    /// <summary>
    /// Called when a folder is clicked (for syncing with IconGridPanel)
    /// </summary>
    public Action<string> OnFolderClicked;

    /// <summary>
    /// Controls visibility of close button (hide if this is the only panel)
    /// </summary>
    public bool ShowCloseButton
    {
        get => _closeBtn?.Visible ?? false;
        set
        {
            if (_closeBtn != null)
                _closeBtn.Visible = value;
        }
    }

    public AssetBrowserPanel(Widget parent) : base(parent)
    {
        SetSizeMode(SizeMode.CanGrow, SizeMode.CanGrow);

        _assetsPath = Project.Current?.GetAssetsPath();
        _codePath = Project.Current?.GetCodePath();

        CreateUI();
        RefreshTree();
    }

    private void CreateUI()
    {
        Layout = Layout.Column();
        Layout.Spacing = 4;
        Layout.Margin = 4;

        // Toolbar
        _toolbar = Layout.Add(new Widget(this));
        _toolbar.Layout = Layout.Row();
        _toolbar.Layout.Spacing = 4;
        _toolbar.FixedHeight = 28;

        // Search icon
        var searchIcon = _toolbar.Layout.Add(new Label("search", this));
        searchIcon.SetStyles("font-family: Material Icons; font-size: 16px; color: #888;");
        searchIcon.FixedWidth = 20;

        // Search input
        _searchEdit = _toolbar.Layout.Add(new LineEdit(this));
        _searchEdit.PlaceholderText = "Search...";
        _searchEdit.TextEdited += OnSearchChanged;
        _searchEdit.ReturnPressed += RefreshTree;

        // Refresh button
        var refreshBtn = _toolbar.Layout.Add(new IconButton("refresh"));
        refreshBtn.ToolTip = "Refresh";
        refreshBtn.Background = Color.Transparent;
        refreshBtn.OnClick = RefreshTree;

        // Expand all button
        var expandBtn = _toolbar.Layout.Add(new IconButton("unfold_more"));
        expandBtn.ToolTip = "Expand All";
        expandBtn.Background = Color.Transparent;
        expandBtn.OnClick = ExpandAll;

        // Close button
        _closeBtn = _toolbar.Layout.Add(new IconButton("close"));
        _closeBtn.ToolTip = "Close Panel";
        _closeBtn.Background = Color.Transparent;
        _closeBtn.OnClick = () => OnCloseRequested?.Invoke();
        _closeBtn.Visible = false; // Hidden by default, shown when multiple panels exist

        // Tree view
        _treeView = Layout.Add(new TreeView(this));
        _treeView.SetSizeMode(SizeMode.CanGrow, SizeMode.CanGrow);
        _treeView.MultiSelect = false;
        _treeView.ItemSpacing = 1;
        _treeView.IndentWidth = 16;

        _treeView.ItemActivated += OnItemActivated;
        _treeView.OnSelectionChanged += OnSelectionChanged;
    }

    private void OnSearchChanged(string text)
    {
        _searchFilter = text?.ToLowerInvariant() ?? "";

        if (string.IsNullOrEmpty(_searchFilter))
        {
            _treeView.ShouldDisplayChild = null;
        }
        else
        {
            _treeView.ShouldDisplayChild = (obj) =>
            {
                if (obj is AssetFolderNode folder)
                {
                    return folder.MatchesFilter(_searchFilter);
                }
                if (obj is AssetFileNode file)
                {
                    return file.MatchesFilter(_searchFilter);
                }
                return true;
            };
        }

        _treeView.Update();
    }

    public void RefreshTree()
    {
        _treeView.Clear();

        // Add Assets folder
        if (!string.IsNullOrEmpty(_assetsPath) && Directory.Exists(_assetsPath))
        {
            var assetsNode = new AssetFolderNode(_assetsPath, "Assets", "folder_special");
            _treeView.AddItem(assetsNode);
            _treeView.Open(assetsNode);
        }

        // Add Code folder
        if (!string.IsNullOrEmpty(_codePath) && Directory.Exists(_codePath))
        {
            var codeNode = new AssetFolderNode(_codePath, "Code", "code");
            _treeView.AddItem(codeNode);
        }

        _treeView.Update();
    }

    private void ExpandAll()
    {
        foreach (var item in _treeView.Items)
        {
            if (item is AssetFolderNode folder)
            {
                ExpandFolderRecursive(folder);
            }
        }
        _treeView.Update();
    }

    private void ExpandFolderRecursive(AssetFolderNode folder)
    {
        folder.EnsureChildrenBuilt();
        _treeView.Open(folder);

        foreach (var child in folder.Children)
        {
            if (child is AssetFolderNode subFolder)
            {
                ExpandFolderRecursive(subFolder);
            }
        }
    }

    private void OnItemActivated(object item)
    {
        if (item is AssetFileNode fileNode)
        {
            var asset = AssetSystem.FindByPath(fileNode.FullPath);
            if (asset != null)
            {
                OnAssetSelected?.Invoke(asset);
                asset.OpenInEditor();
            }
            else
            {
                OnFileSelected?.Invoke(fileNode.FullPath);
                EditorUtility.OpenFolder(fileNode.FullPath);
            }
        }
        else if (item is AssetFolderNode folderNode)
        {
            _treeView.Toggle(folderNode);
        }
    }

    private void OnSelectionChanged(object[] items)
    {
        var item = items.FirstOrDefault();

        if (item is AssetFileNode fileNode)
        {
            var asset = AssetSystem.FindByPath(fileNode.FullPath);
            if (asset != null)
            {
                EditorUtility.InspectorObject = asset;
            }
        }
        else if (item is AssetFolderNode folderNode)
        {
            OnFolderSelected?.Invoke(folderNode.FullPath);
            OnFolderClicked?.Invoke(folderNode.FullPath);
        }
    }

    /// <summary>
    /// Navigate to and select a specific file in the tree
    /// </summary>
    public void NavigateToFile(string path)
    {
        var normalizedPath = Path.GetFullPath(path);

        foreach (var rootItem in _treeView.Items)
        {
            if (rootItem is AssetFolderNode rootFolder)
            {
                var node = rootFolder.FindNode(normalizedPath);
                if (node != null)
                {
                    _treeView.ExpandPathTo(node);
                    _treeView.SelectItem(node);
                    _treeView.ScrollTo(node);
                    break;
                }
            }
        }
    }
}
