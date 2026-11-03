using AnimeTracker;

namespace AnimeTracker.Desktop;

public sealed partial class App
{
    // Only enabled by an explicit CLI flag and a validated, isolated data folder.
    void RunLinuxSmoke()
    {
        var report=Store.FilePath("linux-smoke.txt");
        try
        {
            if(!LibraryWindow.IsVisible)throw new Exception("Startup with no tray left the app inaccessible.");
            if(Store.Widget.Enabled||Widget.IsVisible)throw new Exception("Linux widget must default to disabled.");
            if(Store.Library.Items.Count==1&&Store.Library.Items[0].Title=="Linux smoke 日本語")
            {
                var persisted=Store.Library.Items[0];if(persisted.Watched!=4||persisted.Score!=8||persisted.Notes!="Заметка")throw new Exception("Restart lost data.");
                File.WriteAllText(report,"PASS: native Linux restart preserved progress, score and notes; close exits.\n");LibraryWindow.Close();return;
            }
            if(Store.Library.Items.Count!=0)throw new Exception("Smoke folder must be empty.");
            var anime=Store.AddManual("Linux smoke 日本語");Store.Update(anime,anime.Title,3,12,"watching",8,"Заметка");Store.Increment(anime);
            var loaded=Library.Load(Store.LibraryPath).Items.Single();
            if(loaded.Watched!=4||loaded.Status!="watching"||loaded.Score!=8||loaded.Notes!="Заметка")throw new Exception("Progress persistence failed.");
            LibraryWindow.UpdateLayout();
            if(LibraryWindow.Bounds.Width<800)throw new Exception("Native Linux window not laid out.");
            var start=new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false};foreach(var argument in new[]{"--data-dir",Store.Root,"--no-tray"})start.ArgumentList.Add(argument);
            using(var duplicate=System.Diagnostics.Process.Start(start)!)
            {
                if(!duplicate.WaitForExit(5000)){duplicate.Kill(true);throw new Exception("Duplicate Linux instance did not exit.");}
                if(duplicate.ExitCode!=0||!File.Exists(Store.FilePath(".open-request")))throw new Exception("Duplicate instance did not request the existing window.");
                File.Delete(Store.FilePath(".open-request"));
            }
            File.WriteAllText(report,"PASS: packaged linux-x64 apphost, native X11 window, --startup without tray, widget disabled, +1/status/score/notes persisted; close exits.\n");
            LibraryWindow.Close(); // The normal Linux close handler must terminate the process.
        }
        catch(Exception e){File.WriteAllText(report,"FAIL: "+e);Quitting=true;if(ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)desktop.Shutdown(1);}
    }
}
