using Editor;
using Sandbox;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GeneralGame.Editor;

/// <summary>
/// Tree node representing a folder in the asset browser
/// </summary>
public class AssetFolderNode : TreeNode
{
    public string FullPath { get; }
    public string DisplayName { get; }
    public string IconName { get; set; }

    private FileSystemWatcher _watcher;
    private bool _isRoot;

    public override bool HasChildren => Directory.Exists(FullPath) &&
        (Directory.EnumerateDirectories(FullPath).Any() || Directory.EnumerateFiles(FullPath).Any());

    public override string Name => DisplayName;
    public override bool CanEdit => !_isRoot;

    public AssetFolderNode(string path, string displayName = null, string icon = "folder") : base()
    {
        FullPath = Path.GetFullPath(path);
        DisplayName = displayName ?? Path.GetFileName(path);
        IconName = icon;
        _isRoot = displayName != null;
        Value = this;

        // Watch for changes
        if (Directory.Exists(FullPath))
        {
            try
            {
                _watcher = new FileSystemWatcher(FullPath);
                _watcher.EnableRaisingEvents = true;
                _watcher.Created += OnFileSystemChanged;
                _watcher.Deleted += OnFileSystemChanged;
                _watcher.Renamed += OnFileSystemChanged;
            }
            catch
            {
                // Ignore watcher errors
            }
        }
    }

    ~AssetFolderNode()
    {
        _watcher?.Dispose();
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        MainThread.Queue(Dirty);
    }

    /// <summary>
    /// Public method to force building children
    /// </summary>
    public void EnsureChildrenBuilt()
    {
        if (Children == null || !Children.Any())
        {
            BuildChildren();
        }
    }

    protected override void BuildChildren()
    {
        Clear();

        if (!Directory.Exists(FullPath))
            return;

        try
        {
            // Add subdirectories first
            var directories = Directory.GetDirectories(FullPath)
                .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase);

            foreach (var dir in directories)
            {
                var dirInfo = new DirectoryInfo(dir);

                // Skip hidden folders
                if (dirInfo.Attributes.HasFlag(FileAttributes.Hidden))
                    continue;

                // Skip obj folder in code directories
                if (dirInfo.Name.Equals("obj", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Skip .sbox folder
                if (dirInfo.Name.StartsWith("."))
                    continue;

                AddItem(new AssetFolderNode(dir));
            }

            // Add files
            var files = Directory.GetFiles(FullPath)
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);

                // Skip hidden files
                if (fileInfo.Attributes.HasFlag(FileAttributes.Hidden))
                    continue;

                // Skip generated/meta files
                var fileName = fileInfo.Name;
                if (fileName.Contains(".generated", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) ||
                    fileName.StartsWith("."))
                    continue;

                // Skip compiled assets if source exists
                if (fileName.EndsWith("_c"))
                {
                    var sourcePath = file[..^2]; // Remove _c
                    if (File.Exists(sourcePath))
                        continue;
                }

                AddItem(new AssetFileNode(file));
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"Error building folder tree: {ex.Message}");
        }
    }

    public override void OnPaint(VirtualWidget item)
    {
        PaintSelection(item);

        var rect = item.Rect;

        // Folder icon
        Paint.SetPen(Theme.Yellow);
        var iconRect = Paint.DrawIcon(rect, item.IsOpen ? "folder_open" : IconName, 16, TextFlag.LeftCenter);

        rect.Left += 22;

        // Folder name
        Paint.SetPen(Theme.Text);
        Paint.SetDefaultFont();
        Paint.DrawText(rect, DisplayName, TextFlag.LeftCenter);
    }

    public override bool OnContextMenu()
    {
        var menu = new ContextMenu(null);

        menu.AddOption("Open in Explorer", "folder_open", () =>
        {
            EditorUtility.OpenFolder(FullPath);
        });

        menu.AddSeparator();

        // Create submenu
        var createMenu = menu.AddMenu("Create", "add");
        AssetCreator.AddOptions(createMenu, FullPath);
        menu.AddSeparator();

        if (!_isRoot)
        {
            menu.AddOption("Rename", "edit", () =>
            {
                ShowRenameDialog();
            });
        }

        menu.AddOption("Copy Path", "content_copy", () =>
        {
            EditorUtility.Clipboard.Copy(FullPath);
        });

        menu.AddOption("Copy Relative Path", "content_copy", () =>
        {
            var relativePath = Path.GetRelativePath(Project.Current?.GetRootPath() ?? "", FullPath);
            EditorUtility.Clipboard.Copy(relativePath);
        });

        menu.AddSeparator();

        menu.AddOption("Refresh", "refresh", () =>
        {
            Dirty();
        });

        if (!_isRoot)
        {
            menu.AddSeparator();

            menu.AddOption("Delete", "delete", () =>
            {
                var confirm = new PopupWindow(
                    "Delete Folder",
                    $"Are you sure you want to delete '{DisplayName}'?\nAll contents will be deleted.",
                    "Cancel",
                    new Dictionary<string, Action>()
                    {
                        { "Delete", () =>
                            {
                                try
                                {
                                    Directory.Delete(FullPath, recursive: true);
                                    Parent?.Dirty();
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
            });
        }

        menu.OpenAtCursor();
        return true;
    }

    private void ShowRenameDialog()
    {
        var dialog = new RenameDialog("Rename Folder", DisplayName);
        dialog.OnConfirm = (newName) =>
        {
            if (string.IsNullOrWhiteSpace(newName) || newName == DisplayName)
                return;

            var newPath = Path.Combine(Path.GetDirectoryName(FullPath), newName);
            try
            {
                Directory.Move(FullPath, newPath);
                Parent?.Dirty();
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to rename folder: {ex.Message}");
            }
        };
        dialog.Show();
    }


    public override void OnRename(VirtualWidget item, string text, List<TreeNode> selection = null)
    {
        if (string.IsNullOrWhiteSpace(text) || text == DisplayName)
            return;

        var newPath = Path.Combine(Path.GetDirectoryName(FullPath), text);
        try
        {
            Directory.Move(FullPath, newPath);
            Parent?.Dirty();
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to rename folder: {ex.Message}");
        }
    }

    public override bool OnDragStart()
    {
        if (_isRoot)
            return false;

        var drag = new Drag(TreeView);
        drag.Data.Text = FullPath;
        drag.Data.Url = new Uri("file:///" + FullPath);
        drag.Execute();
        return true;
    }

    public override void OnDragHover(Widget.DragEvent ev)
    {
        ev.Action = DropAction.Copy;
    }

    public override void OnDrop(Widget.DragEvent ev)
    {
        foreach (var file in ev.Data.Files)
        {
            if (file.ToLowerInvariant() == FullPath.ToLowerInvariant())
                continue;

            try
            {
                var fileName = Path.GetFileName(file);
                var destPath = Path.Combine(FullPath, fileName);

                if (Directory.Exists(file))
                {
                    if (ev.KeyboardModifiers.HasFlag(KeyboardModifiers.Ctrl))
                        CopyDirectory(file, destPath);
                    else
                        Directory.Move(file, destPath);
                }
                else if (File.Exists(file))
                {
                    if (ev.KeyboardModifiers.HasFlag(KeyboardModifiers.Ctrl))
                        File.Copy(file, destPath);
                    else
                        File.Move(file, destPath);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to move/copy: {ex.Message}");
            }
        }

        Dirty();
        ev.Action = DropAction.Copy;
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)));
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
        }
    }

    /// <summary>
    /// Check if this folder or any of its contents matches the search filter.
    /// Scans filesystem directly to find matches in unopened folders.
    /// </summary>
    public bool MatchesFilter(string filter)
    {
        if (string.IsNullOrEmpty(filter))
            return true;

        // Check folder name
        if (DisplayName.ToLowerInvariant().Contains(filter))
            return true;

        // If children are already built, check them
        if (Children != null && Children.Any())
        {
            foreach (var child in Children)
            {
                if (child is AssetFolderNode folder && folder.MatchesFilter(filter))
                    return true;
                if (child is AssetFileNode file && file.MatchesFilter(filter))
                    return true;
            }
            return false;
        }

        // Otherwise scan filesystem directly
        if (!Directory.Exists(FullPath))
            return false;

        try
        {
            // Check files in this folder
            foreach (var file in Directory.GetFiles(FullPath))
            {
                var fileName = Path.GetFileName(file).ToLowerInvariant();
                if (fileName.Contains(filter))
                    return true;
            }

            // Check subfolders recursively
            foreach (var dir in Directory.GetDirectories(FullPath))
            {
                var dirName = Path.GetFileName(dir);

                // Skip hidden/system folders
                if (dirName.StartsWith(".") || dirName.Equals("obj", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Check folder name
                if (dirName.ToLowerInvariant().Contains(filter))
                    return true;

                // Recursively check contents
                var subFolder = new AssetFolderNode(dir);
                if (subFolder.MatchesFilter(filter))
                    return true;
            }
        }
        catch
        {
            // Ignore filesystem errors
        }

        return false;
    }

    /// <summary>
    /// Find a node by its full path
    /// </summary>
    public TreeNode FindNode(string path)
    {
        if (Path.GetFullPath(FullPath).Equals(path, StringComparison.OrdinalIgnoreCase))
            return this;

        // Build children if not built yet
        if (Children == null || !Children.Any())
            BuildChildren();

        foreach (var child in Children)
        {
            if (child is AssetFolderNode folder)
            {
                if (path.StartsWith(folder.FullPath, StringComparison.OrdinalIgnoreCase))
                {
                    var result = folder.FindNode(path);
                    if (result != null)
                        return result;
                }
            }
            else if (child is AssetFileNode file)
            {
                if (Path.GetFullPath(file.FullPath).Equals(path, StringComparison.OrdinalIgnoreCase))
                    return file;
            }
        }

        return null;
    }

    protected override bool HasDescendant(object obj)
    {
        if (obj is AssetFileNode fileNode)
        {
            return fileNode.FullPath.StartsWith(FullPath, StringComparison.OrdinalIgnoreCase);
        }
        if (obj is AssetFolderNode folderNode)
        {
            return folderNode.FullPath.StartsWith(FullPath, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }
}

/// <summary>
/// Simple popup for renaming files/folders
/// </summary>
internal class RenameDialog : PopupWidget
{
    private LineEdit _lineEdit;
    public Action<string> OnConfirm;

    public RenameDialog(string title, string initialText) : base(null)
    {
        Layout = Layout.Row();
        Layout.Margin = 4;
        Layout.Spacing = 4;

        _lineEdit = Layout.Add(new LineEdit(this));
        _lineEdit.Text = initialText;
        _lineEdit.MinimumWidth = 200;
        _lineEdit.SelectAll();
        _lineEdit.ReturnPressed += () =>
        {
            OnConfirm?.Invoke(_lineEdit.Text);
            Close();
        };

        var okBtn = Layout.Add(new Button("OK", this));
        okBtn.Clicked = () =>
        {
            OnConfirm?.Invoke(_lineEdit.Text);
            Close();
        };

        _lineEdit.Focus();
    }

    public new void Show()
    {
        OpenAtCursor();
        _lineEdit.Focus();
    }
}
