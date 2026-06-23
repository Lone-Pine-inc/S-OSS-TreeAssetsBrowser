using Editor;
using Sandbox;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GeneralGame.Editor;

/// <summary>
/// Helper for building asset context menus with Create Material/Texture options.
/// </summary>
public static class AssetContextMenuHelper
{
    // Builds the right-click menu for a single file/asset. Shared by the tree view and the icon grid
    // so both show identical options. Rename UI differs per view, so it is passed in via onRename.
    public static void BuildFileMenu(Menu menu, string fullPath, Asset asset, Action onRename, Action onChanged)
    {
        var fileName = Path.GetFileName(fullPath);

        if (asset != null)
            menu.AddOption("Open in Editor", "edit", () => asset.OpenInEditor());
        else
            menu.AddOption("Open", "open_in_new", () => EditorUtility.OpenFolder(fullPath));

        menu.AddOption("Show in Explorer", "folder_open", () => EditorUtility.OpenFileFolder(fullPath));

        menu.AddSeparator();

        if (asset != null)
            menu.AddOption("Copy Relative Path", "content_paste_go", () => EditorUtility.Clipboard.Copy(asset.RelativePath));
        menu.AddOption("Copy Absolute Path", "content_paste", () => EditorUtility.Clipboard.Copy(fullPath));

        // Asset-type specific options (Create Material, Create Texture, etc.)
        AddAssetTypeOptions(menu, asset);

        menu.AddSeparator();

        menu.AddOption("Rename", "edit", () => onRename?.Invoke());
        menu.AddOption("Duplicate", "file_copy", () =>
        {
            DuplicateFile(fullPath);
            onChanged?.Invoke();
        });

        menu.AddSeparator();

        var parentFolder = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(parentFolder))
        {
            var createMenu = menu.AddMenu("Create", "add");
            AssetCreator.AddOptions(createMenu, parentFolder);
            menu.AddSeparator();
        }

        menu.AddOption("Delete", "delete", () =>
        {
            var confirm = new PopupWindow(
                "Delete File",
                $"Are you sure you want to delete '{fileName}'?",
                "Cancel",
                new Dictionary<string, Action>()
                {
                    { "Delete", () =>
                        {
                            try
                            {
                                DeleteFileWithCompiled(fullPath, asset);
                                onChanged?.Invoke();
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
        });
    }

    // Builds the right-click menu for a folder. Shared by the tree view and the icon grid.
    public static void BuildFolderMenu(Menu menu, string fullPath, string displayName, bool isRoot,
        Action onOpen, Action onRename, Action onRefresh, Action onDeleted)
    {
        if (onOpen != null)
            menu.AddOption("Open", "folder_open", () => onOpen());

        menu.AddOption("Open in Explorer", "launch", () => EditorUtility.OpenFolder(fullPath));

        menu.AddSeparator();

        var createMenu = menu.AddMenu("Create", "add");
        AssetCreator.AddOptions(createMenu, fullPath);

        // Paste files copied from Windows Explorer into this folder
        if (WindowsClipboard.HasFiles())
        {
            menu.AddOption("Paste", "content_paste", () =>
            {
                PasteFromClipboard(fullPath);
                onRefresh?.Invoke();
            });
        }

        menu.AddSeparator();

        if (!isRoot)
            menu.AddOption("Rename", "edit", () => onRename?.Invoke());

        menu.AddOption("Copy Path", "content_copy", () => EditorUtility.Clipboard.Copy(fullPath));
        menu.AddOption("Copy Relative Path", "content_copy", () =>
        {
            var relativePath = Path.GetRelativePath(Project.Current?.GetRootPath() ?? "", fullPath);
            EditorUtility.Clipboard.Copy(relativePath);
        });

        menu.AddSeparator();

        menu.AddOption("Refresh", "refresh", () => onRefresh?.Invoke());

        if (!isRoot)
        {
            menu.AddSeparator();
            menu.AddOption("Delete", "delete", () =>
            {
                var confirm = new PopupWindow(
                    "Delete Folder",
                    $"Are you sure you want to delete '{displayName}'?\nAll contents will be deleted.",
                    "Cancel",
                    new Dictionary<string, Action>()
                    {
                        { "Delete", () =>
                            {
                                try
                                {
                                    Directory.Delete(fullPath, recursive: true);
                                    onDeleted?.Invoke();
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
    }

    // Duplicates a file next to itself, finding a free "_copy" name.
    public static void DuplicateFile(string filePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            var nameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
            var extension = Path.GetExtension(filePath);

            var newName = $"{nameWithoutExt}_copy{extension}";
            var newPath = Path.Combine(directory, newName);

            var counter = 1;
            while (File.Exists(newPath))
            {
                newName = $"{nameWithoutExt}_copy{counter++}{extension}";
                newPath = Path.Combine(directory, newName);
            }

            File.Copy(filePath, newPath);

            // Register the new file so it gets compiled and recognised as an asset immediately
            // (without this it shows up uncompiled until an editor restart or rename).
            AssetSystem.RegisterFile(newPath);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to duplicate file: {ex.Message}");
        }
    }

    // Registers a freshly created/copied/moved path with the asset system so it compiles right away.
    // Accepts a single file or a directory (registers every file inside, recursively).
    public static void RegisterNewPath(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    if (ShouldRegister(file))
                        AssetSystem.RegisterFile(file);
                }
            }
            else if (File.Exists(path) && ShouldRegister(path))
            {
                AssetSystem.RegisterFile(path);
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to register '{path}': {ex.Message}");
        }
    }

    // Copies the files currently on the Windows clipboard into the target folder (always copy, never move).
    public static void PasteFromClipboard(string targetFolder)
    {
        if (string.IsNullOrEmpty(targetFolder) || !Directory.Exists(targetFolder))
            return;

        foreach (var file in WindowsClipboard.GetFiles())
        {
            if (string.IsNullOrEmpty(file))
                continue;

            try
            {
                var name = Path.GetFileName(file.TrimEnd('\\', '/'));
                var destPath = Path.Combine(targetFolder, name);

                // Don't paste a folder into itself
                if (Path.GetFullPath(file).Equals(Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase))
                    destPath = MakeUniquePath(destPath);
                else if (File.Exists(destPath) || Directory.Exists(destPath))
                    destPath = MakeUniquePath(destPath);

                if (Directory.Exists(file))
                    CopyDirectory(file, destPath);
                else if (File.Exists(file))
                    File.Copy(file, destPath, overwrite: false);
                else
                    continue;

                RegisterNewPath(destPath);
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to paste '{file}': {ex.Message}");
            }
        }
    }

    private static string MakeUniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return path;

        var directory = Path.GetDirectoryName(path);
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);

        int counter = 1;
        string candidate;
        do
        {
            var suffix = counter == 1 ? "_copy" : $"_copy{counter}";
            candidate = Path.Combine(directory, $"{name}{suffix}{ext}");
            counter++;
        }
        while (File.Exists(candidate) || Directory.Exists(candidate));

        return candidate;
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

    // True when the path lives outside the current project (e.g. dragged in from Windows Explorer).
    // Such sources must be copied, never moved, so we don't delete files from their original location.
    public static bool IsExternalSource(string path)
    {
        try
        {
            var root = Project.Current?.GetRootPath();
            if (string.IsNullOrEmpty(root))
                return false;

            var full = Path.GetFullPath(path);
            var rootFull = Path.GetFullPath(root);
            return !full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    // ===== Backward compatibility: move an asset and optionally fix references to its old path =====

    private static readonly HashSet<string> NonTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".tga", ".psd", ".bmp", ".gif", ".dds", ".hdr",
        ".fbx", ".obj", ".gltf", ".glb", ".dmx", ".blend",
        ".wav", ".mp3", ".ogg", ".flac",
        ".vtex", ".vsnd", ".vmdl", ".dll", ".pdb", ".exe", ".zip", ".bin"
    };

    // True if this is a single registered asset being moved inside the project - the case the
    // backward-compatibility reference check applies to.
    public static bool IsBackwardCompatMove(IReadOnlyList<string> files, bool isCopy)
    {
        if (!BrowserSettings.BackwardCompatibility) return false;
        if (isCopy) return false;
        if (files == null || files.Count != 1) return false;

        var file = files[0];
        if (string.IsNullOrEmpty(file) || IsExternalSource(file) || Directory.Exists(file)) return false;
        if (!File.Exists(file)) return false;

        var asset = AssetSystem.FindByPath(file);
        return asset != null && !asset.IsDeleted;
    }

    // Moves an asset, first asking (via a modal) whether to update references to its old path.
    public static void MoveAssetWithReferenceCheck(string sourceFile, string targetFolder, Action onComplete)
    {
        var asset = AssetSystem.FindByPath(sourceFile);
        if (asset == null || asset.IsDeleted)
            return;

        // Don't bother if it's already in the target folder
        if (string.Equals(Path.GetFullPath(Path.GetDirectoryName(sourceFile)), Path.GetFullPath(targetFolder), StringComparison.OrdinalIgnoreCase))
            return;

        var assetsRoot = Project.Current?.GetAssetsPath();
        var oldRef = asset.RelativePath?.Replace('\\', '/');
        var newAbs = Path.Combine(targetFolder, Path.GetFileName(sourceFile));
        var newRef = string.IsNullOrEmpty(assetsRoot)
            ? null
            : Path.GetRelativePath(assetsRoot, newAbs).Replace('\\', '/');

        void JustMove()
        {
            EditorUtility.MoveAssetToDirectory(asset, targetFolder);
            onComplete?.Invoke();
        }

        // Can't compute a clean reference (asset outside the assets mount) - just move normally
        if (string.IsNullOrEmpty(oldRef) || string.IsNullOrEmpty(newRef) || newRef.StartsWith(".."))
        {
            JustMove();
            return;
        }

        var affected = FindFilesReferencing(oldRef);
        affected.RemoveAll(f => string.Equals(Path.GetFullPath(f), Path.GetFullPath(asset.AbsolutePath), StringComparison.OrdinalIgnoreCase));

        var dialog = new MoveReferencesDialog(oldRef, newRef, affected,
            onJustMove: JustMove,
            onMoveAndUpdate: () =>
            {
                EditorUtility.MoveAssetToDirectory(asset, targetFolder);
                RewriteReferences(affected, oldRef, newRef);
                onComplete?.Invoke();
            });
        dialog.Show();
    }

    // Matches the reference only at a path boundary, so "models/foo.vmdl" doesn't match inside
    // "othermodels/foo.vmdl" or "sub/models/foo.vmdl". A trailing "_c" (compiled form) is still matched.
    private static Regex BuildReferenceRegex(string reference)
    {
        return new Regex(@"(?<![\w./\\-])" + Regex.Escape(reference), RegexOptions.IgnoreCase);
    }

    // Finds every text-based project file that mentions the given reference path.
    public static List<string> FindFilesReferencing(string reference)
    {
        var results = new List<string>();
        if (string.IsNullOrEmpty(reference))
            return results;

        var root = Project.Current?.GetAssetsPath();
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            return results;

        var regex = BuildReferenceRegex(reference);

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (!IsScannableTextFile(file))
                continue;

            try
            {
                var info = new FileInfo(file);
                if (info.Length == 0 || info.Length > 16_000_000)
                    continue;

                var text = File.ReadAllText(file);
                if (regex.IsMatch(text))
                    results.Add(file);
            }
            catch
            {
                // Ignore unreadable files
            }
        }

        return results;
    }

    // Rewrites the old reference to the new one in each file (covers compiled "_c" forms too, since
    // they share the prefix). Re-registers the file afterwards.
    public static void RewriteReferences(IEnumerable<string> files, string oldRef, string newRef)
    {
        var regex = BuildReferenceRegex(oldRef);

        foreach (var file in files)
        {
            try
            {
                var text = File.ReadAllText(file);
                var updated = regex.Replace(text, _ => newRef);

                if (updated != text)
                {
                    File.WriteAllText(file, updated);
                    AssetSystem.RegisterFile(file);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to update references in '{file}': {ex.Message}");
            }
        }
    }

    private static bool IsScannableTextFile(string file)
    {
        if (!ShouldRegister(file))
            return false;

        var ext = Path.GetExtension(file);
        if (NonTextExtensions.Contains(ext))
            return false;

        return true;
    }

    private static bool ShouldRegister(string file)
    {
        var name = Path.GetFileName(file);
        if (name.StartsWith(".")) return false;
        if (name.EndsWith("_c", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.Contains(".generated", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    // Deletes a file, also removing its compiled "_c" sibling. Uses Asset.Delete when registered.
    public static void DeleteFileWithCompiled(string fullPath, Asset asset)
    {
        if (asset != null)
        {
            asset.Delete();
            return;
        }

        File.Delete(fullPath);

        var compiledPath = fullPath + "_c";
        if (File.Exists(compiledPath))
        {
            File.Delete(compiledPath);
        }
    }

    private static readonly HashSet<string> MeshExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".fbx", ".obj", ".dmx", ".gltf", ".glb" };

    // Builds the right-click menu shown when several files are selected at once.
    // Shared by the tree view and the icon grid so both behave identically.
    public static void BuildMultiFileMenu(Menu menu, List<(string Path, Asset Asset)> items, Action onChanged)
    {
        if (items == null || items.Count == 0) return;

        int count = items.Count;
        var assets = items.Where(i => i.Asset != null).Select(i => i.Asset).ToList();

        // Asset-type batch options (Create Material (N), Create Texture (N), etc.)
        bool addedTypeOptions = AddMultiAssetTypeOptions(menu, assets);

        if (addedTypeOptions)
            menu.AddSeparator();

        menu.AddOption($"Duplicate ({count})", "file_copy", () =>
        {
            foreach (var it in items)
                DuplicateFile(it.Path);
            onChanged?.Invoke();
        });

        menu.AddOption($"Delete ({count})", "delete", () =>
        {
            var confirm = new PopupWindow(
                "Delete Files",
                $"Are you sure you want to delete {count} item(s)?",
                "Cancel",
                new Dictionary<string, Action>()
                {
                    { "Delete", () =>
                        {
                            foreach (var it in items)
                            {
                                try
                                {
                                    DeleteFileWithCompiled(it.Path, it.Asset);
                                }
                                catch (Exception ex)
                                {
                                    Log.Error($"Failed to delete '{it.Path}': {ex.Message}");
                                }
                            }
                            onChanged?.Invoke();
                        }
                    }
                }
            );
            confirm.Show();
        });
    }

    // Adds asset-type specific batch options with a count suffix, e.g. "Create Material (3)".
    // Each option auto-creates the result next to every source asset (no save dialog).
    // Returns true if any option was added.
    public static bool AddMultiAssetTypeOptions(Menu menu, List<Asset> assets)
    {
        if (assets == null || assets.Count == 0) return false;

        var images = assets.Where(a => a.AssetType == AssetType.ImageFile).ToList();
        var shaders = assets.Where(a => a.AssetType == AssetType.Shader).ToList();
        var meshes = assets.Where(a => MeshExtensions.Contains(Path.GetExtension(a.AbsolutePath))).ToList();

        bool added = false;

        if (images.Count > 0)
        {
            menu.AddOption($"Create Material ({images.Count})", "image", () =>
            {
                foreach (var a in images) CreateMaterialFromImageAuto(a);
                Log.Info($"Created {images.Count} material(s)");
            });
            menu.AddOption($"Create Texture ({images.Count})", "texture", () =>
            {
                foreach (var a in images) CreateTextureFromImageAuto(a);
            });
            menu.AddOption($"Create Sprite ({images.Count})", "emoji_emotions", () =>
            {
                foreach (var a in images) CreateSpriteFromImageAuto(a);
            });
            added = true;
        }

        if (shaders.Count > 0)
        {
            if (added) menu.AddSeparator();
            menu.AddOption($"Create Material ({shaders.Count})", "image", () =>
            {
                foreach (var a in shaders) CreateMaterialFromShaderAuto(a);
            });
            added = true;
        }

        if (meshes.Count > 0)
        {
            if (added) menu.AddSeparator();
            menu.AddOption($"Create Model ({meshes.Count})", "view_in_ar", () =>
            {
                foreach (var a in meshes) CreateModelFromMeshAuto(a);
            });
            added = true;
        }

        var sounds = assets.Where(a => a.AssetType == AssetType.SoundFile).ToList();
        if (sounds.Count > 0)
        {
            if (added) menu.AddSeparator();

            // One sound event per file
            menu.AddOption($"Create Sound Event ({sounds.Count})", "graphic_eq", () =>
            {
                foreach (var a in sounds) CreateSoundEventFromAudioAuto(a);
                Log.Info($"Created {sounds.Count} sound event(s)");
            });

            // A single sound event that randomly picks between all the selected sounds
            menu.AddOption($"Create Random Sound Event ({sounds.Count})", "shuffle", () =>
            {
                CreateRandomSoundEventFromAudios(sounds);
            });

            added = true;
        }

        return added;
    }

    /// <summary>
    /// Add asset-type specific options like Create Material, Create Texture, etc.
    /// </summary>
    public static void AddAssetTypeOptions(Menu menu, Asset asset)
    {
        if (asset == null) return;

        var assetType = asset.AssetType;
        if (assetType == null) return;

        // Image files - can create Material, Texture, Sprite
        if (assetType == AssetType.ImageFile)
        {
            menu.AddSeparator();
            menu.AddOption("Create Material", "image", () => CreateMaterialFromImage(asset));
            menu.AddOption("Create Texture", "texture", () => CreateTextureFromImage(asset));
            menu.AddOption("Create Sprite", "emoji_emotions", () => CreateSpriteFromImage(asset));
        }

        // Shader files - can create Material
        if (assetType == AssetType.Shader)
        {
            menu.AddSeparator();
            menu.AddOption("Create Material", "image", () => CreateMaterialFromShader(asset));
        }

        // Mesh files (FBX, OBJ) - can create Model
        if (MeshExtensions.Contains(Path.GetExtension(asset.AbsolutePath)))
        {
            menu.AddSeparator();
            menu.AddOption("Create Model", "view_in_ar", () => CreateModelFromMesh(asset));
        }

        // Sound files - can create a Sound Event
        if (assetType == AssetType.SoundFile)
        {
            menu.AddSeparator();
            menu.AddOption("Create Sound Event", "graphic_eq", () => CreateSoundEventFromAudio(asset));
        }
    }

    private static void CreateTextureFromImage(Asset asset)
    {
        var fd = new FileDialog(null);
        fd.Title = "Create Texture from Image..";
        fd.Directory = Path.GetDirectoryName(asset.AbsolutePath);
        fd.DefaultSuffix = ".vtex";
        fd.SelectFile($"{asset.Name}.vtex");
        fd.SetFindFile();
        fd.SetModeSave();
        fd.SetNameFilter("Texture File (*.vtex)");

        if (!fd.Execute())
            return;

        File.WriteAllText(fd.SelectedFile, BuildVtexContent(asset));
        AssetSystem.RegisterFile(fd.SelectedFile);
    }

    private static void CreateMaterialFromImage(Asset asset)
    {
        var fd = new FileDialog(null);
        fd.Title = "Create Material from Image..";
        fd.Directory = Path.GetDirectoryName(asset.AbsolutePath);
        fd.DefaultSuffix = ".vmat";
        fd.SelectFile($"{GetMaterialBaseName(asset)}.vmat");
        fd.SetFindFile();
        fd.SetModeSave();
        fd.SetNameFilter("Material File (*.vmat)");

        if (!fd.Execute())
            return;

        File.WriteAllText(fd.SelectedFile, BuildImageMaterialContent(asset));
        AssetSystem.RegisterFile(fd.SelectedFile);
    }

    private static async void CreateSpriteFromImage(Asset asset)
    {
        var fd = new FileDialog(null);
        fd.Title = "Create Sprite from Image..";
        fd.Directory = Path.GetDirectoryName(asset.AbsolutePath);
        fd.DefaultSuffix = ".sprite";
        fd.SelectFile($"{asset.Name}.sprite");
        fd.SetFindFile();
        fd.SetModeSave();
        fd.SetNameFilter("Sprite File (*.sprite)");

        if (!fd.Execute())
            return;

        File.WriteAllText(fd.SelectedFile, BuildSpriteContent(asset));

        var resultAsset = AssetSystem.RegisterFile(fd.SelectedFile);
        while (!resultAsset.IsCompiledAndUpToDate)
        {
            await Task.Delay(10);
        }
    }

    private static void CreateMaterialFromShader(Asset asset)
    {
        var fd = new FileDialog(null);
        fd.Title = "Create Material from Shader..";
        fd.Directory = Path.GetDirectoryName(asset.AbsolutePath);
        fd.DefaultSuffix = ".vmat";
        fd.SelectFile($"{asset.Name}.vmat");
        fd.SetFindFile();
        fd.SetModeSave();
        fd.SetNameFilter("Material File (*.vmat)");

        if (!fd.Execute())
            return;

        File.WriteAllText(fd.SelectedFile, BuildShaderMaterialContent(asset));
        AssetSystem.RegisterFile(fd.SelectedFile);
    }

    private static void CreateModelFromMesh(Asset asset)
    {
        var targetPath = EditorUtility.SaveFileDialog("Create Model..", "vmdl", Path.ChangeExtension(asset.AbsolutePath, "vmdl"));
        if (targetPath == null)
            return;

        EditorUtility.CreateModelFromMeshFile(asset, targetPath);
    }

    private static void CreateSoundEventFromAudio(Asset asset)
    {
        var fd = new FileDialog(null);
        fd.Title = "Create Sound Event..";
        fd.Directory = Path.GetDirectoryName(asset.AbsolutePath);
        fd.DefaultSuffix = ".sound";
        fd.SelectFile($"{asset.Name}.sound");
        fd.SetFindFile();
        fd.SetModeSave();
        fd.SetNameFilter("Sound Event (*.sound)");

        if (!fd.Execute())
            return;

        File.WriteAllText(fd.SelectedFile, BuildSoundEventContent(new[] { GetVsndReference(asset) }));
        AssetSystem.RegisterFile(fd.SelectedFile);
    }

    // ===== Batch creators: auto-name next to each source asset, skip if it already exists =====

    private static void CreateMaterialFromImageAuto(Asset asset)
    {
        var directory = Path.GetDirectoryName(asset.AbsolutePath);
        var destPath = Path.Combine(directory, $"{GetMaterialBaseName(asset)}.vmat");
        if (File.Exists(destPath))
            return;

        File.WriteAllText(destPath, BuildImageMaterialContent(asset));
        AssetSystem.RegisterFile(destPath);
    }

    private static void CreateTextureFromImageAuto(Asset asset)
    {
        var directory = Path.GetDirectoryName(asset.AbsolutePath);
        var destPath = Path.Combine(directory, $"{asset.Name}.vtex");
        if (File.Exists(destPath))
            return;

        File.WriteAllText(destPath, BuildVtexContent(asset));
        AssetSystem.RegisterFile(destPath);
    }

    private static async void CreateSpriteFromImageAuto(Asset asset)
    {
        var directory = Path.GetDirectoryName(asset.AbsolutePath);
        var destPath = Path.Combine(directory, $"{asset.Name}.sprite");
        if (File.Exists(destPath))
            return;

        File.WriteAllText(destPath, BuildSpriteContent(asset));

        var resultAsset = AssetSystem.RegisterFile(destPath);
        while (!resultAsset.IsCompiledAndUpToDate)
        {
            await Task.Delay(10);
        }
    }

    private static void CreateMaterialFromShaderAuto(Asset asset)
    {
        var directory = Path.GetDirectoryName(asset.AbsolutePath);
        var destPath = Path.Combine(directory, $"{asset.Name}.vmat");
        if (File.Exists(destPath))
            return;

        File.WriteAllText(destPath, BuildShaderMaterialContent(asset));
        AssetSystem.RegisterFile(destPath);
    }

    private static void CreateModelFromMeshAuto(Asset asset)
    {
        var destPath = Path.ChangeExtension(asset.AbsolutePath, "vmdl");
        if (File.Exists(destPath))
            return;

        EditorUtility.CreateModelFromMeshFile(asset, destPath);
    }

    private static void CreateSoundEventFromAudioAuto(Asset asset)
    {
        var directory = Path.GetDirectoryName(asset.AbsolutePath);
        var destPath = Path.Combine(directory, $"{asset.Name}.sound");
        if (File.Exists(destPath))
            return;

        File.WriteAllText(destPath, BuildSoundEventContent(new[] { GetVsndReference(asset) }));
        AssetSystem.RegisterFile(destPath);
    }

    // Creates a single sound event that randomly picks between all the given audio files.
    private static void CreateRandomSoundEventFromAudios(List<Asset> assets)
    {
        if (assets == null || assets.Count == 0)
            return;

        var first = assets[0];
        var directory = Path.GetDirectoryName(first.AbsolutePath);
        var destPath = FindFreePath(directory, GetSoundBaseName(first), ".sound");

        var references = assets.Select(GetVsndReference).ToList();
        File.WriteAllText(destPath, BuildSoundEventContent(references));
        AssetSystem.RegisterFile(destPath);
    }

    // ===== Content builders shared by single and batch creators =====

    // Strips trailing texture-role suffixes (_color, _normal, ...) to get the material base name.
    private static string GetMaterialBaseName(Asset asset)
    {
        string[] suffixes = { "color", "ao", "normal", "metallic", "rough", "diff", "diffuse", "nrm", "spec", "selfillum", "mask" };

        var assetName = asset.Name;
        foreach (var t in suffixes)
        {
            if (assetName.EndsWith($"_{t}"))
                assetName = assetName.Substring(0, assetName.Length - (t.Length + 1));
        }
        return assetName;
    }

    // Builds a complex.shader material, auto-wiring sibling textures (normal/ao/rough/...) by name.
    private static string BuildImageMaterialContent(Asset asset)
    {
        var assetName = GetMaterialBaseName(asset);

        var assetPath = Path.GetDirectoryName(asset.AbsolutePath).NormalizeFilename(false);
        var assetPeers = AssetSystem.All
            .Where(x => x.AssetType == AssetType.ImageFile)
            .Where(x => x.AbsolutePath.StartsWith(assetPath))
            .ToArray();

        var assetPeersWithSameBaseName = assetPeers
            .Where(x => x.Name == assetName || x.Name.StartsWith(assetName + "_"))
            .ToArray();

        if (assetPeersWithSameBaseName.Length > 0)
        {
            assetPeers = assetPeersWithSameBaseName;
        }

        string texColor = assetPeers.Where(x => x.Name.Contains("_color") || x.Name.Contains("_diff")).Select(x => x.RelativePath).FirstOrDefault();
        texColor ??= asset.RelativePath;

        string texNormal = assetPeers.Where(x => x.Name.Contains("_nrm") || x.Name.Contains("_normal") || x.Name.Contains("_amb")).Select(x => x.RelativePath).FirstOrDefault() ?? "materials/default/default_normal.tga";
        string texAo = assetPeers.Where(x => x.Name.Contains("_ao") || x.Name.Contains("_occ") || x.Name.Contains("_amb")).Select(x => x.RelativePath).FirstOrDefault() ?? "materials/default/default_ao.tga";
        string texRough = assetPeers.Where(x => x.Name.Contains("_rough")).Select(x => x.RelativePath).FirstOrDefault() ?? "materials/default/default_rough.tga";

        string texMetallic = assetPeers.Where(x => x.Name.Contains("_metallic")).Select(x => x.RelativePath).FirstOrDefault();
        if (texMetallic != null)
        {
            texMetallic = $"\n\tF_METALNESS_TEXTURE 1\n\tF_SPECULAR 1\n\tTextureMetalness \"{texMetallic}\"";
        }

        string texSelfIllum = assetPeers.Where(x => x.Name.Contains("_selfillum")).Select(x => x.RelativePath).FirstOrDefault();
        if (texSelfIllum != null)
        {
            texSelfIllum = $"\n\tF_SELF_ILLUM 1\n\tTextureSelfIllumMask \"{texSelfIllum}\"";
        }

        string tintMask = assetPeers.Where(x => x.Name.Contains("_mask")).Select(x => x.RelativePath).FirstOrDefault();
        if (tintMask != null)
        {
            tintMask = $"\n\tF_TINT_MASK 1\n\tTextureTintMask \"{tintMask}\"";
        }

        return $@"
Layer0
{{
	shader ""shaders/complex.shader_c""

	TextureColor ""{texColor}""
	TextureAmbientOcclusion ""{texAo}""
	TextureNormal ""{texNormal}""
	TextureRoughness ""{texRough}""{texMetallic}{texSelfIllum}{tintMask}

}}
";
    }

    private static string BuildShaderMaterialContent(Asset asset)
    {
        var shaderPath = asset.GetCompiledFile();

        return $@"
Layer0
{{
	shader ""{shaderPath}""

}}
";
    }

    private static string BuildVtexContent(Asset asset)
    {
        var vtexContent = new Dictionary<string, object>
        {
            { "Sequences", new object[]
                {
                    new Dictionary<string, object>
                    {
                        { "Source", asset.RelativePath },
                        { "IsLooping", true }
                    }
                }
            }
        };

        return Json.Serialize(vtexContent);
    }

    private static string BuildSpriteContent(Asset asset)
    {
        var path = Path.ChangeExtension(asset.Path, Path.GetExtension(asset.AbsolutePath));
        var sprite = Sprite.FromTexture(Texture.Load(path));
        return sprite.Serialize().ToJsonString();
    }

    // Builds a .sound (SoundEvent) resource that plays a random sound from the given references.
    private static string BuildSoundEventContent(IEnumerable<string> vsndReferences)
    {
        var content = new Dictionary<string, object>
        {
            { "Volume", "1" },
            { "Pitch", "1" },
            { "SelectionMode", "Random" },
            { "Sounds", vsndReferences.ToArray() },
            { "__version", 1 }
        };

        return Json.Serialize(content);
    }

    // Sound events reference the compiled .vsnd, regardless of the source file extension.
    private static string GetVsndReference(Asset asset)
    {
        return Path.ChangeExtension(asset.RelativePath, "vsnd").Replace('\\', '/');
    }

    // Strips a trailing numeric index so "footstep_01" -> "footstep" for naming a combined event.
    private static string GetSoundBaseName(Asset asset)
    {
        var name = asset.Name;
        var underscore = name.LastIndexOf('_');
        if (underscore > 0 && int.TryParse(name.Substring(underscore + 1), out _))
            name = name.Substring(0, underscore);
        return name;
    }

    private static string FindFreePath(string directory, string baseName, string extension)
    {
        var path = Path.Combine(directory, $"{baseName}{extension}");

        int counter = 1;
        while (File.Exists(path))
            path = Path.Combine(directory, $"{baseName}_{counter++}{extension}");

        return path;
    }
}
