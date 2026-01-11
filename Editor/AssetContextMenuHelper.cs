using Editor;
using Sandbox;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GeneralGame.Editor;

/// <summary>
/// Helper for building asset context menus with Create Material/Texture options.
/// </summary>
public static class AssetContextMenuHelper
{
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
        var meshExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".fbx", ".obj", ".dmx", ".gltf", ".glb" };
        if (meshExtensions.Contains(Path.GetExtension(asset.AbsolutePath)))
        {
            menu.AddSeparator();
            menu.AddOption("Create Model", "view_in_ar", () => CreateModelFromMesh(asset));
        }
    }

    private static void CreateTextureFromImage(Asset asset)
    {
        var assetName = asset.Name;

        var fd = new FileDialog(null);
        fd.Title = "Create Texture from Image..";
        fd.Directory = Path.GetDirectoryName(asset.AbsolutePath);
        fd.DefaultSuffix = ".vtex";
        fd.SelectFile($"{assetName}.vtex");
        fd.SetFindFile();
        fd.SetModeSave();
        fd.SetNameFilter("Texture File (*.vtex)");

        if (!fd.Execute())
            return;

        var imagePath = asset.RelativePath;

        // Create simple vtex JSON structure
        var vtexContent = new Dictionary<string, object>
        {
            { "Sequences", new object[]
                {
                    new Dictionary<string, object>
                    {
                        { "Source", imagePath },
                        { "IsLooping", true }
                    }
                }
            }
        };

        var json = Json.Serialize(vtexContent);
        File.WriteAllText(fd.SelectedFile, json);

        AssetSystem.RegisterFile(fd.SelectedFile);
    }

    private static void CreateMaterialFromImage(Asset asset)
    {
        string[] types = new[] { "color", "ao", "normal", "metallic", "rough", "diff", "diffuse", "nrm", "spec", "selfillum", "mask" };

        var assetName = asset.Name;

        foreach (var t in types)
        {
            if (assetName.EndsWith($"_{t}"))
                assetName = assetName.Substring(0, assetName.Length - (t.Length + 1));
        }

        var fd = new FileDialog(null);
        fd.Title = "Create Material from Image..";
        fd.Directory = Path.GetDirectoryName(asset.AbsolutePath);
        fd.DefaultSuffix = ".vmat";
        fd.SelectFile($"{assetName}.vmat");
        fd.SetFindFile();
        fd.SetModeSave();
        fd.SetNameFilter("Material File (*.vmat)");

        if (!fd.Execute())
            return;

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

        var file = $@"
Layer0
{{
	shader ""shaders/complex.shader_c""

	TextureColor ""{texColor}""
	TextureAmbientOcclusion ""{texAo}""
	TextureNormal ""{texNormal}""
	TextureRoughness ""{texRough}""{texMetallic}{texSelfIllum}{tintMask}

}}
";
        File.WriteAllText(fd.SelectedFile, file);
        AssetSystem.RegisterFile(fd.SelectedFile);
    }

    private static async void CreateSpriteFromImage(Asset asset)
    {
        var assetName = asset.Name;

        var fd = new FileDialog(null);
        fd.Title = "Create Sprite from Image..";
        fd.Directory = Path.GetDirectoryName(asset.AbsolutePath);
        fd.DefaultSuffix = ".sprite";
        fd.SelectFile($"{assetName}.sprite");
        fd.SetFindFile();
        fd.SetModeSave();
        fd.SetNameFilter("Sprite File (*.sprite)");

        if (!fd.Execute())
            return;

        var path = Path.ChangeExtension(asset.Path, Path.GetExtension(asset.AbsolutePath));
        var sprite = Sprite.FromTexture(Texture.Load(path));
        var json = sprite.Serialize().ToJsonString();
        File.WriteAllText(fd.SelectedFile, json);

        var resultAsset = AssetSystem.RegisterFile(fd.SelectedFile);
        while (!resultAsset.IsCompiledAndUpToDate)
        {
            await Task.Delay(10);
        }
    }

    private static void CreateMaterialFromShader(Asset asset)
    {
        var assetName = asset.Name;

        var fd = new FileDialog(null);
        fd.Title = "Create Material from Shader..";
        fd.Directory = Path.GetDirectoryName(asset.AbsolutePath);
        fd.DefaultSuffix = ".vmat";
        fd.SelectFile($"{assetName}.vmat");
        fd.SetFindFile();
        fd.SetModeSave();
        fd.SetNameFilter("Material File (*.vmat)");

        if (!fd.Execute())
            return;

        var shaderPath = asset.GetCompiledFile();

        var file = $@"
Layer0
{{
	shader ""{shaderPath}""

}}
";
        File.WriteAllText(fd.SelectedFile, file);
        AssetSystem.RegisterFile(fd.SelectedFile);
    }

    private static void CreateModelFromMesh(Asset asset)
    {
        var targetPath = EditorUtility.SaveFileDialog("Create Model..", "vmdl", Path.ChangeExtension(asset.AbsolutePath, "vmdl"));
        if (targetPath == null)
            return;

        EditorUtility.CreateModelFromMeshFile(asset, targetPath);
    }
}
