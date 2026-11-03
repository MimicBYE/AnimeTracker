using System;

namespace AnimeTracker {
// Monotonic display time. Hover/focus pauses preserve the remaining five seconds.
public sealed class UpdateNoticeLifetime {
 public static readonly TimeSpan Duration=TimeSpan.FromSeconds(5);
 readonly Func<TimeSpan> clock;
 TimeSpan remaining=Duration,started;
 bool paused;
 public UpdateNoticeLifetime(Func<TimeSpan> clock){this.clock=clock;started=clock();}
 public TimeSpan Remaining {get{
  if(paused)return remaining;var elapsed=clock()-started;if(elapsed<TimeSpan.Zero)elapsed=TimeSpan.Zero;
  return elapsed>=remaining?TimeSpan.Zero:remaining-elapsed;
 }}
 public void SetPaused(bool value){if(value==paused)return;if(value)remaining=Remaining;else started=clock();paused=value;}
}
}
