using System.Xml.Linq;
namespace MocRecovery;

public static class PhysicsModelMetadata
{
    // Editor files can store a parameter ID inline or share it with random-pose settings.
    public static Dictionary<string,string> ParameterIds(XDocument doc)
    {
        var shared = doc.Root!.Element("shared")!.Elements("CParameterId")
            .ToDictionary(e => (string)e.Attribute("xs.id")!, e => (string)e.Attribute("idstr")!);
        return doc.Descendants("CParameterSource").Where(e => e.HasElements).ToDictionary(
            e => (string)e.Element("CParameterGuid")!.Attribute("xs.ref")!,
            e => {
                var id = e.Element("CParameterId")!;
                return (string?)id.Attribute("idstr") ?? shared[(string)id.Attribute("xs.ref")!];
            });
    }

    internal static void Apply(XDocument doc, Dictionary<string,byte[]> entries)
    {
        var model = doc.Root!.Element("main")!.Element("CModelSource")!;
        if (model.Element("CPhysicsSettingsSourceSet")?.Descendants("CPhysicsSettingsSource").Any() != true) return;
        var shared = doc.Root.Element("shared")!;
        int next = doc.Descendants().Attributes("xs.id").Max(a => int.Parse(a.Value[1..])) + 1;
        int index = doc.Descendants().Attributes("xs.idx").Select(a => (int)a).DefaultIfEmpty(-1).Max() + 1;
        string Share(XElement field)
        {
            if (field.Attribute("xs.ref") is XAttribute existing) return existing.Value;
            string id = "#" + next++;
            var value = new XElement(field);
            value.Attribute("xs.n")?.Remove();
            value.SetAttributeValue("xs.id", id);
            value.SetAttributeValue("xs.idx", index++);
            shared.Add(value);
            var reference = new XElement(field.Name, new XAttribute("xs.ref", id));
            if (field.Attribute("xs.n") is XAttribute name) reference.Add(new XAttribute(name));
            field.ReplaceWith(reference);
            return id;
        }
        static XElement Field(string tag, string name, params object[] contents) =>
            new(tag, new XAttribute("xs.n", name), contents);
        static XElement List(string tag, string name, params XElement[] values) =>
            Field(tag, name, new XAttribute("count", values.Length), values);
        var set = model.Element("CParameterGroupSet")!;
        var groups = set.Descendants("CParameterGroup").Where(e => e.HasElements).ToArray();
        var root = groups.Single(g => g.Elements("null").Any(e => (string?)e.Attribute("xs.n") == "parentGroupGuid"));
        var pose = new XElement("CRandomPoseSetting");
        void AddPoseEntries(string name, IEnumerable<XElement> owners, string keyTag, string valueTag, string flag)
        {
            var keys = owners.Select(owner => new XElement(keyTag,
                new XAttribute("xs.ref", Share(owner.Element(keyTag)!)))).ToArray();
            pose.Add(List("array_list", name + ".keys", keys));
            pose.Add(List("array_list", name + ".values", keys.Select(_ => new XElement(valueTag, Field("b", flag, "true"))).ToArray()));
        }
        AddPoseEntries("parameters", model.Descendants("CParameterSource").Where(e => e.HasElements), "CParameterId", "CRandomPoseParamData", "isEnable");
        AddPoseEntries("groups", groups, "CParameterGroupId", "CRandomPoseGroupData", "isExpand");
        set.AddAfterSelf(Field("CParameterGroup", "rootParameterGroup", new XAttribute("xs.ref", Share(root))));
        var info = model.Element("CModelInfo")!;
        info.Add(Field("CEffectParameterGroups", "_effectParameterGroups",
            List("hash_map", "_parameterGroups", new XElement("entry",
                Field("EffectParameterType", "key", new XAttribute("v","EYE_BLINK")), List("carray_list","value")))));
        var options = Field("hash_map","modelOptions",new XAttribute("count",0),new XAttribute("keyType","string"));
        info.AddAfterSelf(options);
        XElement anchor = options;
        foreach (int size in new[] {64, 32, 16})
        {
            string filename = $"recovery-icon-{size}.png";
            entries.Add(filename, PsdReader.EncodePng(size, size, new byte[size * size * 4]));
            var icon = Field("CImageIcon", "_icon"+size, Field("CWritableImage", "image",
                new XAttribute("width",size), new XAttribute("height",size),new XAttribute("type","INT_ARGB"),
                Field("file","image",new XAttribute("path",filename))));
            anchor.AddAfterSelf(icon);
            anchor = icon;
        }
        model.Element("ModelViewerSetting")!.AddAfterSelf(Field("CGuidesSetting","guides",List("carray_list","guidesModeling")));
        model.Add(Field("CArtPathBrushSetting","artPathBrushesSetting",List("carray_list","brushes")),
            Field("CRandomPoseSettingManager","randomPoseSetting",List("array_list","_settings",pose),Field("i","currentIndex",0)));
        var instructions = doc.Nodes().OfType<XProcessingInstruction>().ToList();
        instructions.Single(p => p.Target=="version" && p.Data.StartsWith("CModelSource:")).Data="CModelSource:14";
        foreach (var path in new[]{
            "com.live2d.cubism.doc.model.CEffectParameterGroups","com.live2d.cubism.doc.model.EffectParameterType",
            "com.live2d.cubism.doc.model.drawable.artPath.Line.CArtPathBrushSetting","com.live2d.cubism.doc.modeling.ui.guide.CGuidesSetting",
            "com.live2d.graphics.CWritableImage","com.live2d.type.CImageIcon",
            "com.live2d.cubism.doc.model.randomPose.CRandomPoseSettingManager","com.live2d.cubism.doc.model.randomPose.CRandomPoseSetting",
            "com.live2d.cubism.doc.model.randomPose.CRandomPoseSetting$CRandomPoseGroupData","com.live2d.cubism.doc.model.randomPose.CRandomPoseSetting$CRandomPoseParamData"})
            if (!instructions.Any(p => p.Target=="import" && p.Data==path)) doc.Root.AddBeforeSelf(new XProcessingInstruction("import",path));
    }
}
