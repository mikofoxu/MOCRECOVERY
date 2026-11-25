use std::{env,fs};
use moc2cmo::{Decompiler,Texture,moc3::Moc3Model};
use serde_json::{Value,json};
fn esc(s:&str)->String {s.replace('&',"&amp;").replace('<',"&lt;").replace('>',"&gt;").replace('"',"&quot;").replace('\'',"&apos;")}
fn rename(xml:String, tag:&str, idtag:&str, field:&str, names:&Value)->(String,usize){
 let mut out=String::new();let mut rest=xml.as_str();let mut count=0;
 let start=format!("<{tag}");let end=format!("</{tag}>");
 while let Some(i)=rest.find(&start){
  out.push_str(&rest[..i]); rest=&rest[i..];
  let Some(header_end)=rest.find('>') else {break};
  let boundary=rest.as_bytes().get(start.len()).copied();
  if !matches!(boundary, Some(b' ')|Some(b'>')) || rest[..header_end].ends_with('/') {
   out.push_str(&rest[..header_end+1]); rest=&rest[header_end+1..];continue;
  }
  let Some(j)=rest.find(&end) else {break};
  let len=j+end.len();let mut block=rest[..len].to_string();
  if let Some(arr)=names.as_array(){for n in arr {
   if let (Some(id),Some(name))=(n["Id"].as_str(),n["Name"].as_str()) {
    if name.is_empty(){continue;}
    if block.contains(&format!("<{idtag} xs.n=\"id\" idstr=\"{}\"",esc(id))) {
     let before=format!("<s xs.n=\"{field}\">{}<",esc(id));
     let after=format!("<s xs.n=\"{field}\">{}<",esc(name));
     if block.contains(&before){block=block.replacen(&before,&after,1);count+=1;}
    }
   }
  }}
  out.push_str(&block);rest=&rest[len..];
 }
 out.push_str(rest);(out,count)
}
fn main()->Result<(),Box<dyn std::error::Error>>{
 let args:Vec<String>=env::args().collect();
 if args.len()!=2 {return Err("usage: moc-recover job.json".into());}
 let job:Value=serde_json::from_slice(&fs::read(&args[1])?)?;
 let input=job["input"].as_str().ok_or("missing input")?;
 let output=job["output"].as_str().ok_or("missing output")?;
 let bytes=fs::read(input)?;let model=Moc3Model::parse(&bytes)?;
 let mut d=Decompiler::new();d.set_model_name(job["name"].as_str().unwrap_or("Recovered Model"));
 for t in job["textures"].as_array().ok_or("missing texture list")? {
  d.push_texture(Texture::from_png(fs::read(t.as_str().ok_or("invalid texture")?)?)?);
 }
 let mut p=d.decompile_project(&bytes)?;
 let mut names=0;let mut warnings=Vec::<String>::new();
 if let Some(path)=job["displayInfo"].as_str(){
  match fs::read(path).ok().and_then(|b|serde_json::from_slice::<Value>(&b).ok()) {
   Some(info)=>{
    let xml=String::from_utf8(p.main_xml().to_vec())?;
    let (xml,n)=rename(xml,"CParameterSource","CParameterId","name",&info["Parameters"]);
    // The tag argument above must be the exact element name.
    let (xml,n2)=rename(xml,"CPartSource","CPartId","localName",&info["Parts"]);
    names=n+n2;p.set_main_xml(xml.into_bytes());
   },None=>warnings.push("Display-info JSON could not be parsed; original bytes are preserved in the recovery ZIP.".into())
  }
 }
 p.validate_xml()?;p.write_to(output)?;
 let data=serde_json::to_vec_pretty(&model)?;
 fs::write(job["modelJson"].as_str().ok_or("missing modelJson")?,data)?;
 let required=model.art_meshes().iter().map(|m|m.texture_index()+1).max().unwrap_or(0);
 println!("{}",json!({"parameters":model.parameters().len(),"deformers":model.deformers().len(),"meshes":model.art_meshes().len(),"parts":model.parts().len(),"glues":model.glues().len(),"requiredTextures":required,"restoredNames":names,"warnings":warnings}));
 Ok(())
}
