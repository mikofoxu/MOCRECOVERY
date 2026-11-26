using System.Xml.Linq;

namespace MocRecovery;

public sealed record PhysicsRow(string Group, string Id, string Item, string Parameter, string Axis,
    string Weight, string Scale, string Vertex, string Reflect, string Details);

public sealed record PhysicsReport(string Summary, IReadOnlyList<PhysicsRow> Rows)
{
    internal static PhysicsReport Read(XDocument project, string? companion, IEnumerable<string> notes)
    {
        var set = project.Root?.Element("main")?.Element("CModelSource")?.Element("CPhysicsSettingsSourceSet");
        var groups = set?.Descendants("CPhysicsSettingsSource").Where(e => e.HasElements).ToArray() ?? [];
        var rows = new List<PhysicsRow>();
        var ids = groups.Length > 0 ? PhysicsModelMetadata.ParameterIds(project) : new Dictionary<string,string>();
        static XElement? Field(XElement? item, string name) =>
            item?.Elements().FirstOrDefault(e => (string?)e.Attribute("xs.n") == name);
        static string Value(XElement? item, string name) => Field(item,name)?.Value ?? "";

        foreach (var group in groups)
        {
            string id = (string?)group.Element("CPhysicsSettingId")?.Attribute("idstr") ?? "";
            string name = Value(group,"name");
            foreach (string axis in new[] { "Position", "Angle" })
            {
                string prefix = "normalized" + axis;
                string details = $"Min {Value(group,prefix+"ValueMin")}   ·   Default {Value(group,prefix+"DefaultValue")}   ·   Max {Value(group,prefix+"ValueMax")}";
                rows.Add(new(name,id,"Normalization","",axis,"","","","",details));
            }
            foreach (var item in group.Descendants().Where(e => e.Name == "CPhysicsInput" || e.Name == "CPhysicsOutput"))
            {
                bool output = item.Name == "CPhysicsOutput";
                string guid = (string?)item.Element("CParameterGuid")?.Attribute("xs.ref") ?? "";
                string axis = (string?)Field(item,"type")?.Attribute("v") switch
                {
                    "SRC_TO_X" => "X", "SRC_TO_Y" => "Y", "SRC_TO_G_ANGLE" => "Angle", var other => other ?? ""
                };
                string scale = axis == "Angle" ? Value(item,"angleScale") :
                    Value(Field(item,"translationScale"),axis.ToLowerInvariant());
                rows.Add(new(name,id,output ? "Output" : "Input",ids.GetValueOrDefault(guid,guid),
                    axis,Value(item,"weight"),output ? scale : "",Value(item,"vertexIndex"),Value(item,"isReverse"),""));
            }
            int index = 0;
            foreach (var particle in group.Descendants("CPhysicsVertex"))
            {
                var position = Field(particle,"position");
                string details = $"X {Value(position,"x")}   ·   Y {Value(position,"y")}   ·   Mobility {Value(particle,"mobility")}   ·   Delay {Value(particle,"delay")}   ·   Acceleration {Value(particle,"acceleration")}   ·   Radius {Value(particle,"radius")}";
                rows.Add(new(name,id,"Particle","","","","",(index++).ToString(),"",details));
            }
        }
        string summary = $"{groups.Length} physics groups applied.";
        string fps = Value(set,"settingFPS");
        if (fps.Length > 0) summary += $"   ·   {fps} FPS";
        if (companion != null) summary += $"   ·   {Path.GetFileName(companion)}";
        else if (groups.Length == 0) summary += " No physics companion file was found.";
        foreach (string note in notes.Where(n => n.StartsWith("PHYSICS ",StringComparison.Ordinal) ||
            n.StartsWith("Custom global gravity/wind",StringComparison.Ordinal)))
            rows.Add(new("","","Import note","","","","","","",note));
        return new(summary,rows);
    }
}
