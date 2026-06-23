using Editor;
using Sandbox;
using Sandbox.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Runtime.Intrinsics.X86;
using static Sandbox.Connection;
using static Sandbox.Internal.IControlSheet;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace GeneralGame.Editor;

/// <summary>
/// Tree node representing a file/asset in the asset browser
/// </summary>
public class AssetFileNode : TreeNode
{
    public string FullPath { get; }
    public string FileName { get; }
    public string Extension { get; }
    public Asset Asset { get; private set; }

    public override bool HasChildren => false;
    public override string Name => Path.GetFileNameWithoutExtension(FileName);
    public override bool CanEdit => true;




    private static readonly Dictionary<string, string> ExtensionIcons = new()
    {
        // Models
        { ".vmdl", "view_in_ar" },
        { ".fbx", "view_in_ar" },
        { ".obj", "view_in_ar" },
        { ".gltf", "view_in_ar" },
        { ".glb", "view_in_ar" },
        
        // Textures
        { ".png", "image" },
        { ".jpg", "image" },
        { ".jpeg", "image" },
        { ".tga", "image" },
        { ".vtex", "image" },
        { ".psd", "image" },

        // Materials
        { ".vmat", "texture" },

        // Sounds
        { ".wav", "audiotrack" },
        { ".mp3", "audiotrack" },
        { ".ogg", "audiotrack" },
        { ".vsnd", "audiotrack" },

        // Code
        { ".cs", "code" },
        { ".razor", "code" },
        { ".scss", "style" },
        { ".css", "style" },
        { ".shader", "gradient" },
        { ".shdrgrph", "gradient" },

        // Prefabs & Scenes
        { ".prefab", "inventory_2" },
        { ".scene", "landscape" },

        // Data
        { ".json", "data_object" },
        { ".xml", "data_object" },
        { ".txt", "description" },
        { ".md", "description" },

        // Resources
        { ".item", "category" },
        { ".clothing", "checkroom" },
        { ".weapon", "sports_martial_arts" },

        // Other
        { ".particle", "blur_on" },
        { ".vanmgrph", "animation" },
        { ".vpost", "auto_fix_high" }
    };

    private static readonly Dictionary<string, Color> ExtensionColors = new()
    {
        { ".cs", new Color(0.4f, 0.7f, 1.0f) },      // Blue for C#
        { ".razor", new Color(0.6f, 0.4f, 0.9f) },  // Purple for Razor
        { ".shader", new Color(0.3f, 0.9f, 0.5f) }, // Green for shaders
        { ".vmdl", new Color(0.9f, 0.6f, 0.3f) },   // Orange for models
        { ".vmat", new Color(0.9f, 0.4f, 0.6f) },   // Pink for materials
        { ".prefab", new Color(0.3f, 0.8f, 0.9f) }, // Cyan for prefabs
        { ".scene", new Color(0.9f, 0.9f, 0.3f) },  // Yellow for scenes
    };

    public AssetFileNode(string path) : base()
    {
        FullPath = Path.GetFullPath(path);
        FileName = Path.GetFileName(path);
        Extension = Path.GetExtension(path).ToLowerInvariant();
        Value = this;

        // Try to find associated asset
        Asset = AssetSystem.FindByPath(path);
    }

    public override void OnPaint(VirtualWidget item)
    {
        PaintSelection(item);

        var rect = item.Rect;

        // Get icon
        var icon = GetIcon();
        var iconColor = GetIconColor();

        // Draw icon
        Paint.SetPen(iconColor);
        Paint.DrawIcon(rect, icon, 14, TextFlag.LeftCenter);

        rect.Left += 20;

        // Optionally draw the asset thumbnail right next to the type icon
        if (BrowserSettings.ShowTreeIcons)
        {
            var thumb = GetThumbnail();
            if (thumb != null)
            {
                const float thumbSize = 16f;
                var thumbRect = new Rect(rect.Left, rect.Top + (rect.Height - thumbSize) / 2, thumbSize, thumbSize);
                Paint.Draw(thumbRect, thumb);
                rect.Left += thumbSize + 4;
            }
        }

        // Draw filename
        Paint.SetPen(Theme.Text);
        Paint.SetDefaultFont(8, 400);

        var nameWithoutExt = Path.GetFileNameWithoutExtension(FileName);
        var nameRect = Paint.MeasureText(rect, nameWithoutExt, TextFlag.LeftCenter);
        Paint.DrawText(rect, nameWithoutExt, TextFlag.LeftCenter);

        // Draw extension in dimmer color
        rect.Left += nameRect.Width + 1;
        Paint.SetPen(Theme.Text.WithAlpha(0.4f));
        Paint.SetDefaultFont(7, 400);
        Paint.DrawText(rect, Extension, TextFlag.LeftCenter);
    }

    private Pixmap _thumbnail;
    private bool _thumbnailRequested;

    // Lazily fetches the asset thumbnail (only assets have one), cached after the first request.
    private Pixmap GetThumbnail()
    {
        if (!_thumbnailRequested)
        {
            _thumbnailRequested = true;
            if (Asset != null)
            {
                try { _thumbnail = Asset.GetAssetThumb(); }
                catch { _thumbnail = null; }
            }
        }
        return _thumbnail;
    }

    private string GetIcon()
    {
        // Use extension-based icon
        if (ExtensionIcons.TryGetValue(Extension, out var icon))
        {
            return icon;
        }

        return "description"; // Default file icon
    }

    private Color GetIconColor()
    {
        // Use asset type color if available
        if (Asset?.AssetType?.Color != null && Asset.AssetType.Color != default)
        {
            return Asset.AssetType.Color;
        }

        // Use extension-based color
        if (ExtensionColors.TryGetValue(Extension, out var color))
        {
            return color;
        }

        return Theme.Text.WithAlpha(0.7f);
    }

    public override bool OnContextMenu()
    {
        var menu = new ContextMenu(null);

        // If several files are selected, show the batch menu (Create Material (N), Delete (N), ...)
        var selectedFiles = TreeView?.SelectedItems?.OfType<AssetFileNode>().ToList() ?? new List<AssetFileNode>();
        if (selectedFiles.Count > 1 && selectedFiles.Contains(this))
        {
            var items = selectedFiles.Select(n => (n.FullPath, n.Asset)).ToList();
            AssetContextMenuHelper.BuildMultiFileMenu(menu, items, () => Parent?.Dirty());
        }
        else
        {
            AssetContextMenuHelper.BuildFileMenu(
                menu,
                FullPath,
                Asset,
                onRename: () => ShowRenameDialog(),
                onChanged: () => Parent?.Dirty());
        }

        menu.OpenAtCursor();
        return true;
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

            var oldCompiledPath = FullPath + "_c";
            var newPath = Path.Combine(Path.GetDirectoryName(FullPath), newName);
            try
            {
                File.Move(FullPath, newPath);

                // Delete old compiled _c file so it doesn't linger with the old name
                if (File.Exists(oldCompiledPath))
                {
                    File.Delete(oldCompiledPath);
                }

                Parent?.Dirty();
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to rename file: {ex.Message}");
            }
        };
        dialog.Show();
    }

    public override void OnRename(VirtualWidget item, string text, List<TreeNode> selection = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        // Preserve extension if not provided
        var newName = text;
        if (!Path.HasExtension(newName))
        {
            newName += Extension;
        }

        if (newName == FileName)
            return;

        var oldCompiledPath = FullPath + "_c";
        var newPath = Path.Combine(Path.GetDirectoryName(FullPath), newName);

        try
        {
            if (Asset != null)
            {
                EditorUtility.MoveAssetToDirectory(Asset, Path.GetDirectoryName(newPath));
            }
            else
            {
                File.Move(FullPath, newPath);
            }

            // Delete old compiled _c file so it doesn't linger with the old name
            if (File.Exists(oldCompiledPath))
            {
                File.Delete(oldCompiledPath);
            }

            Parent?.Dirty();
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to rename file: {ex.Message}");
        }
    }

    public override bool OnDragStart()
    {
        var drag = new Drag(TreeView);

        if (Asset != null)
        {
            drag.Data.Object = Asset;
        }

        drag.Data.Text = FullPath;
        drag.Data.Url = new Uri("file:///" + FullPath);
        drag.Execute();
        return true;
    }

    public override void OnActivated()
    {
        if (Asset != null)
        {
            Asset.OpenInEditor();
        }
        else
        {
            EditorUtility.OpenFolder(FullPath);
        }
    }

    /// <summary>
    /// Check if this file matches the search filter
    /// </summary>
    public bool MatchesFilter(string filter)
    {
        if (string.IsNullOrEmpty(filter))
            return true;

        // Check filename
        if (FileName.ToLowerInvariant().Contains(filter))
            return true;

        // Check asset type
        if (Asset?.AssetType?.FriendlyName?.ToLowerInvariant().Contains(filter) == true)
            return true;

        return false;
    }

    public override string GetTooltip()
    {
        var tip = FullPath;

        if (Asset != null)
        {
            tip += $"\nType: {Asset.AssetType?.FriendlyName ?? "Unknown"}";
        }

        var fileInfo = new FileInfo(FullPath);
        if (fileInfo.Exists)
        {
            tip += $"\nSize: {FormatFileSize(fileInfo.Length)}";
            tip += $"\nModified: {fileInfo.LastWriteTime:g}";
        }

        return tip;
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
