using AnimeTracker;
var root = Path.Combine(Path.GetTempPath(), "anime-tracker-core-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    LibraryView.Test(); EpisodeUpdates.Test(); Ratings.Test(root); SequelSuggestions.Test(); CalendarNotes.Test(root); CardEdits.Test(root); EveningChoice.Test(root); PersonalOrder.Test(root); ArcData.Test(root); ArcCatalog.Test(root); WatchChoices.Test(root); ViewPreferences.Test(root);
    Console.WriteLine(MalImportTests.Run());
    var anime = new Anime { Title = "Русский 日本語 & notes", Notes = "Моя заметка", Status = "planned", MalId = 21, Watched = 4, Total = 12, Score = 8 };
    var library = new Library(); library.Items.Add(anime); var path = Path.Combine(root, "library.xml"); library.Save(path);
    var copy = Library.Load(path).Items.Single(); if (copy.Notes != anime.Notes || copy.Id != anime.Id || copy.MalId != 21 || copy.Score != 8 || copy.Watched != 4) throw new Exception("Cross-platform XML roundtrip");
    PortTests.Run(root);
    await CoverTests.Run();
    Console.WriteLine("PASS: shared Windows/macOS domain, XML, MAL/GZIP, queue/hiding, arcs, recommendations, ratings, notes, calendar, metadata preservation");
    return 0;
}
catch(Exception error) { Console.Error.WriteLine(error); return 1; }
finally { if(Directory.Exists(root)) Directory.Delete(root,true); if(File.Exists(root+".tmp"))File.Delete(root+".tmp"); }
