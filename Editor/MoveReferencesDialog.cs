using Editor;
using Label = Editor.Label;
using Sandbox;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GeneralGame.Editor;

// Shown when moving a single asset with Backward Compatibility on. Lists the files that reference the
// asset's current path and lets the user either just move it, or move it and rewrite those references.
internal class MoveReferencesDialog : Dialog
{
    public MoveReferencesDialog(string oldRef, string newRef, List<string> affectedFiles, Action onJustMove, Action onMoveAndUpdate) : base(null)
    {
        affectedFiles ??= new List<string>();

        Window.WindowTitle = "Move Asset";
        Window.Size = new Vector2(580, 440);
        Window.MinimumSize = new Vector2(420, 300);

        Layout = Layout.Column();
        Layout.Margin = 16;
        Layout.Spacing = 10;

        var header = Layout.Add(new Label("Update references to this asset?", this));
        header.SetStyles("font-size: 14px; font-weight: 600;");

        var pathLabel = Layout.Add(new Label($"{oldRef}\n→ {newRef}", this));
        pathLabel.SetStyles("color: #aaa; font-size: 11px;");
        pathLabel.WordWrap = true;

        if (affectedFiles.Count > 0)
        {
            var countLabel = Layout.Add(new Label($"{affectedFiles.Count} file(s) will be changed:", this));
            countLabel.SetStyles("font-size: 12px;");

            // Scrollable list of affected files (project-relative for readability)
            var scroll = Layout.Add(new ScrollArea(this));
            scroll.SetSizeMode(SizeMode.CanGrow, SizeMode.CanGrow);

            var list = new Widget(scroll);
            list.Layout = Layout.Column();
            list.Layout.Margin = 4;
            list.Layout.Spacing = 2;

            var projectRoot = Project.Current?.GetRootPath() ?? "";
            foreach (var file in affectedFiles)
            {
                var display = string.IsNullOrEmpty(projectRoot)
                    ? file
                    : Path.GetRelativePath(projectRoot, file).Replace('\\', '/');

                var row = list.Layout.Add(new Label(display, list));
                row.SetStyles("font-size: 11px; color: #ddd;");
            }

            list.Layout.AddStretchCell();
            scroll.Canvas = list;
        }
        else
        {
            var none = Layout.Add(new Label("No other files reference this asset.", this));
            none.SetStyles("color: #aaa; font-size: 12px;");
            Layout.AddStretchCell();
        }

        // Buttons
        var buttonRow = Layout.AddRow();
        buttonRow.Spacing = 8;
        buttonRow.AddStretchCell();

        var cancelBtn = buttonRow.Add(new Button("Cancel", this));
        cancelBtn.MinimumWidth = 80;
        cancelBtn.Clicked = Close;

        if (affectedFiles.Count > 0)
        {
            var moveBtn = buttonRow.Add(new Button("Just Move", this));
            moveBtn.MinimumWidth = 90;
            moveBtn.Clicked = () =>
            {
                onJustMove?.Invoke();
                Close();
            };

            var updateBtn = buttonRow.Add(new Button.Primary($"Move & Update ({affectedFiles.Count})", this));
            updateBtn.MinimumWidth = 140;
            updateBtn.Clicked = () =>
            {
                onMoveAndUpdate?.Invoke();
                Close();
            };
        }
        else
        {
            var moveBtn = buttonRow.Add(new Button.Primary("Move", this));
            moveBtn.MinimumWidth = 90;
            moveBtn.Clicked = () =>
            {
                onJustMove?.Invoke();
                Close();
            };
        }
    }
}
