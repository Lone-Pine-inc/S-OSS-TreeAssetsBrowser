using Editor;
using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GeneralGame.Editor;

/// <summary>
/// Panel for browsing cloud assets from the S&box community as a tree view.
/// </summary>
public class CloudAssetPanel : Widget, IBrowserPanel
{
    private Widget _toolbar;
    private LineEdit _searchEdit;
    private IconButton _closeBtn;
    private IconButton _searchBtn;
    private IconButton _refreshBtn;
    private TreeView _treeView;
    private Label _statusLabel;

    private string _lastQuery = "";

    public Action OnCloseRequested { get; set; }

    /// <summary>
    /// Called when a cloud category/folder is clicked (for syncing with preview panels)
    /// </summary>
    public Action<List<Package>> OnCloudAssetsLoaded;

    public bool ShowCloseButton
    {
        get => _closeBtn?.Visible ?? false;
        set
        {
            if (_closeBtn != null)
                _closeBtn.Visible = value;
        }
    }

    public CloudAssetPanel(Widget parent) : base(parent)
    {
        SetSizeMode(SizeMode.CanGrow, SizeMode.CanGrow);
        MinimumWidth = 250;
        CreateUI();
        BuildCategoryTree();
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

        // Cloud icon
        var cloudIcon = _toolbar.Layout.Add(new Label("cloud", this));
        cloudIcon.SetStyles("font-family: Material Icons; font-size: 16px; color: #888;");
        cloudIcon.FixedWidth = 20;

        // Search input
        _searchEdit = _toolbar.Layout.Add(new LineEdit(this));
        _searchEdit.PlaceholderText = "Search cloud...";
        _searchEdit.ReturnPressed += DoSearch;

        // Search button
        _searchBtn = _toolbar.Layout.Add(new IconButton("search"));
        _searchBtn.ToolTip = "Search";
        _searchBtn.Background = Color.Transparent;
        _searchBtn.OnClick = DoSearch;

        // Refresh button
        _refreshBtn = _toolbar.Layout.Add(new IconButton("refresh"));
        _refreshBtn.ToolTip = "Refresh";
        _refreshBtn.Background = Color.Transparent;
        _refreshBtn.OnClick = RefreshCurrentCategory;

        _toolbar.Layout.AddStretchCell();

        // Close button
        _closeBtn = _toolbar.Layout.Add(new IconButton("close"));
        _closeBtn.ToolTip = "Close Panel";
        _closeBtn.Background = Color.Transparent;
        _closeBtn.OnClick = () => OnCloseRequested?.Invoke();
        _closeBtn.Visible = false;

        // Status label
        _statusLabel = Layout.Add(new Label("Select a category to browse cloud assets", this));
        _statusLabel.SetStyles("color: #666; font-size: 10px; padding: 4px;");

        // Tree view
        _treeView = Layout.Add(new TreeView(this));
        _treeView.SetSizeMode(SizeMode.CanGrow, SizeMode.CanGrow);
        _treeView.MultiSelect = false;
        _treeView.ItemSpacing = 1;
        _treeView.IndentWidth = 16;

        _treeView.ItemActivated += OnItemActivated;
        _treeView.OnSelectionChanged += OnSelectionChanged;
    }

    private void BuildCategoryTree()
    {
        _treeView.Clear();

        // Add category folders
        _treeView.AddItem(new CloudCategoryNode("model", "Models", "view_in_ar", this));
        _treeView.AddItem(new CloudCategoryNode("material", "Materials", "texture", this));
        _treeView.AddItem(new CloudCategoryNode("sound", "Sounds", "audiotrack", this));
        _treeView.AddItem(new CloudCategoryNode("map", "Maps", "landscape", this));

        _treeView.Update();
    }

    private async void DoSearch()
    {
        var query = _searchEdit.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(query))
        {
            _statusLabel.Text = "Enter search query";
            return;
        }

        _statusLabel.Text = "Searching...";
        _searchBtn.Enabled = false;

        try
        {
            var result = await Package.FindAsync(query, take: 100);

            if (result?.Packages != null && result.Packages.Any())
            {
                // Clear old search results
                var existingSearch = _treeView.Items.OfType<CloudSearchResultsNode>().FirstOrDefault();
                if (existingSearch != null)
                {
                    _treeView.RemoveItem(existingSearch);
                }

                // Add search results node
                var searchNode = new CloudSearchResultsNode(query, result.Packages.ToList());
                _treeView.AddItem(searchNode);
                _treeView.Open(searchNode);
                _treeView.SelectItem(searchNode);

                _statusLabel.Text = $"Found {result.Packages.Count()} results for \"{query}\"";

                // Notify preview panels
                OnCloudAssetsLoaded?.Invoke(result.Packages.ToList());
            }
            else
            {
                _statusLabel.Text = $"No results for \"{query}\"";
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Search error: {ex.Message}";
            Log.Warning($"Cloud search error: {ex.Message}");
        }
        finally
        {
            _searchBtn.Enabled = true;
        }
    }

    private async void RefreshCurrentCategory()
    {
        var selected = _treeView.Selection.FirstOrDefault();
        if (selected is CloudCategoryNode categoryNode)
        {
            categoryNode.IsLoaded = false;
            await LoadCategoryAssets(categoryNode);
        }
    }

    private void OnItemActivated(object item)
    {
        if (item is CloudCategoryNode categoryNode)
        {
            _treeView.Toggle(categoryNode);
        }
        else if (item is CloudPackageNode packageNode)
        {
            // Open package page in browser
            var url = $"https://sbox.game/{packageNode.FullIdent}";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
    }

    private async void OnSelectionChanged(object[] items)
    {
        var item = items.FirstOrDefault();

        if (item is CloudCategoryNode categoryNode)
        {
            await LoadCategoryAssets(categoryNode);
        }
        else if (item is CloudSearchResultsNode searchNode)
        {
            _statusLabel.Text = $"{searchNode.Packages.Count} results for \"{searchNode.Query}\"";
            OnCloudAssetsLoaded?.Invoke(searchNode.Packages);
        }
        else if (item is CloudPackageNode packageNode)
        {
            _statusLabel.Text = $"{packageNode.Title} by {packageNode.Author}";
        }
    }

    internal async Task LoadCategoryAssets(CloudCategoryNode categoryNode)
    {
        if (categoryNode.IsLoading || categoryNode.IsLoaded)
        {
            // Already loaded - just notify preview
            if (categoryNode.IsLoaded)
                OnCloudAssetsLoaded?.Invoke(categoryNode.Packages);
            return;
        }

        categoryNode.IsLoading = true;
        _statusLabel.Text = $"Loading {categoryNode.DisplayName}...";

        try
        {
            var query = $"+type:{categoryNode.TypeFilter}";
            var result = await Package.FindAsync(query, take: 100);

            if (result?.Packages != null)
            {
                categoryNode.SetPackages(result.Packages.ToList());
                _treeView.Open(categoryNode);
                _treeView.Update();

                _statusLabel.Text = $"Loaded {result.Packages.Count()} {categoryNode.DisplayName.ToLower()}";

                // Notify preview panels
                OnCloudAssetsLoaded?.Invoke(result.Packages.ToList());
            }
            else
            {
                _statusLabel.Text = $"No {categoryNode.DisplayName.ToLower()} found";
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Error loading {categoryNode.DisplayName}: {ex.Message}";
            Log.Warning($"Cloud category load error: {ex.Message}");
        }
        finally
        {
            categoryNode.IsLoading = false;
        }
    }
}

/// <summary>
/// Tree node representing a cloud asset category (Models, Materials, etc.)
/// </summary>
internal class CloudCategoryNode : TreeNode
{
    public string TypeFilter { get; }
    public string DisplayName { get; }
    public string IconName { get; }
    public bool IsLoading { get; set; }
    public bool IsLoaded { get; set; }
    public List<Package> Packages { get; private set; } = new();

    private CloudAssetPanel _panel;

    public override bool HasChildren => true;
    public override string Name => DisplayName;

    public CloudCategoryNode(string typeFilter, string displayName, string icon, CloudAssetPanel panel)
    {
        TypeFilter = typeFilter;
        DisplayName = displayName;
        IconName = icon;
        _panel = panel;
        Value = this;
    }

    public void SetPackages(List<Package> packages)
    {
        Packages = packages;
        IsLoaded = true;
        Clear();

        foreach (var pkg in packages)
        {
            AddItem(new CloudPackageNode(pkg));
        }
    }

    protected override void BuildChildren()
    {
        // Load on expand
        if (!IsLoaded && !IsLoading && _panel != null)
        {
            _ = _panel.LoadCategoryAssets(this);
        }
    }

    public override void OnPaint(VirtualWidget item)
    {
        PaintSelection(item);

        var rect = item.Rect;

        // Draw folder icon
        var iconColor = Theme.Yellow;
        if (IsLoading)
            iconColor = Theme.Primary;

        Paint.SetPen(iconColor);
        Paint.DrawIcon(rect, IconName, 16, TextFlag.LeftCenter);

        rect.Left += 22;

        // Draw name
        Paint.SetPen(Theme.Text);
        Paint.SetDefaultFont(9, item.Selected ? 600 : 400);
        Paint.DrawText(rect, DisplayName, TextFlag.LeftCenter);

        // Draw count if loaded
        if (Packages.Count > 0)
        {
            var countText = $"({Packages.Count})";
            Paint.SetPen(Theme.Text.WithAlpha(0.5f));
            Paint.SetDefaultFont(8, 400);
            var countRect = new Rect(item.Rect.Right - 50, item.Rect.Top, 46, item.Rect.Height);
            Paint.DrawText(countRect, countText, TextFlag.RightCenter);
        }
    }
}

/// <summary>
/// Tree node representing search results
/// </summary>
internal class CloudSearchResultsNode : TreeNode
{
    public string Query { get; }
    public List<Package> Packages { get; }

    public override bool HasChildren => Packages.Count > 0;
    public override string Name => $"Search: {Query}";

    public CloudSearchResultsNode(string query, List<Package> packages)
    {
        Query = query;
        Packages = packages;
        Value = this;

        foreach (var pkg in packages)
        {
            AddItem(new CloudPackageNode(pkg));
        }
    }

    public override void OnPaint(VirtualWidget item)
    {
        PaintSelection(item);

        var rect = item.Rect;

        // Draw search icon
        Paint.SetPen(Theme.Primary);
        Paint.DrawIcon(rect, "search", 16, TextFlag.LeftCenter);

        rect.Left += 22;

        // Draw query
        Paint.SetPen(Theme.Text);
        Paint.SetDefaultFont(9, item.Selected ? 600 : 400);

        var displayText = $"\"{Query}\"";
        if (displayText.Length > 20)
            displayText = displayText.Substring(0, 18) + "..\"";
        Paint.DrawText(rect, displayText, TextFlag.LeftCenter);

        // Draw count
        var countText = $"({Packages.Count})";
        Paint.SetPen(Theme.Text.WithAlpha(0.5f));
        Paint.SetDefaultFont(8, 400);
        var countRect = new Rect(item.Rect.Right - 50, item.Rect.Top, 46, item.Rect.Height);
        Paint.DrawText(countRect, countText, TextFlag.RightCenter);
    }
}

/// <summary>
/// Tree node representing a single cloud package/asset
/// </summary>
internal class CloudPackageNode : TreeNode
{
    public Package Package { get; }
    public string FullIdent { get; }
    public string Title { get; }
    public string Author { get; }

    public override bool HasChildren => false;
    public override string Name => Title;

    public CloudPackageNode(Package package)
    {
        Package = package;
        FullIdent = package.FullIdent;
        Title = package.Title ?? package.FullIdent;
        Author = package.Org?.Title ?? "Unknown";
        Value = this;
    }

    public override void OnPaint(VirtualWidget item)
    {
        PaintSelection(item);

        var rect = item.Rect;

        // Draw icon based on package type
        var icon = GetIconForType(Package.PackageType);
        var iconColor = GetColorForType(Package.PackageType);

        Paint.SetPen(iconColor);
        Paint.DrawIcon(rect, icon, 14, TextFlag.LeftCenter);

        rect.Left += 20;

        // Draw title
        Paint.SetPen(Theme.Text);
        Paint.SetDefaultFont(9, item.Selected ? 600 : 400);

        var title = Title;
        if (title.Length > 30)
            title = title.Substring(0, 28) + "..";
        Paint.DrawText(rect, title, TextFlag.LeftCenter);
    }

    public override bool OnContextMenu()
    {
        var menu = new ContextMenu(null);

        menu.AddOption("Open in Browser", "open_in_new", () =>
        {
            var url = $"https://sbox.game/{FullIdent}";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        });

        menu.AddOption("Copy Identifier", "content_copy", () =>
        {
            EditorUtility.Clipboard.Copy(FullIdent);
        });

        menu.OpenAtCursor();
        return true;
    }

    private static string GetIconForType(Package.Type type)
    {
        return type switch
        {
            Package.Type.Model => "view_in_ar",
            Package.Type.Material => "texture",
            Package.Type.Sound => "audiotrack",
            Package.Type.Map => "landscape",
            _ => "cloud_download"
        };
    }

    private static Color GetColorForType(Package.Type type)
    {
        return type switch
        {
            Package.Type.Model => new Color(0.9f, 0.6f, 0.3f),
            Package.Type.Material => new Color(0.9f, 0.4f, 0.6f),
            Package.Type.Sound => new Color(0.4f, 0.7f, 1.0f),
            Package.Type.Map => new Color(0.9f, 0.9f, 0.3f),
            _ => Theme.Text.WithAlpha(0.7f)
        };
    }
}
