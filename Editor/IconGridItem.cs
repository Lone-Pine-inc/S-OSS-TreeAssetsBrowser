using Editor;
using Sandbox;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GeneralGame.Editor;

/// <summary>
/// A single item in the icon grid - displays file/folder with large icon
/// </summary>
public class IconGridItem : Widget
{
    public string FilePath { get; }
    public string FileName { get; }
    public string Extension { get; }
    public bool IsFolder { get; }
    public Asset Asset { get; private set; }

    /// <summary>
    /// Called when item is double-clicked (open file/folder)
    /// </summary>
    public Action<string> OnActivated;

    /// <summary>
    /// Called when folder is double-clicked (for navigation in grid)
    /// </summary>
    public Action<string> OnFolderNavigate;

    private bool _isSelected;
    private Pixmap _thumbnail;

    public const float ItemWidth = 80;
    public const float ItemHeight = 100;
    private const float IconSize = 64;

    private static readonly Dictionary<string, string> ExtensionIcons = new()
    {
        { ".vmdl", "view_in_ar" },
        { ".fbx", "view_in_ar" },
        { ".obj", "view_in_ar" },
        { ".gltf", "view_in_ar" },
        { ".glb", "view_in_ar" },
        { ".png", "image" },
        { ".jpg", "image" },
        { ".jpeg", "image" },
        { ".tga", "image" },
        { ".vtex", "image" },
        { ".psd", "image" },
        { ".vmat", "texture" },
        { ".wav", "audiotrack" },
        { ".mp3", "audiotrack" },
        { ".ogg", "audiotrack" },
        { ".vsnd", "audiotrack" },
        { ".cs", "code" },
        { ".razor", "code" },
        { ".scss", "style" },
        { ".css", "style" },
        { ".shader", "gradient" },
        { ".shdrgrph", "gradient" },
        { ".prefab", "inventory_2" },
        { ".scene", "landscape" },
        { ".json", "data_object" },
        { ".xml", "data_object" },
        { ".txt", "description" },
        { ".md", "description" },
        { ".item", "category" },
        { ".clothing", "checkroom" },
        { ".weapon", "sports_martial_arts" },
        { ".particle", "blur_on" },
        { ".vanmgrph", "animation" },
        { ".vpost", "auto_fix_high" }
    };

    private static readonly Dictionary<string, Color> ExtensionColors = new()
    {
        { ".cs", new Color(0.4f, 0.7f, 1.0f) },
        { ".razor", new Color(0.6f, 0.4f, 0.9f) },
        { ".shader", new Color(0.3f, 0.9f, 0.5f) },
        { ".vmdl", new Color(0.9f, 0.6f, 0.3f) },
        { ".vmat", new Color(0.9f, 0.4f, 0.6f) },
        { ".prefab", new Color(0.3f, 0.8f, 0.9f) },
        { ".scene", new Color(0.9f, 0.9f, 0.3f) },
    };

    public IconGridItem(Widget parent, string path, bool isFolder) : base(parent)
    {
        FilePath = Path.GetFullPath(path);
        FileName = Path.GetFileName(path);
        Extension = isFolder ? "" : Path.GetExtension(path).ToLowerInvariant();
        IsFolder = isFolder;

        Size = new Vector2(ItemWidth, ItemHeight);
        MinimumSize = new Vector2(ItemWidth, ItemHeight);
        MaximumSize = new Vector2(ItemWidth, ItemHeight);

        if (!isFolder)
        {
            Asset = AssetSystem.FindByPath(path);
            if (Asset != null)
            {
                _thumbnail = Asset.GetAssetThumb();
            }
        }

        ToolTip = BuildTooltip();

        // Force accept mouse events
        AcceptDrops = false;
    }

    private string BuildTooltip()
    {
        var tip = FilePath;

        if (!IsFolder && Asset != null)
        {
            tip += $"\nType: {Asset.AssetType?.FriendlyName ?? "Unknown"}";
        }

        if (!IsFolder)
        {
            var fileInfo = new FileInfo(FilePath);
            if (fileInfo.Exists)
            {
                tip += $"\nSize: {FormatFileSize(fileInfo.Length)}";
                tip += $"\nModified: {fileInfo.LastWriteTime:g}";
            }
        }

        return tip;
    }

    protected override void OnPaint()
    {
        var rect = LocalRect;

        // Background
        if (_isSelected)
        {
            Paint.SetBrush(Theme.Primary.WithAlpha(0.3f));
            Paint.DrawRect(rect, 4);
        }

        // Icon area
        var iconRect = new Rect(
            rect.Left + (rect.Width - IconSize) / 2,
            rect.Top + 4,
            IconSize,
            IconSize
        );

        // Draw thumbnail or icon
        if (_thumbnail != null)
        {
            Paint.Draw(iconRect, _thumbnail);
        }
        else
        {
            var icon = GetIcon();
            var iconColor = GetIconColor();
            Paint.SetPen(iconColor);
            Paint.DrawIcon(iconRect, icon, IconSize, TextFlag.Center);
        }

        // Filename
        var textRect = new Rect(
            rect.Left + 2,
            iconRect.Bottom + 2,
            rect.Width - 4,
            rect.Height - iconRect.Height - 8
        );

        Paint.SetPen(Theme.Text);
        Paint.SetDefaultFont(8, 400);

        var displayName = FileName;
        if (displayName.Length > 12)
            displayName = displayName.Substring(0, 10) + "..";

        Paint.DrawText(textRect, displayName, TextFlag.Center);
    }

    private string GetIcon()
    {
        if (IsFolder)
            return "folder";

        if (ExtensionIcons.TryGetValue(Extension, out var icon))
            return icon;

        return "description";
    }

    private Color GetIconColor()
    {
        if (IsFolder)
            return Theme.Yellow;

        if (Asset?.AssetType?.Color != null && Asset.AssetType.Color != default)
            return Asset.AssetType.Color;

        if (ExtensionColors.TryGetValue(Extension, out var color))
            return color;

        return Theme.Text.WithAlpha(0.7f);
    }

    private Vector2 _dragStartPos;
    private bool _isDragging;

    protected override void OnMousePress(MouseEvent e)
    {
        base.OnMousePress(e);

        if (e.LeftMouseButton)
        {
            _isSelected = true;
            _dragStartPos = e.LocalPosition;
            _isDragging = false;
            Update();

            if (!IsFolder && Asset != null)
            {
                EditorUtility.InspectorObject = Asset;
            }
        }
    }

    protected override void OnMouseMove(MouseEvent e)
    {
        base.OnMouseMove(e);

        if (e.ButtonState.HasFlag(MouseButtons.Left) && !_isDragging)
        {
            var delta = e.LocalPosition - _dragStartPos;
            if (delta.Length > 5) // Start drag after moving 5 pixels
            {
                _isDragging = true;
                StartDrag();
            }
        }
    }

    private void StartDrag()
    {
        var drag = new Drag(this);

        if (IsFolder)
        {
            drag.Data.Url = new Uri("file:///" + FilePath);
        }
        else if (Asset != null)
        {
            drag.Data.Text = Asset.RelativePath;
            drag.Data.Url = new Uri("file:///" + Asset.AbsolutePath);
        }
        else
        {
            drag.Data.Text = FilePath;
            drag.Data.Url = new Uri("file:///" + FilePath);
        }

        drag.Execute();
    }

    protected override void OnDoubleClick(MouseEvent e)
    {
        base.OnDoubleClick(e);

        if (e.LeftMouseButton)
        {
            if (IsFolder)
            {
                OnFolderNavigate?.Invoke(FilePath);
            }
            else
            {
                if (Asset != null)
                    Asset.OpenInEditor();
                else
                    EditorUtility.OpenFolder(FilePath);
            }
        }
    }

    protected override void OnContextMenu(ContextMenuEvent e)
    {
        var menu = new ContextMenu(this);

        if (IsFolder)
        {
            menu.AddOption("Open", "folder_open", () => OnFolderNavigate?.Invoke(FilePath));
            menu.AddOption("Open in Explorer", "launch", () => EditorUtility.OpenFolder(FilePath));

            menu.AddSeparator();

            menu.AddOption("Copy Path", "content_copy", () => EditorUtility.Clipboard.Copy(FilePath));

            menu.AddSeparator();

            // Create submenu
            var createMenu = menu.AddMenu("Create", "add");
            AssetCreator.AddOptions(createMenu, FilePath);

            menu.AddSeparator();

            menu.AddOption("Delete", "delete", () => DeleteFolder());
        }
        else
        {
            // Open options
            if (Asset != null)
            {
                menu.AddOption("Open in Editor", "edit", () => Asset.OpenInEditor());
            }
            else
            {
                menu.AddOption("Open", "open_in_new", () => EditorUtility.OpenFolder(FilePath));
            }
            menu.AddOption("Show in Explorer", "folder_open", () => EditorUtility.OpenFileFolder(FilePath));

            menu.AddSeparator();

            // Copy options
            if (Asset != null)
            {
                menu.AddOption("Copy Relative Path", "content_paste_go", () => EditorUtility.Clipboard.Copy(Asset.RelativePath));
            }
            menu.AddOption("Copy Absolute Path", "content_paste", () => EditorUtility.Clipboard.Copy(FilePath));

            // Asset-type specific options (Create Material, Create Texture, etc.)
            AssetContextMenuHelper.AddAssetTypeOptions(menu, Asset);

            menu.AddSeparator();

            // Edit options
            menu.AddOption("Rename", "edit", () => ShowRenameDialog());
            menu.AddOption("Duplicate", "file_copy", () => DuplicateFile());

            menu.AddSeparator();

            // Create submenu for quick asset creation in same folder
            var parentFolder = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(parentFolder))
            {
                var createMenu = menu.AddMenu("Create", "add");
                AssetCreator.AddOptions(createMenu, parentFolder);
                menu.AddSeparator();
            }

            menu.AddOption("Delete", "delete", () => DeleteFile());
        }

        menu.OpenAtCursor();
        e.Accepted = true;
    }


    private void ShowRenameDialog()
    {
        var dialog = new RenameDialog("Rename File", Path.GetFileNameWithoutExtension(FileName));
        dialog.OnConfirm = (newName) =>
        {
            if (string.IsNullOrWhiteSpace(newName))
                return;

            // Preserve extension if not provided
            if (!Path.HasExtension(newName))
            {
                newName += Extension;
            }

            if (newName == FileName)
                return;

            var newPath = Path.Combine(Path.GetDirectoryName(FilePath), newName);
            try
            {
                File.Move(FilePath, newPath);
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to rename file: {ex.Message}");
            }
        };
        dialog.Show();
    }

    private void DuplicateFile()
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            var nameWithoutExt = Path.GetFileNameWithoutExtension(FileName);

            var newName = $"{nameWithoutExt}_copy{Extension}";
            var newPath = Path.Combine(directory, newName);

            var counter = 1;
            while (File.Exists(newPath))
            {
                newName = $"{nameWithoutExt}_copy{counter++}{Extension}";
                newPath = Path.Combine(directory, newName);
            }

            File.Copy(FilePath, newPath);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to duplicate file: {ex.Message}");
        }
    }

    private void DeleteFile()
    {
        var confirm = new PopupWindow(
            "Delete File",
            $"Are you sure you want to delete '{FileName}'?",
            "Cancel",
            new Dictionary<string, Action>()
            {
                { "Delete", () =>
                    {
                        try
                        {
                            File.Delete(FilePath);
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"Failed to delete file: {ex.Message}");
                        }
                    }
                }
            }
        );
        confirm.Show();
    }

    private void DeleteFolder()
    {
        var confirm = new PopupWindow(
            "Delete Folder",
            $"Are you sure you want to delete '{FileName}'?\nAll contents will be deleted.",
            "Cancel",
            new Dictionary<string, Action>()
            {
                { "Delete", () =>
                    {
                        try
                        {
                            Directory.Delete(FilePath, recursive: true);
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"Failed to delete folder: {ex.Message}");
                        }
                    }
                }
            }
        );
        confirm.Show();
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }
}
