using System.Text;

namespace AnimeTracker.Desktop;

public sealed class LinuxStartup
{
    readonly string executable,configHome;
    readonly string? dataDirectory;
    public string EntryPath=>Path.Combine(configHome,"autostart","local.animetracker.AnimeTracker.desktop");
    public LinuxStartup(string executable,string configHome,string? dataDirectory=null)
    {
        this.executable=Path.GetFullPath(executable);this.configHome=Path.GetFullPath(configHome);this.dataDirectory=dataDirectory;
    }
    static string Value(string value)
    {
        if(value.IndexOfAny(['\r','\n','\0'])>=0)throw new ArgumentException("Путь содержит неподдерживаемые символы.");
        return value.Replace("\\","\\\\");
    }
    public static string QuoteArgument(string value)
    {
        Value(value);
        var quoted=value.Replace("\\","\\\\").Replace("\"","\\\"").Replace("`","\\`").Replace("$","\\$").Replace("%","%%");
        return "\""+quoted.Replace("\\","\\\\")+"\"";
    }
    public string Entry(bool startup)
    {
        if(executable.Contains('='))throw new ArgumentException("Для ярлыка переместите приложение в папку без знака = в пути.");
        string args=startup?" --startup":"";
        if(dataDirectory!=null)args+=" --data-dir "+QuoteArgument(Path.GetFullPath(dataDirectory));
        // GIO probes argv[0] before expanding %% to %. A fixed env launcher
        // keeps executable paths containing a literal percent valid, without a shell.
        return "[Desktop Entry]\nType=Application\nVersion=1.0\nName=Anime Tracker\nComment=Anime library and episode tracker\nExec=/usr/bin/env -- "+QuoteArgument(executable)+args+"\nTryExec="+Value(executable)+"\nIcon="+Value(Path.Combine(Path.GetDirectoryName(executable)!,"app.png"))+"\nTerminal=false\nCategories=AudioVideo;\nStartupWMClass=AnimeTracker\n";
    }
    public bool Enabled=>File.Exists(EntryPath)&&File.ReadAllText(EntryPath)==Entry(true);
    public void Set(bool enabled)
    {
        if(!enabled){if(File.Exists(EntryPath))File.Delete(EntryPath);return;}
        if(!File.Exists(executable))throw new FileNotFoundException("Не найден исполняемый файл приложения.",executable);
        Write(EntryPath,Entry(true));
    }
    public string InstallMenu(string dataHome)
    {
        var path=Path.Combine(dataHome,"applications","local.animetracker.AnimeTracker.desktop");Write(path,Entry(false));return path;
    }
    static void Write(string path,string content)
    {
        var folder=Path.GetDirectoryName(path)!;
        if(!Directory.Exists(folder)){if(OperatingSystem.IsLinux())Directory.CreateDirectory(folder,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);else Directory.CreateDirectory(folder);}
        var temp=path+".tmp-"+Guid.NewGuid().ToString("N");
        try{File.WriteAllText(temp,content,new UTF8Encoding(false));File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
}

public sealed class DataFolderLock : IDisposable
{
    readonly FileStream stream;
    DataFolderLock(FileStream stream){this.stream=stream;}
    public static DataFolderLock? TryAcquire(string root)
    {
        Directory.CreateDirectory(root);
        try{return new DataFolderLock(new FileStream(Path.Combine(root,".instance.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None));}
        catch(IOException e) when((e.HResult&0xffff) is 11 or 32 or 33 || (OperatingSystem.IsMacOS()&&(e.HResult&0xffff)==35)){return null;}
    }
    public static void RequestOpen(string root)=>File.WriteAllText(Path.Combine(root,".open-request"),Guid.NewGuid().ToString("N"));
    public void Dispose()=>stream.Dispose(); // Keep the inode: deleting it permits two owners.
}
