using System.Collections.ObjectModel;
using System.Windows;

namespace VisionSupport.Shell;

public sealed record ActivityEntry(DateTime At, string Source, string Message)
{
    public string Time => At.ToString("HH:mm:ss");
}

/// <summary>
/// The last few things that happened, shown on the idle screen. Deliberately bounded and in
/// memory only - this is a glance-at-it record for a tool that sits open all day, not an audit
/// trail, and an unbounded list would grow for as long as the shell runs.
/// </summary>
public sealed class ActivityLog
{
    private const int Capacity = 50;

    public ObservableCollection<ActivityEntry> Entries { get; } = new();

    public void Add(string source, string message)
    {
        void Append()
        {
            Entries.Insert(0, new ActivityEntry(DateTime.Now, source, message));
            while (Entries.Count > Capacity) Entries.RemoveAt(Entries.Count - 1);
        }

        Application? app = Application.Current;
        if (app is null || app.Dispatcher.CheckAccess()) Append();
        else app.Dispatcher.BeginInvoke(Append);
    }
}
