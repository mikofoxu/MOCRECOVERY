using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MocRecovery;
public sealed class Plan
{
    public required string Input { get; init; }
    public required string Output { get; init; }
    public string Report => Path.ChangeExtension(Output, ".recovery.txt");
    public string Backup => Path.ChangeExtension(Output, ".recovery.zip");
    public List<string> Textures { get; } = [];
    public HashSet<string> Assets { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? PsdInput { get; set; }
    public bool InferParameterLinks { get; set; } = true;
    public List<string> Notes { get; } = [];
    public string? DisplayInfo { get; set; }
    public string? PhysicsInfo { get; set; }
    public string[] Outputs => [Output, Report, Backup];
}
public static class Recovery
{
    public static Plan Inspect(string input, string? outputDirectory = null)
    {
        input = Path.GetFullPath(input);
        using (var s = File.OpenRead(input))
        {
            Span<byte> magic = stackalloc byte[4];
            if (s.Read(magic) != 4 || !magic.SequenceEqual("MOC3"u8))
                throw new InvalidDataException("The selected file is not a valid MOC3 model.");
        }
        var outputRoot = Path.GetFullPath(outputDirectory ?? AppContext.BaseDirectory);
        var p = new Plan { Input = input, Output = Path.Combine(outputRoot, Path.GetFileNameWithoutExtension(input)+".cmo3") };
        if (string.Equals(input,p.Output,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Select a .moc3 input, not a .cmo3 project.");
        p.Assets.Add(input);
        var dir = Path.GetDirectoryName(input)!;
        var manifest = Path.ChangeExtension(input, ".model3.json");
        bool hasTextureList = false;
        if (File.Exists(manifest))
        {
            p.Assets.Add(manifest);
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllBytes(manifest));
                var refs = doc.RootElement.GetProperty("FileReferences");
                if (refs.TryGetProperty("Moc", out var moc) && !string.Equals(LocalPath(dir,moc.GetString()!),input,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The matching model3.json points to a different MOC3. Select textures manually.");
                if (refs.TryGetProperty("Textures", out var textures))
                {
                    hasTextureList=true;
                    foreach(var t in textures.EnumerateArray())
                    {
                        var path=LocalPath(dir,t.GetString()!);
                        p.Textures.Add(path); // Keep missing slots; never shift texture indices.
                        if(File.Exists(path)) p.Assets.Add(path);
                        else p.Notes.Add("MISSING TEXTURE: " + path);
                    }
                }
                foreach(var prop in refs.EnumerateObject())
                    if(prop.Name != "Moc" && prop.Name != "Textures") CollectReferences(prop.Value,dir,p);
                if(refs.TryGetProperty("Physics",out var ph))
                {var path=LocalPath(dir,ph.GetString()!);if(File.Exists(path))p.PhysicsInfo=path;}
                if(refs.TryGetProperty("DisplayInfo",out var di))
                { var path=LocalPath(dir,di.GetString()!); if(File.Exists(path))p.DisplayInfo=path; }
            }
            catch(Exception e) when(e is JsonException or KeyNotFoundException or InvalidDataException or InvalidOperationException)
            {p.Notes.Add("MANIFEST NOT FULLY READ: "+e.Message);}
        }
        else p.Notes.Add("No matching .model3.json found. Runtime texture order and auxiliary references cannot be confirmed.");
        if(!hasTextureList)
        {
            // Only an unambiguous conventional atlas directory is guessed.
            var candidates=Directory.GetDirectories(dir,Path.GetFileNameWithoutExtension(input)+".*",SearchOption.TopDirectoryOnly)
                .Where(d=>Directory.EnumerateFiles(d,"texture_*.png").Any()).ToArray();
            if(candidates.Length==1)
            {
                p.Textures.AddRange(Directory.GetFiles(candidates[0],"texture_*.png").OrderBy(NaturalKey,StringComparer.OrdinalIgnoreCase));
                p.Notes.Add("Texture order inferred from numbered filenames; verify the order before conversion.");
                foreach(var t in p.Textures)p.Assets.Add(t);
            }
            else p.Notes.Add("No unambiguous texture set found. Choose texture PNGs or use transparent placeholders.");
        }
        // Runtime exports often omit expressions and VTube Studio files from model3.json.
        foreach(var f in Directory.GetFiles(dir,"*.json",SearchOption.TopDirectoryOnly))
        {
            var n=Path.GetFileName(f);var stem=Path.GetFileNameWithoutExtension(input);
            if(n.StartsWith(stem+".",StringComparison.OrdinalIgnoreCase) || n.EndsWith(".exp3.json",StringComparison.OrdinalIgnoreCase) || n=="items_pinned_to_model.json")
                p.Assets.Add(f);
        }
        var cdi=Path.ChangeExtension(input,".cdi3.json");
        if(p.DisplayInfo==null && File.Exists(cdi))p.DisplayInfo=cdi;
        var physics=Path.ChangeExtension(input,".physics3.json");
        if(p.PhysicsInfo==null && File.Exists(physics))p.PhysicsInfo=physics;
        return p;
    }
    private static void CollectReferences(JsonElement e,string dir,Plan p)
    {
        if(e.ValueKind==JsonValueKind.String)
        {
            var s=e.GetString()!;
            // Values at FileReferences leaves are resource filenames (not motion group names).
            var path=LocalPath(dir,s);
            if(File.Exists(path)) p.Assets.Add(path); else p.Notes.Add("MISSING COMPANION: "+s);
        }
        else if(e.ValueKind==JsonValueKind.Array)foreach(var child in e.EnumerateArray())CollectReferences(child,dir,p);
        else if(e.ValueKind==JsonValueKind.Object)foreach(var prop in e.EnumerateObject())
            if(prop.Name is "File" or "Sound" || prop.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)CollectReferences(prop.Value,dir,p);
    }
    public static string LocalPath(string dir,string relative)
    {
        if(Path.IsPathRooted(relative))throw new InvalidDataException("Absolute companion path is not supported: "+relative);
        var full=Path.GetFullPath(Path.Combine(dir,relative.Replace('\\',Path.DirectorySeparatorChar)));
        if(!full.StartsWith(Path.GetFullPath(dir)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Companion path leaves the model folder: "+relative);
        return full;
    }
    public static string NaturalKey(string s)=>Regex.Replace(s,@"\d+",m=>m.Value.PadLeft(12,'0'));

    public static async Task<string> ConvertAsync(Plan plan,string engine,bool overwrite,IProgress<string>? progress=null)
    {
        var conflicts=plan.Outputs.Where(File.Exists).ToArray();
        if(conflicts.Length>0 && !overwrite)throw new IOException("Output already exists: "+string.Join(", ",conflicts));
        var dir=Path.GetDirectoryName(plan.Input)!;
        var outputDir=Path.GetDirectoryName(plan.Output)!;
        Directory.CreateDirectory(outputDir);
        var temp=Path.Combine(outputDir,".moc-recovery-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string stage="Starting recovery";
        void Stage(string message){stage=message;progress?.Report(message);}
        try
        {
            Stage("Reading model and reconstructing the editable rig…");
            var textures=new List<string>();var missing=new List<string>();
            // A valid transparent PNG is used at each missing index, preserving later pages.
            var transparent=Path.Combine(temp,"transparent.png");
            File.WriteAllBytes(transparent,Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNgYGBgAAAABQABpfZFQAAAAABJRU5ErkJggg=="));
            foreach(var t in plan.Textures)
            {
                if(File.Exists(t)){textures.Add(t);plan.Assets.Add(t);}
                else {textures.Add(transparent);missing.Add(t);}
            }
            var staged=Path.Combine(temp,"result.cmo3");var modelJson=Path.Combine(temp,"runtime.model.json");
            var job=Path.Combine(temp,"job.json");
            File.WriteAllText(job,JsonSerializer.Serialize(new { input=plan.Input,output=staged,name=Path.GetFileNameWithoutExtension(plan.Input),textures,displayInfo=plan.DisplayInfo,modelJson }));
            var psi=new ProcessStartInfo(engine){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            psi.ArgumentList.Add(job);
            using var process=Process.Start(psi)??throw new IOException("Could not start the recovery engine.");
            var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();var output=await stdout;var error=await stderr;
            if(process.ExitCode!=0)throw new InvalidDataException("Conversion failed. Existing outputs were not changed.\r\n"+error);
            using var summary=JsonDocument.Parse(output);var s=summary.RootElement;
            Stage("Restoring physics and parameter groups from companion files…");
            var enrichment=ProjectEnricher.Apply(staged,plan);
            using(var f=File.OpenRead(staged)){byte[] magic=new byte[4];f.ReadExactly(magic);if(!magic.AsSpan().SequenceEqual("CAFF"u8))throw new InvalidDataException("Engine did not produce a CMO3 archive.");}
            Stage("Preparing companion motions and checking recovered geometry…");
            var extra=Path.Combine(temp,"editor-import");
            using var recovered=JsonDocument.Parse(File.ReadAllBytes(modelJson));
            var companions=CompanionExporter.Export(plan,recovered.RootElement,extra);
            PsdImportResult? psdResult=null;
            if(plan.PsdInput!=null)
            {
                Stage("Importing optional PSD source artwork… Large layers may take a few minutes.");
                psdResult=PsdImporter.Apply(staged,plan.PsdInput,Path.Combine(extra,"psd-source"));
            }
            Stage("Naming deformers from ArtMeshes…");
            var deformerNames=DeformerNames.NameFromArtMeshes(staged);
            Stage("Validating recovered project…");
            var validation=RecoveryValidator.Validate(staged,Path.Combine(extra,"validation"),plan.PhysicsInfo);
            if(validation.Errors.Count>0)throw new InvalidDataException("Recovery validation failed. Existing outputs were not changed.\r\n"+string.Join("\r\n",validation.Errors));
            var report=new StringBuilder();
            report.AppendLine("MOC3 RECOVERY REPORT — v1.5.0").AppendLine("Input: "+plan.Input).AppendLine("Output: "+plan.Output).AppendLine();
            report.AppendLine("RECOVERED FROM MOC3");
            foreach(var key in new[]{"parameters","deformers","meshes","parts","glues"})report.AppendLine($"{key}: {s.GetProperty(key)}");
            report.AppendLine("Geometry, triangle indices, UVs, clipping masks, keyforms, parameter bindings and deformer hierarchy are reconstructed by the engine.");
            report.AppendLine("Display names restored from CDI: "+s.GetProperty("restoredNames"));
            report.AppendLine("Native physics setups restored: "+enrichment.PhysicsSettings);
            report.AppendLine("Native parameter folders restored (excluding root): "+enrichment.ParameterGroups);
            report.AppendLine("Inferred linked parameter pairs: "+enrichment.LinkedPairs.Count+(plan.InferParameterLinks?"":" (option disabled)"));
            foreach(var pair in enrichment.LinkedPairs)report.AppendLine("INFERRED LINK: "+pair);
            foreach(var note in enrichment.Notes)report.AppendLine(note);
            report.AppendLine().AppendLine("EDITOR IMPORT PACKAGE (inside recovery ZIP: editor-import/)");
            report.AppendLine($"Expression snapshots: {companions.Expressions}; import-ready motions: {companions.Motions}; pose visibility previews: {companions.Poses}.");
            report.AppendLine("Extract the ZIP. Add the recovered CMO3 to an animation scene, right-click its model track, choose Import Motion. Read editor-import/README.txt first.");
            report.AppendLine($"Additional expression fade previews: {companions.FadePreviews}; filtered editor motion copies: {companions.EditorMotions}.");
            report.AppendLine("Expression fade previews approximate an isolated transition against defaults. Runtime stacking/animated-base expressions and automatic pose switching are not recreated. Pose snapshots reset other groups to their first choice.");
            foreach(var note in companions.Notes)report.AppendLine(note);
            report.AppendLine().AppendLine("OPTIONAL PSD");
            if(psdResult==null)report.AppendLine("No PSD selected. Recovery uses MOC3 and available companion files.");
            else
            {
                report.AppendLine($"PSD raster layers: {psdResult.Layers}; source images imported: {psdResult.Imported}; unique name candidates: {psdResult.NameMatches}.");
                report.AppendLine("PSD source images and names are available in the project. Existing mesh texture links remain on the atlas; name candidates are not pixel-verified. Unmatched artwork does not recreate omitted mesh geometry or rigging.");
                foreach(var note in psdResult.Notes)report.AppendLine(note);
            }
            report.AppendLine().AppendLine("VALIDATION");
            report.AppendLine($"Checked {validation.Meshes} meshes, {validation.Keyforms} mesh keyforms and {validation.Samples} local interpolation samples against emitted Editor data. No checked-data mismatches.");
            report.AppendLine($"Checked {validation.Rig?.Deformers} deformers, {validation.Rig?.Keyforms} deformer keyforms and {validation.Rig?.Samples} deformer local samples. Physics setups compared field-by-field with companion: {validation.PhysicsSetupsCompared}.");
            report.AppendLine("Also checked UVs, triangles, clipping references/flags, culling, draw order, opacity, and physics numeric/range/particle references.");
            foreach(var note in validation.Warnings)report.AppendLine("REVIEW: "+note);
            report.AppendLine("Not verified: world-space deformer evaluation, rendered clipping/compositing, physics simulation, or every parameter combination. Import editor-import/validation/parameter-sweep.motion3.json and parameter-pairs.motion3.json (when co-bound pairs exist) to review single and simultaneous movement visually. Pair schedule JSON identifies each segment; at most 64 pairs are included.");
            foreach(var note in deformerNames)report.AppendLine(note);
            int required=s.GetProperty("requiredTextures").GetInt32();
            report.AppendLine($"Texture pages required: {required}; supplied existing PNGs: {plan.Textures.Count(File.Exists)}.");
            report.AppendLine().AppendLine("MISSING OR UNCONFIRMED INPUTS");
            foreach(var note in plan.Notes)report.AppendLine("Initial discovery: "+note);
            foreach(var t in missing)report.AppendLine("MISSING TEXTURE (transparent placeholder): "+t);
            for(int i=plan.Textures.Count;i<required;i++)report.AppendLine($"MISSING TEXTURE PAGE {i}: transparent placeholder; artwork cannot be recovered from MOC3.");
            foreach(var warning in s.GetProperty("warnings").EnumerateArray())report.AppendLine(warning.GetString());
            foreach(var suffix in new[]{".physics3.json",".exp3.json",".motion3.json",".pose3.json",".userdata3.json"})
                if(!plan.Assets.Any(f=>f.EndsWith(suffix,StringComparison.OrdinalIgnoreCase)))
                    report.AppendLine("NOT PROVIDED: "+suffix+" — no matching companion data found; it may never have existed for this model.");
            if(plan.DisplayInfo==null)report.AppendLine("No CDI display-info file: friendly parameter/part names were unavailable; runtime IDs are used.");
            report.AppendLine().AppendLine("PRESERVED AS FILES, NOT RESTORED AS NATIVE EDITOR SETTINGS");
            report.AppendLine("The recovery ZIP preserves the original MOC3, supplied textures, detected companion files, and the recovered runtime JSON. It can be opened with Windows Explorer.");
            report.AppendLine("Original companions remain preserved. Expressions and pose groups additionally produce importable approximations; existing motions are checked and prepared for Editor import. Native controllers and animation scenes are not rebuilt. Physics setups and parameter groups are now imported when their companion data is valid; see counts and warnings above.");
            report.AppendLine("Unreferenced expression JSON files in this folder are included; they may belong to another model if models share a folder.");
            report.AppendLine().AppendLine("NOT RECOVERABLE FROM THESE INPUTS / NOT VERIFIED");
            report.AppendLine((psdResult==null?"Original PSD layers were not supplied. ":"Supplied PSD artwork is imported separately; it cannot restore omitted rigging. ")+"Removed mesh geometry, editor-only controls, original GUIDs, deformer labels, authoring history and comments are not fully represented by a MOC3. Exact missing rig object names/counts cannot be determined without the original CMO3.");
            report.AppendLine("Rig texture links use the recovered atlas pages. Optional PSD source images are imported separately without automatic texture relinking. Advanced/version-specific features may not round-trip exactly.");
            report.AppendLine("A valid archive does not prove visual or animated equivalence. Open in Cubism Editor and check the rig before use.");
            Stage("Preserving companion files and writing the report…");
            var zip=Path.Combine(temp,"assets.zip");
            using(var archive=ZipFile.Open(zip,ZipArchiveMode.Create))
            {
                foreach(var asset in plan.Assets.Order(StringComparer.OrdinalIgnoreCase))
                {
                    if(!File.Exists(asset)){report.AppendLine("FILE DISAPPEARED BEFORE BACKUP: "+asset);continue;}
                    var relative=Path.GetRelativePath(dir,asset);
                    if(relative.StartsWith("..") || Path.IsPathRooted(relative))relative="selected-textures/"+Guid.NewGuid().ToString("N")+"-"+Path.GetFileName(asset);
                    archive.CreateEntryFromFile(asset,"original/"+relative.Replace('\\','/'),CompressionLevel.Optimal);
                    report.AppendLine("Preserved: "+asset);
                }
                if(plan.PsdInput!=null)archive.CreateEntryFromFile(plan.PsdInput,"original/source-artwork/"+Path.GetFileName(plan.PsdInput),CompressionLevel.Optimal);
                foreach(var generated in Directory.GetFiles(extra,"*",SearchOption.AllDirectories))
                    archive.CreateEntryFromFile(generated,"editor-import/"+Path.GetRelativePath(extra,generated).Replace('\\','/'),CompressionLevel.Optimal);
                archive.CreateEntryFromFile(modelJson,"recovered/runtime.model.json",CompressionLevel.Optimal);
            }
            var reportPath=Path.Combine(temp,"report.txt");File.WriteAllText(reportPath,report.ToString(),Encoding.UTF8);
            // Commit only after conversion, ZIP and report all succeeded. Existing files are
            // retained as uniquely named .bak files when replacement was explicitly approved.
            var pairs=new[]{(staged,plan.Output),(zip,plan.Backup),(reportPath,plan.Report)};
            var installed=new List<string>();var backups=new List<(string,string)>();
            try
            {
                foreach(var (source,dest) in pairs)
                {
                    if(File.Exists(dest))
                    {
                        if(!overwrite)throw new IOException("An output appeared during conversion: "+dest);
                        var backup=dest+"."+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6]+".bak";
                        File.Move(dest,backup);backups.Add((backup,dest));
                    }
                    File.Move(source,dest);installed.Add(dest);
                }
            }
            catch
            {
                foreach(var f in installed)File.Delete(f);
                foreach(var (b,d) in backups)File.Move(b,d);
                throw;
            }
            if(backups.Count>0)report.AppendLine("Previous output files were retained as timestamped .bak files in the same folder.");
            return report.ToString();
        }
        catch(OutOfMemoryException e){throw new IOException("Memory allocation failed during: "+stage+"\r\nClose other memory-heavy apps and retry. If a PSD was selected, No PSD can be used temporarily. Existing outputs were not replaced.",e);}
        finally {try{Directory.Delete(temp,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
}
