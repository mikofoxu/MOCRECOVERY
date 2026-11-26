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
    private readonly TextBox destination=new(){ReadOnly=true,Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,BackColor=SystemColors.Control,Text="Output: choose a model first."};
    private readonly ListBox textures=new(){Dock=DockStyle.Fill,HorizontalScrollbar=true};
    private readonly TextBox report=new(){Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=true,Dock=DockStyle.Fill,Font=new Font("Consolas",10),BackColor=Color.FromArgb(245,247,250)};
    private readonly Button browse=new(){Text="Choose .moc3…",AutoSize=true};
    private readonly Button selectTextures=new(){Text="Choose textures…",AutoSize=true};
    private readonly Button clearTextures=new(){Text="No textures",AutoSize=true};
    private readonly Button up=new(){Text="Move up",AutoSize=true};
    private readonly Button down=new(){Text="Move down",AutoSize=true};
    private readonly Button convert=new(){Text="Create .cmo3",AutoSize=true,Enabled=false};
    private readonly Button openFolder=new(){Text="Open folder",AutoSize=true,Enabled=false};
    private readonly Label status=new(){Text="Choose a compiled model to begin.",AutoSize=true};
    private readonly ProgressBar progress=new(){Dock=DockStyle.Fill,Visible=false,Style=ProgressBarStyle.Continuous};
    private readonly Label progressPercent=new(){Text="0%",AutoSize=true,Visible=false,Anchor=AnchorStyles.Left,Margin=new Padding(10,0,0,0)};
    private readonly CheckBox inferLinks=new(){Text="Rebuild linked parameter grids (inferred)",Checked=true,AutoSize=true};
    private readonly Button selectPsd=new(){Text="Choose optional PSD…",AutoSize=true};
    private readonly Button clearPsd=new(){Text="No PSD",AutoSize=true};
    private readonly Label psdStatus=new(){Text="PSD: none (optional)",AutoSize=true,MaximumSize=new Size(340,0),Margin=new Padding(0,8,0,8)};
    private readonly CheckBox matchPsd=new(){Text="Try preserving PSD layer names using artwork matching (experimental)",Checked=true,Enabled=false,AutoSize=true};
    private readonly CheckBox relinkPsd=new(){Text="Attempt PSD texture relinking (Warning: Experimental)",Checked=false,Enabled=false,AutoSize=true};
    private string? psdPath;
    private Plan? plan;private bool busy;
    public MainWindow()
    {
        SuspendLayout();
        // All design sizes are 96-DPI logical pixels. Let WinForms scale once
        // rather than mixing font-scaled controls with fixed-height text rows.
        AutoScaleDimensions=new SizeF(96F,96F);
        AutoScaleMode=AutoScaleMode.Dpi;
        Font=new Font("Segoe UI",10F);
        Text="MOC3 Recovery 1.6.2";
        ClientSize=new Size(960,870);
        MinimumSize=new Size(780,700);
        StartPosition=FormStartPosition.CenterScreen;
        WindowState=FormWindowState.Maximized;

        var viewport=new Panel(){Dock=DockStyle.Fill,AutoScroll=true};
        var layout=new TableLayoutPanel(){Dock=DockStyle.Fill,Padding=new Padding(24),
            ColumnCount=1,RowCount=15,MinimumSize=new Size(0,820),Margin=Padding.Empty};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        // Text and button rows grow to their preferred heights at every DPI.
        for(int row=0;row<14;row++)layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles[4]=new RowStyle(SizeType.Absolute,112);
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));

        var title=new Label(){Text="Recover an editable Cubism project",
            Font=new Font("Segoe UI",18,FontStyle.Bold),AutoSize=true,
            Margin=new Padding(0,0,0,20)};
        layout.Controls.Add(title,0,0);
        var fileRow=new TableLayoutPanel(){Dock=DockStyle.Fill,AutoSize=true,
            AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=2,RowCount=1,
            Margin=new Padding(0,0,0,12)};
        fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fileRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        input.Dock=DockStyle.None;input.Anchor=AnchorStyles.Left|AnchorStyles.Right;
        input.Margin=new Padding(0,0,12,0);
        fileRow.Controls.Add(input,0,0);fileRow.Controls.Add(browse,1,0);
        layout.Controls.Add(fileRow,0,1);
        destination.Margin=new Padding(0,0,0,22);
        layout.Controls.Add(destination,0,2);
        var textureHeading=new Label(){Text="Texture PNGs — atlas order: 0, 1, 2…",
            AutoSize=true,Margin=new Padding(0,0,0,8)};
        layout.Controls.Add(textureHeading,0,3);
        textures.IntegralHeight=false;textures.Margin=new Padding(0,0,0,8);
        layout.Controls.Add(textures,0,4);

        foreach(var button in new[]{browse,selectTextures,clearTextures,up,down,convert,openFolder,selectPsd,clearPsd})
        {
            button.AutoSize=true;button.AutoSizeMode=AutoSizeMode.GrowAndShrink;
            button.Padding=new Padding(12,6,12,6);
            button.Margin=new Padding(0,0,8,8);
        }
        browse.Margin=Padding.Empty;
        FlowLayoutPanel ButtonRow(params Control[] controls)
        {
            var row=new FlowLayoutPanel(){Dock=DockStyle.Fill,AutoSize=true,
                AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=true,
                Margin=Padding.Empty,Padding=Padding.Empty};
            row.Controls.AddRange(controls);return row;
        }
        var textureButtons=ButtonRow(selectTextures,clearTextures,up,down);
        textureButtons.Margin=new Padding(0,0,0,12);
        layout.Controls.Add(textureButtons,0,5);
        layout.Controls.Add(ButtonRow(convert,openFolder),0,6);
        status.Margin=new Padding(0,8,0,8);layout.Controls.Add(status,0,7);
        var progressRow=new TableLayoutPanel(){Dock=DockStyle.Fill,AutoSize=true,ColumnCount=2,RowCount=1,Margin=new Padding(0,0,0,12)};
        progressRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));progressRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        progress.Height=18;progress.Margin=Padding.Empty;progress.MinimumSize=new Size(0,18);
        progressRow.Controls.Add(progress,0,0);progressRow.Controls.Add(progressPercent,1,0);layout.Controls.Add(progressRow,0,8);
        var reportHeading=new Label(){Text="Recovery report",AutoSize=true,
            Font=new Font("Segoe UI",10,FontStyle.Bold),Margin=new Padding(0,8,0,8)};
        inferLinks.Margin=new Padding(0,8,0,8);layout.Controls.Add(inferLinks,0,9);
        layout.Controls.Add(ButtonRow(selectPsd,clearPsd,psdStatus),0,10);
        matchPsd.Margin=new Padding(0,4,0,10);layout.Controls.Add(matchPsd,0,11);
        relinkPsd.Margin=new Padding(0,0,0,10);layout.Controls.Add(relinkPsd,0,12);
        layout.Controls.Add(reportHeading,0,13);
        report.Margin=Padding.Empty;report.MinimumSize=new Size(0,140);
        layout.Controls.Add(report,0,14);
        viewport.Controls.Add(layout);Controls.Add(viewport);
        // Constrain wrapping labels to the current width, including after a
        // monitor/DPI change. Long status messages must grow their row.
        void FitLabels()
        {
            int width=Math.Max(1,layout.ClientSize.Width-layout.Padding.Horizontal);
            matchPsd.MaximumSize=new Size(width,0);relinkPsd.MaximumSize=new Size(width,0);
            foreach(var label in new[]{title,textureHeading,status})
                if(label.MaximumSize.Width!=width)label.MaximumSize=new Size(width,0);
        }
        layout.SizeChanged+=(_,_)=>FitLabels();
        Shown+=(_,_)=>FitLabels();
        ResumeLayout(true);
        selectPsd.Click+=(_,_)=>{
            using var dialog=new OpenFileDialog(){Title="Choose optional original layered PSD",Filter="Photoshop document (*.psd)|*.psd"};
            if(dialog.ShowDialog(this)==DialogResult.OK){psdPath=dialog.FileName;psdStatus.Text="PSD: "+Path.GetFileName(psdPath);matchPsd.Checked=true;matchPsd.Enabled=true;relinkPsd.Checked=false;relinkPsd.Enabled=true;}
        };
        matchPsd.CheckedChanged+=(_,_)=>{if(!matchPsd.Checked)relinkPsd.Checked=false;relinkPsd.Enabled=!busy&&psdPath!=null&&matchPsd.Checked;};
        clearPsd.Click+=(_,_)=>{psdPath=null;psdStatus.Text="PSD: none (optional)";matchPsd.Checked=true;matchPsd.Enabled=false;relinkPsd.Checked=false;relinkPsd.Enabled=false;};
        browse.Click+=(_,_)=>ChooseModel();selectTextures.Click+=(_,_)=>ChooseTextures();clearTextures.Click+=(_,_)=>{if(plan!=null){plan.Textures.Clear();RefreshTextures();}};
        up.Click+=(_,_)=>MoveTexture(-1);down.Click+=(_,_)=>MoveTexture(1);convert.Click+=async(_,_)=>await RunConversion();
        openFolder.Click+=(_,_)=>{if(plan!=null)Process.Start(new ProcessStartInfo("explorer.exe"){ArgumentList={Path.GetDirectoryName(plan.Output)!},UseShellExecute=true});};
        FormClosing+=(_,e)=>{if(busy){e.Cancel=true;status.Text="Please wait for conversion to finish before closing.";}};
        report.Text="Choose a .moc3 file. The app saves the same filename with .cmo3 beside MocRecovery.exe.\r\n\r\nThe recovery report below lists missing inputs and features that cannot be restored. The original model and detected companion files are also preserved in a recovery ZIP.\r\n\r\nAn exact original project cannot be recreated from compiled data alone.";
    }
    private void ChooseModel()
    {
        using var dialog=new OpenFileDialog(){Title="Choose a compiled Live2D model",Filter="Cubism compiled model (*.moc3)|*.moc3|All files (*.*)|*.*"};
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        try
        {
            var next=Recovery.Inspect(dialog.FileName, AppContext.BaseDirectory);plan=next;psdPath=null;psdStatus.Text="PSD: none (optional)";matchPsd.Checked=true;matchPsd.Enabled=false;relinkPsd.Checked=false;relinkPsd.Enabled=false;input.Text=next.Input;
            progress.Value=0;progressPercent.Text="0%";progress.Visible=false;progressPercent.Visible=false;
            destination.Text="Output: "+next.Output;RefreshTextures();convert.Enabled=true;openFolder.Enabled=true;
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
        plan.PsdInput=psdPath;plan.RelinkPsdTextures=psdPath!=null&&relinkPsd.Checked;relinkPsd.Enabled=false;plan.MatchPsdArtwork=psdPath!=null&&matchPsd.Checked;matchPsd.Enabled=false;
        plan.InferParameterLinks=inferLinks.Checked;inferLinks.Enabled=false;
        busy=true;foreach(var b in new[]{browse,selectTextures,clearTextures,up,down,convert,openFolder,selectPsd,clearPsd})b.Enabled=false;progress.Value=0;progressPercent.Text="0%";progress.Visible=true;progressPercent.Visible=true;
        bool acceptingProgress=true;
        var temp=Path.Combine(Path.GetTempPath(),"MocRecovery-"+Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);var engine=Path.Combine(temp,"moc-recover.exe");
            using(var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("MocRecovery.Engine.exe")??throw new IOException("The embedded recovery engine is missing."))
            using(var file=File.Create(engine))await resource.CopyToAsync(file);
            var updates=new Progress<RecoveryProgress>(p=>{if(!acceptingProgress)return;progress.Value=p.Percent;progressPercent.Text=p.Percent+"%";status.Text=p.Message;});
            var selected=plan;
            report.Text=await Task.Run(()=>Recovery.ConvertAsync(selected,engine,replace,numericProgress:updates));
            acceptingProgress=false;progress.Value=100;progressPercent.Text="100%";
            status.Text="Saved "+Path.GetFileName(plan.Output)+" — review the report below.";
        }
        catch(Exception e){acceptingProgress=false;status.Text="Recovery failed.";report.Text="RECOVERY FAILED\r\n\r\n"+e.ToString();MessageBox.Show(this,e.Message,"Recovery failed",MessageBoxButtons.OK,MessageBoxIcon.Error);}
        finally
        {
            inferLinks.Enabled=true;matchPsd.Enabled=psdPath!=null;relinkPsd.Enabled=psdPath!=null&&matchPsd.Checked;busy=false;acceptingProgress=false;foreach(var b in new[]{browse,selectTextures,clearTextures,up,down,convert,openFolder,selectPsd,clearPsd})b.Enabled=true;
            try{Directory.Delete(temp,true);}catch{}
        }
    }
}
