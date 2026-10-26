using AnimeTracker;
using AnimeTracker.Desktop;
using System.Xml.Linq;

static class PortTests
{
 public static void Run(string root)
 {
  var source=Path.Combine(root,"windows-data");Directory.CreateDirectory(source);
  var anime=new Anime{Title="Мой тайтл 日本語",Status="planned",Watched=4,Total=12,Score=7,Notes="Строка 1\nСтрока 2",MalId=21,CatalogId=21};var library=new Library();library.Items.Add(anime);library.Save(Path.Combine(source,"library.xml"));
  var choices=new WatchChoices();choices.Add(anime);choices.Hide(anime,null);choices.Save(Path.Combine(source,"watch-choices.xml"));new WidgetOptions{Pinned=anime.Id,Style=2,Enabled=false}.Save(Path.Combine(source,"widget-settings.xml"));
  var target=Path.Combine(root,"mac-data");var store=new AppStore(target);var backup=store.ImportWindowsFolder(source);if(!Directory.Exists(backup)||!File.Exists(Path.Combine(source,"library.xml")))throw new Exception("Migration deleted source/backup");var migrated=store.Library.Items.Single();if(migrated.Id!=anime.Id||migrated.Notes!=anime.Notes||migrated.Watched!=4||migrated.Score!=7||migrated.Status!="planned"||!store.Choices.IsHidden(migrated)||store.Choices.Active(store.Library).Single()!=migrated||store.Widget.Enabled)throw new Exception("Windows folder migration lost data");
  store.SetWidget(anime.Id,1);if(store.Widget.Enabled)throw new Exception("Style change enabled disabled widget");var restarted=new AppStore(target);if(restarted.Widget.Enabled||restarted.Widget.Style!=1)throw new Exception("Disabled widget restart");restarted.SetWidget(anime.Id,2,true);if(!new AppStore(target).Widget.Enabled)throw new Exception("Re-enable widget");
  var legacy=Path.Combine(root,"legacy-widget.xml");new XElement("widget",new XAttribute("style",0)).Save(legacy);if(!WidgetOptions.Load(legacy).Enabled)throw new Exception("Legacy widget default");
  store.Update(migrated,migrated.Title,4,12,migrated.Status,8,"Новая заметка");if(migrated.Watched!=4||migrated.Status!="planned"||migrated.Id!=anime.Id)throw new Exception("Card update lost migrated progress");
  var export=Path.Combine(root,"backup.zip");store.ExportBackup(export);if(!File.Exists(export)||new FileInfo(export).Length==0)throw new Exception("Backup export");
  var broken=Path.Combine(root,"broken-data");Directory.CreateDirectory(broken);File.WriteAllText(Path.Combine(broken,"library.xml"),"broken");var failed=new AppStore(broken);bool rejected=false;try{failed.AddManual("No overwrite");}catch(InvalidOperationException){rejected=true;}if(!rejected||File.ReadAllText(Path.Combine(broken,"library.xml"))!="broken")throw new Exception("Corrupt library protection");
  Console.WriteLine("PASS: Windows-to-Mac migration, IDs, notes/status/progress/queue, original source and backup, disabled widget persistence, corruption protection");
 }
}
