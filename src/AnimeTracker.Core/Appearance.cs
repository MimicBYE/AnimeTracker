using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace AnimeTracker {
public sealed class AppearanceOptions {
 public string Mode="light";
 public bool Dark {get{return Mode=="dark";}}
 public static AppearanceOptions Load(string path) {
  foreach(var file in new[]{path,path+".bak"})try {
   if(!File.Exists(file))continue;
   using(var reader=XmlReader.Create(file,new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=4096})){
    var root=XDocument.Load(reader).Root;var mode=root==null?null:(string)root.Attribute("mode");
    if(root!=null&&root.Name=="appearance"&&!root.HasElements&&String.IsNullOrWhiteSpace(root.Value)&&(mode=="light"||mode=="dark"))return new AppearanceOptions {Mode=mode};
   }
  }catch(IOException){}catch(UnauthorizedAccessException){}catch(XmlException){}
  return new AppearanceOptions();
 }
 public void Save(string path) {
  if(Mode!="light"&&Mode!="dark")throw new ArgumentException("Неизвестная тема приложения.");
  Directory.CreateDirectory(Path.GetDirectoryName(path));var temp=path+".tmp";
  try{new XElement("appearance",new XAttribute("mode",Mode)).Save(temp);if(File.Exists(path))File.Replace(temp,path,path+".bak");else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}
 }
}
// Legacy light shades remain unchanged; both hosts use these shared neutral dark tokens.
// New UI colors must be registered here, so a light-only island cannot go unnoticed.
public static class PaperPalette {
 static readonly Dictionary<string,string> Colors=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase) {
  {"#47361D","#E8E9EC"},{"#725932","#B8BDC6"},{"#896F3E","#B8BDC6"},
  {"#B9862C","#D7AD55"},{"#DDB44A","#D7AD55"},{"#CCA039","#E3BB68"},{"#BC902D","#C69A43"},
  {"#241B0B","#241B0B"},{"#000000","#000000"},
  {"#F8EECC","#181A1D"},{"#F5E5A6","#181A1D"},{"#F9E7A5","#1E2125"},
  {"#FFF9E1","#24272B"},{"#FFF2BA","#24272B"},{"#FFFAF0","#24272B"},{"#FFFFFF","#24272B"},
  {"#FFF8DF","#121417"},{"#EAD490","#30343A"},{"#E3C977","#3B4048"},
  {"#EFDA8D","#30343A"},{"#E8CF79","#3D424A"},{"#E4C879","#3D424A"},
  {"#F1E0A3","#30343A"},{"#F4E4AE","#30343A"},{"#F4EBCD","#1D2024"},
  {"#F5E1A4","#3D424A"},{"#EBD798","#30343A"},{"#E8D59D","#30343A"},
  {"#D6C18B","#4A505A"},{"#C8A65C","#707884"},{"#CFB06F","#4A505A"},{"#D0B46F","#4A505A"},{"#E1CF9D","#4A505A"},
  {"#F1E5BE","#17191C"},{"#D3B66E","#707884"},{"#BE9845","#9299A3"},{"#A57B2D","#D7AD55"},{"#C4A052","#707884"},
  {"#9B7732","#B8BDC6"},{"#735825","#B8BDC6"},{"#BE9F57","#707884"},
  {"#B22222","#FFA59C"},{"#FA8072","#FFA59C"},{"#A0522D","#E9B684"},{"#8B4513","#D7AD55"}
 };
 public static IEnumerable<string> LightColors {get{return Colors.Keys;}}
 public static string Resolve(string light,bool dark) {
  string value;if(!Colors.TryGetValue(light,out value))throw new ArgumentException("Цвет отсутствует в палитре: "+light);
  return dark?value:light;
 }
 public static string Key(string light) {return "Paper_"+light.Substring(1).ToUpperInvariant();}
}
}
