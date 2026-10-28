using AnimeTracker;

namespace AnimeTracker.Desktop;

public static class LinuxPaths
{
    public static string Base(string variable,string fallback,Func<string,string?> environment,string home)
    {
        var value=environment(variable);
        return !string.IsNullOrWhiteSpace(value)&&Path.IsPathFullyQualified(value)?Path.GetFullPath(value):Path.Combine(home,fallback);
    }
    public static TrackerFiles Resolve(Func<string,string?> environment,string home)=>new(
        Path.Combine(Base("XDG_DATA_HOME",".local/share",environment,home),"AnimeTracker"),
        Path.Combine(Base("XDG_CONFIG_HOME",".config",environment,home),"AnimeTracker"),
        Path.Combine(Base("XDG_CACHE_HOME",".cache",environment,home),"AnimeTracker"));
    public static TrackerFiles Default()=>Resolve(Environment.GetEnvironmentVariable,Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    public static string ConfigHome=>Base("XDG_CONFIG_HOME",".config",Environment.GetEnvironmentVariable,Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    public static string StateHome=>Path.Combine(Base("XDG_STATE_HOME",".local/state",Environment.GetEnvironmentVariable,Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),"AnimeTracker");
    public static void Prepare(TrackerFiles files)
    {
        foreach(var root in files.Roots)if(!Directory.Exists(root))
        {
            if(OperatingSystem.IsLinux())Directory.CreateDirectory(root,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
            else Directory.CreateDirectory(root);
        }
    }
    public static string? MigrateLegacy(TrackerFiles files,string legacy)
    {
        if(File.Exists(files.Library)||File.Exists(files.Library+".bak")||!File.Exists(Path.Combine(legacy,"library.xml")))return null;
        if(files.ExistingFiles().Count>0)throw new IOException("Обнаружены старые и новые данные. Перенесите папку вручную через настройки: "+legacy);
        Library.Load(Path.Combine(legacy,"library.xml"));
        return new TrackerService(files,new ShikimoriCatalogSource(),()=>DateTime.UtcNow,false).ImportWindowsFolder(legacy);
    }
}
