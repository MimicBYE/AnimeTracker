using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace AnimeTracker {
public static class AppearanceTests {
 static void Check(bool value,string name){if(!value)throw new Exception("Appearance: "+name);}
 static double Linear(double value){value/=255;return value<=0.04045?value/12.92:Math.Pow((value+0.055)/1.055,2.4);}
 static double Luminance(string color){return 0.2126*Linear(Convert.ToInt32(color.Substring(1,2),16))+0.7152*Linear(Convert.ToInt32(color.Substring(3,2),16))+0.0722*Linear(Convert.ToInt32(color.Substring(5,2),16));}
 public static void Run(string root){
  root=Path.Combine(root,"appearance");var source=Path.Combine(root,"source");var service=new TrackerService(source);Check(service.Appearance.Mode=="light","legacy default");
  var anime=service.AddManual("Синтетический тайтл 日本語");service.Update(anime,anime.Title,7,24,"watching",9,"Личная заметка");var bytes=File.ReadAllBytes(service.LibraryPath);
  int changes=0,notices=0;service.Changed+=()=>changes++;service.AppearanceChanged+=()=>{notices++;Check(AppearanceOptions.Load(service.Files.Appearance).Mode==service.Appearance.Mode,"event before persistence");};
  service.SetAppearance("dark");Check(service.Appearance.Dark&&notices==1&&changes==0,"theme notified library / live state");
  Check(new TrackerService(source).Appearance.Dark&&File.ReadAllBytes(service.LibraryPath).SequenceEqual(bytes),"restart or personal fields");
  Directory.CreateDirectory(service.Files.Appearance+".tmp");bool failed=false;try{service.SetAppearance("light");}catch(IOException){failed=true;}catch(UnauthorizedAccessException){failed=true;}
  Check(failed&&service.Appearance.Dark&&notices==1&&new TrackerService(source).Appearance.Dark,"failed save published changes");Directory.Delete(service.Files.Appearance+".tmp");
  service.SetAppearance("light");Check(!service.Appearance.Dark&&AppearanceOptions.Load(service.Files.Appearance+".bak").Dark,"backup");
  foreach(var xml in new[]{"<wrong mode='light'/>","<appearance mode='unknown'/>","<appearance mode='light'><wrong/></appearance>","<!DOCTYPE appearance [<!ENTITY x SYSTEM 'file:///never'>]><appearance mode='light'>&x;</appearance>","<appearance"}){
   File.WriteAllText(service.Files.Appearance,xml);Check(AppearanceOptions.Load(service.Files.Appearance).Dark,"invalid settings did not use backup");
  }
  File.Delete(service.Files.Appearance+".bak");Check(!AppearanceOptions.Load(service.Files.Appearance).Dark,"invalid settings default");
  failed=false;try{service.SetAppearance("system");}catch(ArgumentException){failed=true;}Check(failed,"invalid mode accepted");service.SetAppearance("dark");
  var split=new TrackerService(new TrackerFiles(Path.Combine(root,"xdg-data"),Path.Combine(root,"xdg-config"),Path.Combine(root,"xdg-cache")),new ShikimoriCatalogSource(),()=>DateTime.UtcNow);
  split.ImportWindowsFolder(source);Check(split.Appearance.Dark&&File.Exists(split.Files.Appearance)&&!File.Exists(Path.Combine(split.Root,"appearance-settings.xml")),"XDG migration routing");
  var backup=Path.Combine(root,"../appearance-backup.zip");split.ExportBackup(backup);using(var zip=ZipFile.OpenRead(backup))Check(zip.GetEntry("appearance-settings.xml")!=null,"backup omitted settings");
  var flat=new TrackerService(Path.Combine(root,"../flat-theme"));flat.ImportWindowsFolder(source);Check(flat.Appearance.Dark,"flat migration omitted theme");
  flat.ImportWindowsFolder(split.Root); // A flat source without config correctly defaults to light.
  Check(!flat.Appearance.Dark,"missing theme migration default");
  foreach(bool dark in new[]{false,true})foreach(var foreground in new[]{"#47361D","#725932"})foreach(var background in new[]{"#F8EECC","#FFF9E1","#FFF8DF"}){
   double a=Luminance(PaperPalette.Resolve(foreground,dark)),b=Luminance(PaperPalette.Resolve(background,dark));Check((Math.Max(a,b)+0.05)/(Math.Min(a,b)+0.05)>=4.5,"text contrast "+foreground+" / "+background+" dark="+dark);
  }
  foreach(bool dark in new[]{false,true}){double a=Luminance(PaperPalette.Resolve("#241B0B",dark)),b=Luminance(PaperPalette.Resolve("#DDB44A",dark));Check((b+0.05)/(a+0.05)>=4.5,"primary button contrast");}
  Check(File.ReadAllBytes(service.LibraryPath).SequenceEqual(bytes),"theme operation changed personal data");
 }
}
}
