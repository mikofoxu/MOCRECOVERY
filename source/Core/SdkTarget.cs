using System.Globalization;
using System.Xml.Linq;
namespace MocRecovery;
public static class SdkTarget
{
    public static (string Label,int Target,string Format) FromMocVersion(byte version)=>version switch
    {
        1=>("3.0",3000000,"VERSION_30"),
        2=>("3.3",3030000,"VERSION_33"),
        3=>("4.0",4000000,"VERSION_40"),
        4=>("4.2",4020000,"VERSION_42"),
        5=>("5.0",5000000,"VERSION_50"),
        6=>("5.3",5030000,"VERSION_53"),
        _=>throw new InvalidDataException($"Unsupported MOC3 version byte {version}; cannot choose a reliable SDK target.")
    };
    public static string Apply(XDocument doc,string mocPath)
    {
        using var file=File.OpenRead(mocPath);Span<byte> header=stackalloc byte[6];file.ReadExactly(header);
        if(!header[..4].SequenceEqual("MOC3"u8))throw new InvalidDataException("Invalid MOC3 header when setting SDK target.");
        var version=FromMocVersion(header[4]);
        var model=doc.Descendants("CModelSource").Single(e=>e.HasElements);
        var target=model.Elements("i").SingleOrDefault(e=>(string?)e.Attribute("xs.n")=="targetVersionNo");
        if(target==null){target=new XElement("i",new XAttribute("xs.n","targetVersionNo"));model.Add(target);}
        target.Value=version.Target.ToString(CultureInfo.InvariantCulture);
        // Keep an existing SDK export dialog selection consistent, without synthesizing editor preferences.
        foreach(var setting in model.Descendants("CModelExportSettingDialogData"))
            foreach(var format in setting.Descendants("FormatVersion").Where(e=>(string?)e.Attribute("xs.n")=="version"))format.SetAttributeValue("v",version.Format);
        return version.Label;
    }
}
