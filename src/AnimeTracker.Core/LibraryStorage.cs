using System;
using System.IO;
using System.Linq;

namespace AnimeTracker {
public sealed class LibraryOpenResult {
 public Library Library=new Library();
 public bool ReadOnly;
 public string Message="";
}

public static class LibraryStorage {
 // Load stays strict and never changes files (including import/preview callers).
 // Only application startup explicitly opts into restoring a validated backup.
 public static LibraryOpenResult Open(string path) {
  var result=new LibraryOpenResult(); Exception failure;
  try {
   if(!File.Exists(path) && File.Exists(path+".bak")) throw new FileNotFoundException("Основной файл отсутствует.");
   result.Library=Library.Load(path); return result;
  } catch(Exception e) { if(!StorageError(e))throw; failure=e; }
  string temporary=path+".recovery-"+Guid.NewGuid().ToString("N"), preserved="";
  try {
   if(!File.Exists(path+".bak")) throw new FileNotFoundException("Резервная копия .bak отсутствует.");
   File.Copy(path+".bak",temporary); // Validate exactly the bytes that will be restored.
   result.Library=Library.Load(temporary);
   if(File.Exists(path)) {
    preserved=path+".corrupt-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+"-"+Guid.NewGuid().ToString("N").Substring(0,6);
    File.Replace(temporary,path,preserved);
   } else File.Move(temporary,path);
   result.Message="Восстановлена предыдущая резервная копия: "+path+".bak. Проверьте последние изменения."+(preserved.Length>0?" Исходный повреждённый файл сохранён: "+preserved:"");
  } catch(Exception e) {
   if(!StorageError(e))throw;
   result.ReadOnly=true;
   result.Message="Не удалось восстановить библиотеку. Запись отключена; исходные файлы сохранены. "+path+"\n"+failure.Message+"\n"+e.Message;
  } finally { try { if(File.Exists(temporary))File.Delete(temporary); } catch(IOException){} catch(UnauthorizedAccessException){} }
  return result;
 }
 static bool StorageError(Exception e) { return e is IOException || e is InvalidDataException || e is UnauthorizedAccessException || e is System.Xml.XmlException || e is FormatException || e is OverflowException || e is ArgumentException; }

 public static void SaveItem(Library library,Anime current,Anime edited,string path) {
  if(current!=null && (!library.Items.Contains(current) || edited.Id!=current.Id)) throw new InvalidOperationException("Запись больше не находится в библиотеке.");
  if(current==null && library.Items.Any(a=>a.Id==edited.Id)) throw new InvalidOperationException("Такой id уже есть в библиотеке.");
  var items=library.Items.Select(a=>a==current?edited:a).ToList(); if(current==null)items.Add(edited);
  new Library {Left=library.Left,Top=library.Top,Items=items}.Save(path);
  if(current==null)library.Items.Add(edited); else current.CopyFrom(edited);
 }
 public static bool Increment(Library library,Anime anime,string path) {
  if(anime.Completed)return false;
  var next=anime.Copy();next.Increment();if(next.Watched==anime.Watched)return false;
  SaveItem(library,anime,next,path);return true;
 }
 public static void RestoreProgress(Library library,Anime anime,Anime before,string path) {
  var next=anime.Copy();next.Watched=before.Watched;next.Status=before.Status;next.Updated=before.Updated;
  SaveItem(library,anime,next,path);
 }
 public static void Remove(Library library,Anime anime,string path) {
  if(!library.Items.Contains(anime))throw new InvalidOperationException("Запись больше не находится в библиотеке.");
  new Library {Left=library.Left,Top=library.Top,Items=library.Items.Where(a=>a!=anime).ToList()}.Save(path);
  library.Items.Remove(anime);
 }
 public static void Insert(Library library,Anime anime,int index,string path) {
  if(library.Items.Any(a=>a.Id==anime.Id))throw new InvalidOperationException("Такой id уже есть в библиотеке.");
  var items=library.Items.ToList();index=Math.Max(0,Math.Min(index,items.Count));items.Insert(index,anime);
  new Library {Left=library.Left,Top=library.Top,Items=items}.Save(path);library.Items.Insert(index,anime);
 }
}
}
