using AnimeTracker;
using AnimeTracker.Desktop;
using System.Diagnostics;
using System.IO.Compression;

static class LinuxPlatformTests
{
    static void Check(bool value,string text){if(!value)throw new Exception("Linux: "+text);}
    static TrackerFiles Split(string root,string name)=>new(Path.Combine(root,name,"data"),Path.Combine(root,name,"config"),Path.Combine(root,name,"cache"));
    public static async Task Run(string root)
    {
        root=Path.Combine(root,"linux");Directory.CreateDirectory(root);
        var fallback=LinuxPaths.Resolve(_=>"relative/not-valid",root);
        Check(fallback.Root==Path.GetFullPath(Path.Combine(root,".local/share/AnimeTracker"))&&fallback.ConfigRoot==Path.GetFullPath(Path.Combine(root,".config/AnimeTracker")),"relative XDG value not ignored");
        var configured=LinuxPaths.Resolve(key=>Path.Combine(root,key),root);Check(configured.Root==Path.Combine(root,"XDG_DATA_HOME","AnimeTracker")&&configured.CacheRoot==Path.Combine(root,"XDG_CACHE_HOME","AnimeTracker"),"custom XDG roots");
        var files=Split(root,"split");LinuxPaths.Prepare(files);var store=new TrackerService(files,new ShikimoriCatalogSource(),()=>DateTime.UtcNow,false);
        Check(!store.Widget.Enabled&&!File.Exists(files.Widget),"default widget or eager preference write");
        var anime=store.AddManual("Раздельные пути 日本語");store.Update(anime,anime.Title,3,12,"watching",7,"Заметка");store.ChangeChoices(next=>{next.Add(anime);next.Hide(anime,null);});store.SetWidget(anime.Id,2,false);store.View.Search="Японский";store.SaveView();
        Directory.CreateDirectory(Path.Combine(files.CacheRoot,"covers"));File.WriteAllBytes(files.PathFor("covers/21.png"),[1,2,3]);
        CalendarNotes.Save(new(),files.Schedule);
        Check(File.Exists(files.Library)&&File.Exists(Path.Combine(files.ConfigRoot,"widget-settings.xml"))&&File.Exists(Path.Combine(files.CacheRoot,"schedule.xml"))&&!File.Exists(Path.Combine(files.Root,"widget-settings.xml")),"split file routing");
        var restart=new TrackerService(files,new ShikimoriCatalogSource(),()=>DateTime.UtcNow,false);Check(restart.Library.Items.Single().Notes=="Заметка"&&restart.Choices.Hidden.Count==1&&restart.View.Search=="Японский"&&!restart.Widget.Enabled,"split restart");
        var backup=Path.Combine(root,"split.zip");store.ExportBackup(backup);using(var zip=ZipFile.OpenRead(backup)){foreach(var name in new[]{"library.xml","watch-choices.xml","widget-settings.xml","view-settings.xml","covers/21.png","schedule.xml"})Check(zip.GetEntry(name)!=null,"backup missing "+name);Check(zip.GetEntry("config/widget-settings.xml")==null,"backup is not portable flat layout");}
        bool rejected=false;try{store.ExportBackup(Path.Combine(files.ConfigRoot,"bad.zip"));}catch(ArgumentException){rejected=true;}Check(rejected,"backup inside config accepted");
        var source=new TrackerService(Path.Combine(root,"source"));var incoming=source.AddManual("Windows fixture");source.Update(incoming,incoming.Title,8,13,"on_hold",9,"Не потерять");source.SetWidget(incoming.Id,1,false);
        var imported=store.ImportWindowsFolder(source.Root);Check(File.Exists(Path.Combine(imported,"library.xml"))&&File.Exists(source.LibraryPath)&&store.Library.Items.Single().Id==incoming.Id&&store.Widget.Style==1&&store.Library.Items[0].Watched==8,"split migration/backup");
        var brokenFiles=Split(root,"rollback");var broken=new TrackerService(brokenFiles,new ShikimoriCatalogSource(),()=>DateTime.UtcNow,false);var kept=broken.AddManual("Keep me");string original=File.ReadAllText(broken.LibraryPath);Directory.CreateDirectory(brokenFiles.Widget);
        bool failed=false;try{broken.ImportWindowsFolder(source.Root);}catch(IOException){failed=true;}catch(UnauthorizedAccessException){failed=true;}
        Check(failed&&File.ReadAllText(broken.LibraryPath)==original&&broken.Library.Items.Single()==kept,"multi-root rollback lost library");
        var migratedFiles=Split(root,"migrate");LinuxPaths.Prepare(migratedFiles);var migration=LinuxPaths.MigrateLegacy(migratedFiles,source.Root);
        Check(migration!=null&&Library.Load(migratedFiles.Library).Items.Single().Id==incoming.Id&&File.Exists(source.LibraryPath)&&LinuxPaths.MigrateLegacy(migratedFiles,source.Root)==null,"legacy migration not safe/idempotent");
        using(var lease=DataFolderLock.TryAcquire(files.Root)){Check(lease!=null&&DataFolderLock.TryAcquire(files.Root)==null,"second owner acquired data");DataFolderLock.RequestOpen(files.Root);Check(!files.ExistingFiles().ContainsKey(".instance.lock")&&!files.ExistingFiles().ContainsKey(".open-request"),"runtime lock/request entered backup");}
        using(var next=DataFolderLock.TryAcquire(files.Root))Check(next!=null,"owner lock not released");
        var flat=new TrackerService(Path.Combine(root,"locked-flat"));flat.AddManual("Before import");using(var lease=DataFolderLock.TryAcquire(flat.Root)){flat.ImportWindowsFolder(source.Root);Check(DataFolderLock.TryAcquire(flat.Root)==null,"folder import moved the lock inode");}
        var exe=Path.Combine(root,"Fake App");File.WriteAllText(exe,"fixture");var startup=new LinuxStartup(exe,Path.Combine(root,"xdg-config"),files.Root);Check(!startup.Enabled,"unexpected startup");startup.Set(true);startup.Set(true);Check(startup.Enabled&&Directory.GetFiles(Path.GetDirectoryName(startup.EntryPath)!).Length==1&&File.ReadAllText(startup.EntryPath).Contains("--data-dir"),"startup registration/args/idempotence");startup.Set(false);Check(!File.Exists(startup.EntryPath),"startup disable");
        if(OperatingSystem.IsLinux())await NativeDesktopEntry(root);
        Console.WriteLine("PASS: Linux XDG roots/fallback, split XML/backup/import/rollback, legacy migration, widget default, instance lock, desktop autostart and quoting");
    }
    static async Task NativeDesktopEntry(string root)
    {
        if(!OperatingSystem.IsLinux())return;
        string probe=Path.Combine(root,"Приложение $ ` % ' "+(char)34+(char)92+" test");
        // Literal shell program; all test paths are passed via argv/environment.
        File.WriteAllText(probe,"#!/bin/sh\nprintf '%s\\n' \"$@\" > \"$ANIMETRACKER_PROBE_OUTPUT\"\n");
        File.SetUnixFileMode(probe,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        string directory=Path.Combine(root,"данные $ ` % ' "+(char)34+(char)92+" test");
        var entry=new LinuxStartup(probe,Path.Combine(root,"quote-config"),directory).InstallMenu(Path.Combine(root,"quote-data"));
        using(var validate=Process.Start(new ProcessStartInfo("desktop-file-validate"){ArgumentList={entry},UseShellExecute=false})!){await validate.WaitForExitAsync();Check(validate.ExitCode==0,"desktop-file-validate failed");}
        var output=Path.Combine(root,"argv.txt");var start=new ProcessStartInfo("gio"){ArgumentList={"launch",entry},UseShellExecute=false};start.Environment["ANIMETRACKER_PROBE_OUTPUT"]=output;
        using(var launch=Process.Start(start)!){await launch.WaitForExitAsync();Check(launch.ExitCode==0,"GIO rejected quoted Exec");}
        for(int i=0;i<200&&!File.Exists(output);i++)await Task.Delay(10);
        Check(File.Exists(output)&&File.ReadAllLines(output).SequenceEqual(new[]{"--data-dir",directory}),"GIO changed argv for spaces/Unicode/quotes/dollar/percent/backtick");
    }
}
