using System.Diagnostics;
using System.Reflection;
using MocRecovery;

internal static class Program
{
    [STAThread] static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainWindow());
    }
}
public sealed class MainWindow : Form
{
    private readonly TextBox input=new(){ReadOnly=true,Dock=DockStyle.Fill};
    private readonly TextBox destination=new(){ReadOnly=true,Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,BackColor=SystemColors.Control,AccessibleName="Output folder"};
    private readonly ListBox textures=new(){Dock=DockStyle.Fill,HorizontalScrollbar=true};
    private readonly TextBox report=new(){Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,WordWrap=true,Dock=DockStyle.Fill,Font=new Font("Consolas",10),BackColor=Color.FromArgb(245,247,250)};
    private readonly Button browse=new(){Text="Choose .moc3…",AutoSize=true};
    private readonly Button selectTextures=new(){Text="Choose textures…",AutoSize=true};
    private readonly Button clearTextures=new(){Text="Clear texture list",AutoSize=true};
    private readonly Button up=new(){Text="Move up",AutoSize=true};
    private readonly Button down=new(){Text="Move down",AutoSize=true};
    private readonly Button convert=new(){Text="Create .cmo3",AutoSize=true,Enabled=false};
    private readonly Button openFolder=new(){Text="Open folder",AutoSize=true,Enabled=false};
    private readonly LinkLabel status=new(){LinkArea=new LinkArea(0,0),Text="Choose a compiled model to begin.",AutoSize=true};
    private readonly ProgressBar progress=new(){Dock=DockStyle.Fill,Visible=false,Style=ProgressBarStyle.Continuous};
    private readonly Label progressPercent=new(){Text="0%",AutoSize=true,Visible=false,Anchor=AnchorStyles.Left,Margin=new Padding(10,0,0,0)};
    private readonly CheckBox inferDeformerNames=new(){Text="Guess deformer names from ArtMeshes (experimental)",Checked=true,AutoSize=true};
    private readonly CheckBox inferLinks=new(){Text="Rebuild linked parameter grids (inferred)",Checked=true,AutoSize=true};
    private readonly Button selectPsd=new(){Text="Add PSD files…",AutoSize=true};
    private readonly Button clearPsd=new(){Text="Clear PSDs",AutoSize=true};
    private readonly ListView psdList = new()
    {
        Dock = DockStyle.Fill, View = View.Details, HeaderStyle = ColumnHeaderStyle.None,
        FullRowSelect = true, HideSelection = false, ShowItemToolTips = true,
        AccessibleName = "Attached PSD files", Margin = new Padding(0, 0, 0, 8)
    };
    private readonly Button removePsd = new() { Text = "Remove selected", Enabled = false };
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    private readonly TabPage reportPage = new("Recovery report") { Padding = new Padding(12) };
    private readonly TextBox fullReport = new() { Dock = DockStyle.Fill, Multiline = true,
        ReadOnly = true, WordWrap = true, ScrollBars = ScrollBars.Vertical, AccessibleName = "Full recovery report" };
    private readonly LinkLabel reportSummary = new() { AutoSize = true };
    private readonly Button viewReport = new() { Text = "View full report", Visible = false };
    private readonly Button copyFullReport = new() { Text = "Copy report" };
    private readonly Button copyReport = new() { Text = "Copy report" };
    private readonly CheckBox matchPsd=new(){Text="Try preserving PSD layer names using artwork matching (experimental)",Checked=true,Enabled=false,AutoSize=true};
    private readonly CheckBox relinkPsd=new(){Text="Attempt PSD texture relinking (experimental)",Checked=false,Enabled=false,AutoSize=true};
    private readonly Button chooseOutput=new(){Text="Choose output folder…",AutoSize=true};
    private readonly Button cancel=new(){Text="Cancel",Enabled=false,AutoSize=true};
    private string outputFolder=AppContext.BaseDirectory;
    private CancellationTokenSource? activeConversion;

    private Plan? plan;private bool busy;
    public MainWindow()
    {
        SuspendLayout();
        // All design sizes are 96-DPI logical pixels. Let WinForms scale once
        // rather than mixing font-scaled controls with fixed-height text rows.
        AutoScaleDimensions=new SizeF(96F,96F);
        AutoScaleMode=AutoScaleMode.Dpi;
        Font=new Font("Segoe UI",10F);
        Text="MOC3 Recovery 1.91";
        ClientSize=new Size(960,870);
        MinimumSize=new Size(780,700);
        StartPosition=FormStartPosition.CenterScreen;
        WindowState=FormWindowState.Maximized;

        var viewport=new Panel(){Dock=DockStyle.Fill,AutoScroll=true};
        var layout=new TableLayoutPanel(){Dock=DockStyle.Fill,Padding=new Padding(24),
            ColumnCount=1,RowCount=14,MinimumSize=new Size(0,890),Margin=Padding.Empty};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        // Text and button rows grow to their preferred heights at every DPI.
        for(int row=0;row<13;row++)layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles[3]=new RowStyle(SizeType.Absolute,80);
        layout.RowStyles[6]=new RowStyle(SizeType.Absolute,80);
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));

        var title=new Label(){Text="Recover an editable Cubism project",
            Font=new Font("Segoe UI",18,FontStyle.Bold),AutoSize=true,
            Margin=new Padding(0,0,0,20)};
        layout.Controls.Add(title,0,0);
        var paths = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 2,
            Margin = Padding.Empty
        };
        paths.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        paths.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void AddPath(Button picker, TextBox path, int row)
        {
            paths.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            picker.Dock = DockStyle.Fill;
            path.Dock = DockStyle.None;
            path.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            path.Margin = new Padding(12, 0, 0, 8);
            paths.Controls.Add(picker, 0, row);
            paths.Controls.Add(path, 1, row);
        }
        AddPath(browse, input, 0);
        AddPath(chooseOutput, destination, 1);
        layout.Controls.Add(paths, 0, 1);
        SetOutputFolder(outputFolder);
        chooseOutput.Click+=(_,_)=>{
            using var picker=new FolderBrowserDialog{InitialDirectory=outputFolder,Description="Choose the folder for recovery outputs",UseDescriptionForTitle=true};
            if(picker.ShowDialog(this)==DialogResult.OK)SetOutputFolder(picker.SelectedPath);
        };
        cancel.Click+=(_,_)=>{
            cancel.Enabled=false;
            activeConversion?.Cancel();
            status.Text="Cancelling recovery…";
        };
        var textureHeading=new Label(){Text="Texture PNGs — atlas order: 0, 1, 2…",
            AutoSize=true,Margin=new Padding(0,0,0,8)};
        layout.Controls.Add(textureHeading,0,2);
        textures.IntegralHeight=false;textures.Margin=new Padding(0,0,0,8);
        layout.Controls.Add(textures,0,3);

        foreach(var button in new[]{browse,chooseOutput,selectTextures,clearTextures,up,down,convert,cancel,openFolder,selectPsd,removePsd,clearPsd,copyReport,viewReport,copyFullReport})
        {
            button.AutoSize=true;button.AutoSizeMode=AutoSizeMode.GrowAndShrink;
            button.Padding=new Padding(12,6,12,6);
            button.Margin=new Padding(0,0,8,8);
        }
        browse.Margin=chooseOutput.Margin=new Padding(0,0,0,8);
        FlowLayoutPanel ButtonRow(params Control[] controls)
        {
            var row=new FlowLayoutPanel(){Dock=DockStyle.Fill,AutoSize=true,
                AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=true,
                Margin=Padding.Empty,Padding=Padding.Empty};
            row.Controls.AddRange(controls);return row;
        }
        var textureButtons=ButtonRow(selectTextures,clearTextures,up,down);
        textureButtons.Margin=new Padding(0,0,0,12);
        layout.Controls.Add(textureButtons, 0, 4);
        layout.Controls.Add(new Label { Text = "PSD artwork (optional)", AutoSize = true,
            Margin = new Padding(0, 0, 0, 8) }, 0, 5);
        psdList.Columns.Add("Filename", 420);
        psdList.ClientSizeChanged += (_, _) => psdList.Columns[0].Width =
            Math.Max(1, psdList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
        layout.Controls.Add(psdList, 0, 6);
        layout.Controls.Add(ButtonRow(selectPsd, removePsd, clearPsd), 0, 7);

        var options = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill,
            ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 0, 0, 8) };
        for (int i = 0; i < 2; i++)
        {
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            options.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }
        options.Controls.Add(matchPsd, 0, 0);
        options.Controls.Add(relinkPsd, 0, 1);
        options.Controls.Add(inferLinks, 1, 0);
        options.Controls.Add(inferDeformerNames, 1, 1);
        layout.Controls.Add(options, 0, 8);
        layout.Controls.Add(ButtonRow(convert, cancel, openFolder), 0, 9);
        var progressRow=new TableLayoutPanel(){Dock=DockStyle.Fill,AutoSize=true,ColumnCount=2,RowCount=1,Margin=new Padding(0,0,0,12)};
        progressRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));progressRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        progress.Height=18;progress.Margin=Padding.Empty;progress.MinimumSize=new Size(0,18);
        progressRow.Controls.Add(progress,0,0);progressRow.Controls.Add(progressPercent,1,0);layout.Controls.Add(progressRow,0,10);
        var reportHeading=new Label(){Text="Recovery report",AutoSize=true,
            Font=new Font("Segoe UI",10,FontStyle.Bold),Margin=new Padding(0,8,0,8)};
        var reportHeader = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true,
            ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        reportHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        reportHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        reportHeader.Controls.Add(reportHeading, 0, 0);
        reportHeader.Controls.Add(ButtonRow(viewReport, copyReport), 1, 0);
        layout.Controls.Add(reportHeader, 0, 11);
        status.Margin = new Padding(0, 0, 0, 8);
        layout.Controls.Add(status, 0, 12);
        report.Margin = Padding.Empty;
        report.MinimumSize = new Size(0, 140);
        layout.Controls.Add(report, 0, 13);
        void CopyReport(object? sender, EventArgs args)
        {
            try { if (report.TextLength > 0) Clipboard.SetText(report.Text); }
            catch (System.Runtime.InteropServices.ExternalException e)
            { MessageBox.Show(this, e.Message, "Unable to copy report"); }
        }
        copyReport.Click += CopyReport;
        copyFullReport.Click += CopyReport;
        viewReport.Click += (_, _) => tabs.SelectedTab = reportPage;
        status.LinkClicked += (_, _) => ShowWarnings();
        reportSummary.LinkClicked += (_, _) => ShowWarnings();
        var recoveryPage = new TabPage("Recovery");
        viewport.Controls.Add(layout);
        recoveryPage.Controls.Add(viewport);
        tabs.TabPages.Add(recoveryPage);
        Controls.Add(tabs);

        var reader = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 2 };
        reader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        reader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        reader.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        reader.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        reportSummary.Anchor = AnchorStyles.Left;
        reader.Controls.Add(reportSummary, 0, 0);
        reader.Controls.Add(copyFullReport, 1, 0);
        fullReport.Font = report.Font;
        reader.Controls.Add(fullReport, 0, 1);
        reader.SetColumnSpan(fullReport, 2);
        reportPage.Controls.Add(reader);
        Disposed += (_, _) => reportPage.Dispose();
        // Constrain wrapping labels to the current width, including after a
        // monitor/DPI change. Long status messages must grow their row.
        void FitLabels()
        {
            int width=Math.Max(1,layout.ClientSize.Width-layout.Padding.Horizontal);
            foreach (CheckBox option in options.Controls)
            {
                int available = Math.Max(1, width / 2 - option.Margin.Horizontal);
                if (option.MaximumSize.Width != available) option.MaximumSize = new Size(available, 0);
            }
            foreach(var label in new[]{title,textureHeading,status})
                if(label.MaximumSize.Width!=width)label.MaximumSize=new Size(width,0);
        }
        layout.SizeChanged+=(_,_)=>FitLabels();
        Shown+=(_,_)=>FitLabels();
        ResumeLayout(true);
        selectPsd.Click+=(_,_)=>{
            using var dialog=new OpenFileDialog(){Title="Add layered PSD files",Multiselect=true,Filter="Photoshop document (*.psd)|*.psd"};
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;
            bool added = false;
            foreach (var path in dialog.FileNames)
            {
                if (psdList.Items.ContainsKey(path)) continue;
                psdList.Items.Add(new ListViewItem(Path.GetFileName(path)) { Name = path, ToolTipText = path });
                added = true;
            }
            if (added) { matchPsd.Checked = true; relinkPsd.Checked = true; }
            UpdatePsdControls();
        };
        matchPsd.CheckedChanged += (_, _) =>
        {
            if (!matchPsd.Checked) relinkPsd.Checked = false;
            UpdatePsdControls();
        };
        removePsd.Click += (_, _) =>
        {
            foreach (var item in psdList.SelectedItems.Cast<ListViewItem>().ToArray()) item.Remove();
            UpdatePsdControls();
        };
        psdList.SelectedIndexChanged += (_, _) => UpdatePsdControls();
        clearPsd.Click += (_, _) => { psdList.Items.Clear(); UpdatePsdControls(); };
        UpdatePsdControls();
        browse.Click+=(_,_)=>ChooseModel();selectTextures.Click+=(_,_)=>ChooseTextures();clearTextures.Click+=(_,_)=>{if(plan!=null){plan.Textures.Clear();RefreshTextures();}};
        up.Click+=(_,_)=>MoveTexture(-1);down.Click+=(_,_)=>MoveTexture(1);convert.Click+=async(_,_)=>await RunConversion();
        openFolder.Click+=(_,_)=>{if(plan!=null)Process.Start(new ProcessStartInfo("explorer.exe"){ArgumentList={Path.GetDirectoryName(plan.Output)!},UseShellExecute=true});};
        FormClosing+=(_,e)=>{if(busy){e.Cancel=true;status.Text="Please wait for conversion to finish before closing.";}};
        report.Text="Choose a .moc3 file. The app saves the same filename with .cmo3 in the selected output folder (the app folder by default).\r\n\r\nThe recovery report below lists missing inputs and features that cannot be restored. The original model and detected companion files are also preserved in a recovery ZIP.\r\n\r\nAn exact original project cannot be recreated from compiled data alone.";
    }
    private void ResetReportTab()
    {
        tabs.TabPages.Remove(reportPage);
        viewReport.Visible = false;
        fullReport.Clear();
        status.LinkArea = new LinkArea(0, 0);
    }

    private void OpenCompletedReport(Plan completed)
    {
        fullReport.Text = report.Text;
        string summary = Path.GetFileName(completed.Output) + " saved   ·   " + completed.CompletionSummary;
        string warningText = "Engine and validation warnings: " + completed.WarningCount;
        var warningLink = completed.WarningCount > 0
            ? new LinkArea(summary.IndexOf(warningText, StringComparison.Ordinal), warningText.Length)
            : new LinkArea(0, 0);
        foreach (var label in new[] { status, reportSummary })
        {
            label.Text = summary;
            label.LinkArea = warningLink;
        }
        tabs.TabPages.Add(reportPage);
        viewReport.Visible = true;
        tabs.SelectedTab = reportPage;
        fullReport.Select(0, 0);
        fullReport.ScrollToCaret();
    }

    private void ShowWarnings()
    {
        tabs.SelectedTab = reportPage;
        int start = fullReport.Text.IndexOf(Environment.NewLine + Plan.WarningHeading + Environment.NewLine,
            StringComparison.Ordinal);
        if (start < 0) return;
        fullReport.Focus();
        fullReport.Select(start + Environment.NewLine.Length, Plan.WarningHeading.Length);
        fullReport.ScrollToCaret();
    }

    private void UpdatePsdControls()
    {
        bool hasFiles = psdList.Items.Count > 0;
        if (!hasFiles) { matchPsd.Checked = true; relinkPsd.Checked = false; }
        clearPsd.Enabled = matchPsd.Enabled = hasFiles && !busy;
        relinkPsd.Enabled = matchPsd.Enabled && matchPsd.Checked;
        removePsd.Enabled = !busy && psdList.SelectedItems.Count > 0;
    }

    private void SetOutputFolder(string folder)
    {
        outputFolder=Path.GetFullPath(folder);
        if(plan!=null)plan.Output=Path.Combine(outputFolder,Path.GetFileNameWithoutExtension(plan.Input)+".cmo3");
        destination.Text=outputFolder;
    }
    private void ChooseModel()
    {
        using var dialog=new OpenFileDialog(){Title="Choose a compiled Live2D model",Filter="Cubism compiled model (*.moc3)|*.moc3|All files (*.*)|*.*"};
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        try
        {
            var next=Recovery.Inspect(dialog.FileName, outputFolder);plan=next;ResetReportTab();psdList.Items.Clear();UpdatePsdControls();input.Text=next.Input;
            progress.Value=0;progressPercent.Text="0%";progress.Visible=false;progressPercent.Visible=false;
            RefreshTextures();convert.Enabled=true;openFolder.Enabled=true;
            report.Text="Ready.\r\n\r\n"+string.Join("\r\n",next.Notes)+"\r\n\r\n"+next.Assets.Count+" source/companion files found for preservation.\r\n\r\nPhysics and parameter folders will be restored from companion files when available. Other runtime settings are preserved in the recovery ZIP.";
            status.Text="Ready to create "+Path.GetFileName(next.Output)+".";
        }
        catch(Exception e){MessageBox.Show(this,e.Message,"Unable to read model",MessageBoxButtons.OK,MessageBoxIcon.Error);}
    }
    private void ChooseTextures()
    {
        if(plan==null)return;
        using var dialog=new OpenFileDialog(){Title="Select texture atlas PNGs (then check their order)",Filter="PNG textures (*.png)|*.png",Multiselect=true,InitialDirectory=Path.GetDirectoryName(plan.Input)};
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        plan.Textures.Clear();plan.Textures.AddRange(dialog.FileNames.OrderBy(Recovery.NaturalKey,StringComparer.OrdinalIgnoreCase));RefreshTextures();
    }
    private void RefreshTextures(){textures.Items.Clear();if(plan!=null)for(int i=0;i<plan.Textures.Count;i++)textures.Items.Add($"{i}: {(File.Exists(plan.Textures[i])?"":"[MISSING] ")}{plan.Textures[i]}");}
    private void MoveTexture(int delta)
    {
        int i=textures.SelectedIndex,j=i+delta;if(plan==null||i<0||j<0||j>=plan.Textures.Count)return;
        (plan.Textures[i],plan.Textures[j])=(plan.Textures[j],plan.Textures[i]);RefreshTextures();textures.SelectedIndex=j;
    }
    private async Task RunConversion()
    {
        if(plan==null||busy)return;
        var existing=plan.Outputs.Where(File.Exists).ToArray();bool replace=false;
        if(existing.Length>0)
        {
            replace=MessageBox.Show(this,"These output files already exist:\r\n\r\n"+string.Join("\r\n",existing)+"\r\n\r\nReplace them? Existing files will be kept as timestamped .bak files.","Replace existing outputs?",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes;
            if(!replace)return;
        }
        ResetReportTab();
        report.Clear();
        var psdPaths = psdList.Items.Cast<ListViewItem>().Select(item => item.Name).ToList();
        plan.PsdInputs.Clear();plan.PsdInputs.AddRange(psdPaths);plan.RelinkPsdTextures=psdPaths.Count>0&&relinkPsd.Checked;relinkPsd.Enabled=false;plan.MatchPsdArtwork=psdPaths.Count>0&&matchPsd.Checked;matchPsd.Enabled=false;
        plan.InferDeformerNames=inferDeformerNames.Checked;inferDeformerNames.Enabled=false;
        plan.InferParameterLinks=inferLinks.Checked;inferLinks.Enabled=false;
        using var cancellation=new CancellationTokenSource();
        activeConversion=cancellation;cancel.Enabled=true;chooseOutput.Enabled=false;
        busy=true;UpdatePsdControls();foreach(var b in new[]{browse,selectTextures,clearTextures,up,down,convert,openFolder,selectPsd,clearPsd})b.Enabled=false;progress.Value=0;progressPercent.Text="0%";progress.Visible=true;progressPercent.Visible=true;
        bool acceptingProgress=true;
        var temp=Path.Combine(Path.GetTempPath(),"MocRecovery-"+Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);var engine=Path.Combine(temp,"moc-recover.exe");
            using(var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("MocRecovery.Engine.exe")??throw new IOException("The embedded recovery engine is missing."))
            using(var file=File.Create(engine))await resource.CopyToAsync(file,cancellation.Token);
            var updates=new Progress<RecoveryProgress>(p=>{if(!acceptingProgress||cancellation.IsCancellationRequested)return;if(p.Percent>=98)cancel.Enabled=false;progress.Value=p.Percent;progressPercent.Text=p.Percent+"%";status.Text=p.Message;});
            var selected=plan;
            report.Text=await Task.Run(()=>Recovery.ConvertAsync(selected,engine,replace,numericProgress:updates,cancellationToken:cancellation.Token));
            acceptingProgress=false;progress.Value=100;progressPercent.Text="100%";
            OpenCompletedReport(selected);
        }
        catch(OperationCanceledException) when(cancellation.IsCancellationRequested)
        {
            acceptingProgress=false;progress.Visible=progressPercent.Visible=false;
            status.Text="Recovery cancelled.";report.Text="Conversion cancelled. Existing output files were preserved.";
        }
        catch(Exception e){acceptingProgress=false;status.Text="Recovery failed.";report.Text="RECOVERY FAILED\r\n\r\n"+e.ToString();MessageBox.Show(this,e.Message,"Recovery failed",MessageBoxButtons.OK,MessageBoxIcon.Error);}
        finally
        {
            activeConversion=null;cancel.Enabled=false;chooseOutput.Enabled=true;
            inferDeformerNames.Enabled=true;inferLinks.Enabled=true;busy=false;acceptingProgress=false;foreach(var b in new[]{browse,selectTextures,clearTextures,up,down,convert,openFolder,selectPsd,clearPsd})b.Enabled=true;
            UpdatePsdControls();
            try{Directory.Delete(temp,true);}catch{}
        }
    }
}
