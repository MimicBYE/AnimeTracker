using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
#if MODERN_DOTNET
using System.Formats.Tar;
#endif

namespace AnimeTracker {
public static class UpdatePackage {
 public static string Hash(string file){using(var hash=SHA256.Create())using(var input=File.OpenRead(file))return BitConverter.ToString(hash.ComputeHash(input)).Replace("-","").ToLowerInvariant();}
 public static string Within(string root,string name){
  if(String.IsNullOrWhiteSpace(name)||name.Contains('\\')||name.Contains(':')||name.StartsWith("/",StringComparison.Ordinal)||name.IndexOf('\0')>=0)throw new InvalidDataException("Опасный путь в обновлении.");
  var parts=name.TrimEnd('/').Split('/');if(parts.Any(p=>p.Length==0||p=="."||p==".."||p.EndsWith(".",StringComparison.Ordinal)||p.EndsWith(" ",StringComparison.Ordinal)))throw new InvalidDataException("Опасный путь в обновлении.");
  var path=Path.GetFullPath(Path.Combine(root,String.Join(Path.DirectorySeparatorChar.ToString(),parts)));if(!Inside(root,path))throw new InvalidDataException("Путь вышел за папку обновления.");return path;
 }
 public static bool Inside(string root,string path){return Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,Path.DirectorySeparatorChar=='\\'?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal);}
 public static void NoLinks(string path){for(var item=Path.GetFullPath(path);!String.IsNullOrEmpty(item);item=Path.GetDirectoryName(item))if((File.Exists(item)||Directory.Exists(item))&&(File.GetAttributes(item)&FileAttributes.ReparsePoint)!=0){
#if MODERN_DOTNET
  // macOS exposes its system temporary folder through this fixed OS-owned alias.
  if(OperatingSystem.IsMacOS()&&item=="/var"&&Directory.ResolveLinkTarget(item,true).FullName=="/private/var")continue;
#endif
  throw new IOException("Символьные ссылки в путях обновления не поддерживаются.");}}
 public static void Copy(string from,string to){Directory.CreateDirectory(Path.GetDirectoryName(to));File.Copy(from,to,true);
#if MODERN_DOTNET
  if(!OperatingSystem.IsWindows())File.SetUnixFileMode(to,File.GetUnixFileMode(from));
#endif
 }
 public static string[] Files(string root){NoLinks(root);var output=new List<string>();foreach(var file in Directory.GetFiles(root)){NoLinks(file);output.Add(file.Substring(root.Length+1));}foreach(var dir in Directory.GetDirectories(root)){NoLinks(dir);foreach(var name in Files(dir))output.Add(Path.GetFileName(dir)+Path.DirectorySeparatorChar+name);}return output.ToArray();}
 public static string Extract(string archive,string destination,string platform,string version,CancellationToken token){
  if(Directory.Exists(destination))throw new IOException("Папка распаковки уже существует.");Directory.CreateDirectory(destination);var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;int count=0;
  Action<string,long,int,bool,Stream> add=(name,size,mode,directory,input)=>{
   token.ThrowIfCancellationRequested();var target=Within(destination,name);if(!names.Add(name.TrimEnd('/'))||++count>20000||size<0||(total+=size)>1024L*1024*1024)throw new InvalidDataException("Некорректный или слишком большой архив обновления.");
   int kind=mode&0xf000;if(kind!=0&&kind!=0x8000&&kind!=0x4000)throw new InvalidDataException("Ссылки и специальные файлы в обновлении запрещены.");
   if(directory){Directory.CreateDirectory(target);return;}Directory.CreateDirectory(Path.GetDirectoryName(target));
   using(var output=new FileStream(target,FileMode.CreateNew)){var buffer=new byte[81920];long copied=0;int read;while(input!=null&&(read=input.Read(buffer,0,buffer.Length))>0){token.ThrowIfCancellationRequested();copied+=read;if(copied>size)throw new InvalidDataException("Некорректная длина файла.");output.Write(buffer,0,read);}if(copied!=size)throw new InvalidDataException("Неполный файл в архиве.");}
#if MODERN_DOTNET
   if(!OperatingSystem.IsWindows())File.SetUnixFileMode(target,(UnixFileMode)(mode==0?0x1a4:mode&0x1ff));
#endif
  };
  if(platform=="linux-x64"){
#if MODERN_DOTNET
   using(var source=File.OpenRead(archive))using(var gzip=new GZipStream(source,CompressionMode.Decompress))using(var tar=new TarReader(gzip)){
    TarEntry entry;while((entry=tar.GetNextEntry())!=null){bool directory=entry.EntryType==TarEntryType.Directory;if(!directory&&entry.EntryType!=TarEntryType.RegularFile&&entry.EntryType!=TarEntryType.V7RegularFile)throw new InvalidDataException("Неподдерживаемая TAR-запись.");add(entry.Name,entry.Length,((int)entry.Mode)|(directory?0x4000:0x8000),directory,entry.DataStream);}
   }
#else
   throw new PlatformNotSupportedException();
#endif
  }else using(var zip=ZipFile.OpenRead(archive))foreach(var entry in zip.Entries){bool directory=entry.FullName.EndsWith("/",StringComparison.Ordinal);using(var input=entry.Open())add(entry.FullName,entry.Length,(entry.ExternalAttributes>>16)&0xffff,directory,input);}
  var root=platform=="macos-arm64"?Path.Combine(destination,"Anime Tracker.app"):Path.Combine(destination,"AnimeTracker-"+version+"-"+platform);Validate(root,platform,version);return root;
 }
 public static string Executable(string root,string platform){return platform=="macos-arm64"?Path.Combine(root,"Contents","MacOS","AnimeTracker"):Path.Combine(root,platform=="windows-x64"?"AnimeTracker.exe":"AnimeTracker");}
 public static string BuildPath(string root,string platform){return platform=="macos-arm64"?Path.Combine(root,"Contents","Resources","BUILD-INFO.json"):Path.Combine(root,"BUILD-INFO.json");}
 public static void Validate(string root,string platform,string version){
  NoLinks(root);var build=UpdateBuild.Read(BuildPath(root,platform));if(build.updateProtocol!=1||build.packageVersion!=version||build.channel!=(UpdateVersion.Parse(version).Development>0?"dev":"stable"))throw new InvalidDataException("Пакет не поддерживает протокол обновления или его версия не совпадает с релизом.");
  var exe=Executable(root,platform);using(var input=File.OpenRead(exe))using(var reader=new BinaryReader(input)){
   if(platform=="windows-x64"){if(reader.ReadUInt16()!=0x5a4d||input.Length<128)throw new InvalidDataException("Ожидался Windows EXE.");input.Position=0x3c;long offset=reader.ReadUInt32();if(offset>input.Length-6)throw new InvalidDataException("Некорректный PE.");input.Position=offset;if(reader.ReadUInt32()!=0x4550||reader.ReadUInt16()!=0x8664)throw new InvalidDataException("Ожидался EXE x64.");}
   else if(platform=="macos-arm64"){if(reader.ReadUInt32()!=0xfeedfacf||reader.ReadUInt32()!=0x100000c)throw new InvalidDataException("Ожидался macOS ARM64.");}
   else if(platform=="linux-x64"){if(reader.ReadUInt32()!=0x464c457f||reader.ReadByte()!=2||reader.ReadByte()!=1)throw new InvalidDataException("Ожидался ELF x64.");input.Position=18;if(reader.ReadUInt16()!=62)throw new InvalidDataException("Ожидался Linux x64.");}
   else throw new InvalidDataException("Неизвестная платформа.");
  }
  if(platform=="windows-x64"&&!File.Exists(exe+".config"))throw new InvalidDataException("Отсутствует конфигурация EXE.");
  foreach(var name in Files(root)){var parts=name.Split(Path.DirectorySeparatorChar);if(parts.Any(p=>new[]{"data","covers","imports",".git",".tools"}.Contains(p))||new[]{"library.xml","appearance-settings.xml","update-settings.xml","widget-settings.xml"}.Contains(Path.GetFileName(name)))throw new InvalidDataException("В обновлении обнаружены личные данные.");}
 }
}
}
