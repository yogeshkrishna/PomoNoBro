using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using Microsoft.Win32;
using Path = System.IO.Path;

namespace PomoNoBro
{
    internal sealed class DesktopApp
    {
        private readonly Window window;
        private readonly LocalStateStore storage;
        private readonly LocalState saved;
        private readonly bool canSave;
        private readonly string showcaseDirectory;
        private readonly FocusTimer timer = new FocusTimer();
        private readonly DispatcherTimer tick;
        private readonly TextBox goal;
        private readonly TextBox focusLength;
        private readonly TextBox breakLength;
        private readonly ObservableCollection<ScenarioChoice> scenarios = new ObservableCollection<ScenarioChoice>();
        private readonly List<CueResult> outcomes = new List<CueResult>();
        private Scenario scenario;
        private CueEngine engine;

        public DesktopApp(Window window, string stateDirectory, string appDirectory)
        {
            this.window = window;
            storage = new LocalStateStore(stateDirectory);
            showcaseDirectory = Path.Combine(appDirectory, "Showcase");
            try
            {
                saved = storage.Load();
                canSave = true;
            }
            catch (Exception ex)
            {
                if (!IsFileError(ex)) throw;
                saved = new LocalState();
                canSave = false;
                ShowStorageMessage("Saved history could not be read. This run will keep new sessions in memory.");
            }

            goal = Control<TextBox>("GoalInput");
            focusLength = Control<TextBox>("FocusMinutesInput");
            breakLength = Control<TextBox>("BreakMinutesInput");
            focusLength.Text = saved.FocusMinutes.ToString();
            breakLength.Text = saved.BreakMinutes.ToString();
            goal.TextChanged += delegate
            {
                Control<TextBlock>("GoalPlaceholder").Visibility = VisibilityFor(goal.Text.Length == 0);
                ShowMessage("TimerMessage", null);
            };
            focusLength.TextChanged += delegate { RefreshDuration(); };
            breakLength.TextChanged += delegate { RefreshDuration(); };
            Click("NavFocus", delegate { Navigate("Focus"); });
            Click("NavShowcase", delegate { Navigate("Showcase"); });
            Click("NavHistory", delegate { Navigate("History"); });
            Click("FocusMode", delegate { SelectMode(TimerMode.Focus); });
            Click("BreakMode", delegate { SelectMode(TimerMode.Break); });
            Click("TimerPrimary", UseTimer);
            Click("TimerEnd", FinishTimer);
            Click("OpenScenario", OpenScenario);
            Click("NextStep", RunStep);
            Click("ResetReplay", ResetReplay);
            Click("DismissCue", delegate
            {
                RenderCue(CueEngine.Quiet("Cue dismissed", "The replay can continue."));
                Control<Button>("NextStep").Focus();
            });
            Click("SnoozeCue", delegate
            {
                RenderCue(engine.Snooze());
                Control<Button>("NextStep").Focus();
            });
            Click("CorrectCue", delegate
            {
                RenderCue(engine.MarkOnTask());
                Control<Button>("NextStep").Focus();
            });
            var picker = Control<ComboBox>("ScenarioPicker");
            picker.ItemsSource = scenarios;
            picker.SelectionChanged += delegate
            {
                var selected = (ScenarioChoice)picker.SelectedItem;
                scenario = selected == null ? null : selected.Data;
                ResetReplay();
            };

            tick = new DispatcherTimer(DispatcherPriority.Background);
            tick.Interval = TimeSpan.FromMilliseconds(125);
            tick.Tick += delegate
            {
                if (timer.Remaining == TimeSpan.Zero) FinishTimer();
                else RenderClock();
            };
            window.Closing += delegate
            {
                if (timer.IsActive) FinishTimer();
            };

            LoadPackagedScenarios();
            ResetReplay();
            SelectMode(TimerMode.Focus);
            Navigate("Focus");
        }

        private void Navigate(string destination)
        {
            foreach (string page in new[] { "Focus", "Showcase", "History" })
            {
                bool selected = page == destination;
                Control<StackPanel>(page + "Page").Visibility = VisibilityFor(selected);
                Control<Button>("Nav" + page).Tag = selected ? "Selected" : null;
            }
            if (destination == "History") RenderHistory();
            Control<ScrollViewer>("PageScroll").ScrollToTop();
        }

        private void RefreshDuration()
        {
            ShowMessage("TimerMessage", null);
            if (timer.Phase == TimerPhase.Ready) SelectMode(timer.Mode);
        }

        private void SelectMode(TimerMode mode)
        {
            if (timer.IsActive) return;
            bool focus = mode == TimerMode.Focus;
            int minutes;
            if (!TryMinutes(focus ? focusLength : breakLength, out minutes))
                minutes = focus ? saved.FocusMinutes : saved.BreakMinutes;
            ShowMessage("TimerMessage", null);
            timer.Prepare(mode, minutes);
            RenderTimer();
        }

        private void UseTimer()
        {
            if (timer.Phase == TimerPhase.Running)
            {
                if (timer.Remaining == TimeSpan.Zero)
                {
                    FinishTimer();
                    return;
                }
                timer.Pause();
                tick.Stop();
            }
            else if (timer.Phase == TimerPhase.Paused)
            {
                timer.Resume();
                tick.Start();
            }
            else
            {
                TimerMode mode = timer.Mode;
                if (timer.Phase == TimerPhase.Finished)
                    mode = mode == TimerMode.Focus ? TimerMode.Break : TimerMode.Focus;
                StartTimer(mode);
            }
            RenderTimer();
        }

        private void StartTimer(TimerMode mode)
        {
            bool focus = mode == TimerMode.Focus;
            string text = goal.Text.Trim();
            if (focus && text.Length == 0)
            {
                ShowMessage("TimerMessage", "Add a goal to start your focus session.");
                goal.Focus();
                return;
            }
            int focusMinutes;
            int breakMinutes;
            if (!TryMinutes(focusLength, out focusMinutes))
            {
                ShowLengthError("Focus", focusLength);
                return;
            }
            if (!TryMinutes(breakLength, out breakMinutes))
            {
                ShowLengthError("Break", breakLength);
                return;
            }

            ShowMessage("TimerMessage", null);
            if (saved.FocusMinutes != focusMinutes || saved.BreakMinutes != breakMinutes)
            {
                saved.FocusMinutes = focusMinutes;
                saved.BreakMinutes = breakMinutes;
                SaveState();
            }
            timer.Prepare(mode, focus ? focusMinutes : breakMinutes);
            timer.Start(focus ? text : "");
            tick.Start();
        }

        private void FinishTimer()
        {
            if (!timer.IsActive) return;
            tick.Stop();
            FocusRecord record = timer.Finish();
            if (record != null)
            {
                saved.History.Add(record);
                SaveState();
                RenderHistory();
            }
            RenderTimer();
            if (Control<StackPanel>("FocusPage").IsVisible) Control<Button>("TimerPrimary").Focus();
        }

        private void RenderTimer()
        {
            bool focus = timer.Mode == TimerMode.Focus;
            bool active = timer.IsActive;
            Control<Button>("FocusMode").Tag = focus ? "Selected" : null;
            Control<Button>("BreakMode").Tag = focus ? null : "Selected";
            Control<Button>("FocusMode").IsEnabled = !active;
            Control<Button>("BreakMode").IsEnabled = !active;
            goal.IsReadOnly = focus && active;
            focusLength.IsReadOnly = active;
            breakLength.IsReadOnly = active;
            Control<TextBlock>("TimerModeText").Text = focus ? "FOCUS" : "BREAK";
            var end = Control<Button>("TimerEnd");
            end.Visibility = VisibilityFor(active);
            end.Content = focus ? "End session" : "End break";

            string primary;
            string status;
            switch (timer.Phase)
            {
                case TimerPhase.Running:
                    primary = "Pause";
                    status = focus ? "Focusing" : "On a break";
                    break;
                case TimerPhase.Paused:
                    primary = "Resume";
                    status = "Paused";
                    break;
                case TimerPhase.Finished:
                    primary = focus ? "Start break" : "Start focus";
                    status = timer.Remaining == TimeSpan.Zero ? "Complete" : "Ended early";
                    break;
                default:
                    primary = focus ? "Start focus" : "Start break";
                    status = "Ready";
                    break;
            }
            Control<Button>("TimerPrimary").Content = primary;
            Control<TextBlock>("ClockStatus").Text = status;
            RenderClock();
        }

        private void RenderClock()
        {
            Control<TextBlock>("ClockText").Text = Clock((int)Math.Ceiling(timer.Remaining.TotalSeconds));
            var arc = Control<System.Windows.Shapes.Path>("ProgressArc");
            const double radius = 116;
            var center = new Point(132, 132);
            double fraction = timer.RemainingFraction;
            if (fraction <= 0)
            {
                arc.Data = Geometry.Empty;
                return;
            }
            if (fraction >= 0.99995)
            {
                arc.Data = new EllipseGeometry(center, radius, radius);
                return;
            }
            double angle = fraction * 2 * Math.PI;
            var end = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));
            var figure = new PathFigure { StartPoint = new Point(center.X, center.Y - radius) };
            figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0,
                fraction > 0.5, SweepDirection.Clockwise, true));
            arc.Data = new PathGeometry(new[] { figure });
        }

        private void SaveState()
        {
            if (!canSave) return;
            try
            {
                storage.Save(saved);
                ShowStorageMessage(null);
            }
            catch (Exception ex)
            {
                if (!IsFileError(ex)) throw;
                ShowStorageMessage("History could not be saved. It is available in memory for this run.");
            }
        }

        private void RenderHistory()
        {
            var history = saved.History;
            long today = history.Where(record => record.EndedAt.LocalDateTime.Date == DateTime.Today)
                .Sum(record => (long)record.ElapsedSeconds);
            Control<TextBlock>("FocusToday").Text = Duration(today);
            Control<TextBlock>("CompletedSessions").Text = history.Count(record => record.Completed).ToString();
            Control<StackPanel>("EmptyHistory").Visibility = VisibilityFor(history.Count == 0);
            Control<ItemsControl>("HistoryList").ItemsSource = history
                .OrderByDescending(record => record.EndedAt)
                .Select(record => new HistoryRow
                {
                    Goal = record.Goal,
                    Summary = Duration(record.ElapsedSeconds) + (record.Completed ? " · Completed" : " · Ended early"),
                    Date = record.EndedAt.LocalDateTime.ToString("MMM d, HH:mm")
                }).ToArray();
        }

        private void LoadPackagedScenarios()
        {
            string warning = null;
            try
            {
                if (!Directory.Exists(showcaseDirectory)) return;
                foreach (string file in Directory.GetFiles(showcaseDirectory, "*.json").OrderBy(path => path))
                {
                    try { scenarios.Add(new ScenarioChoice(file, Scenario.Load(file))); }
                    catch (Exception ex)
                    {
                        if (!IsFileError(ex)) throw;
                        warning = "Could not read " + Path.GetFileName(file) + ". Check the JSON or open another scenario.";
                    }
                }
                if (scenarios.Count > 0) Control<ComboBox>("ScenarioPicker").SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                if (!IsFileError(ex)) throw;
                warning = "The bundled scenarios could not be read. Use Open JSON to choose a local scenario.";
            }
            ShowMessage("ScenarioError", warning);
        }

        private void OpenScenario()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Open a showcase scenario",
                Filter = "Scenario JSON (*.json)|*.json",
                CheckFileExists = true,
                InitialDirectory = Directory.Exists(showcaseDirectory) ? showcaseDirectory : ""
            };
            if (dialog.ShowDialog(window) == true) LoadScenarioFile(dialog.FileName);
        }

        private void LoadScenarioFile(string file)
        {
            try
            {
                var choice = new ScenarioChoice(file, Scenario.Load(file));
                var previous = scenarios.FirstOrDefault(item =>
                    String.Equals(item.File, file, StringComparison.OrdinalIgnoreCase));
                if (previous != null) scenarios[scenarios.IndexOf(previous)] = choice;
                else scenarios.Add(choice);
                Control<ComboBox>("ScenarioPicker").SelectedItem = choice;
                ShowMessage("ScenarioError", null);
            }
            catch (Exception ex)
            {
                if (!IsFileError(ex)) throw;
                string detail = ex is InvalidDataException ? ex.Message : "Check that the file uses the showcase JSON format.";
                ShowMessage("ScenarioError", "Could not open " + Path.GetFileName(file) + ". " + detail);
            }
        }

        private void ResetReplay()
        {
            outcomes.Clear();
            engine = scenario == null ? null : new CueEngine(scenario);
            var picker = Control<ComboBox>("ScenarioPicker");
            picker.IsEnabled = scenarios.Count > 0;
            Control<ScrollViewer>("TimelineScroll").ScrollToTop();
            Control<TextBlock>("ScenarioGoal").Text = scenario == null ? "Choose a local scenario" : scenario.Goal;
            Control<TextBlock>("ScenarioTerms").Text = engine == null ? "" :
                "Focus terms · " + (engine.Terms.Length == 0 ? "None" : String.Join(" · ", engine.Terms));
            Control<TextBlock>("RuleSummary").Text = scenario == null ? "" : String.Format(
                "Cue after {0}s off task, {1}s away, or {2}s tired. Snooze for {3}s.",
                scenario.Rules.OffTaskSeconds, scenario.Rules.AwaySeconds,
                scenario.Rules.TiredSeconds, scenario.Rules.SnoozeSeconds);
            RenderCue(scenario == null
                ? CueEngine.Quiet("No scenario loaded", "Open a local JSON file to run a showcase.")
                : CueEngine.Quiet("Ready to replay", "Run one step at a time to see how the timer responds."));
            RenderReplay();
        }

        private void RunStep()
        {
            if (scenario == null) return;
            if (outcomes.Count == scenario.Steps.Length)
            {
                ResetReplay();
                return;
            }
            var result = engine.Observe(scenario.Steps[outcomes.Count]);
            outcomes.Add(result);
            RenderCue(result);
            RenderReplay();
            int current = outcomes.Count - 1;
            var timeline = Control<ItemsControl>("Timeline");
            timeline.UpdateLayout();
            var row = timeline.ItemContainerGenerator.ContainerFromIndex(current) as FrameworkElement;
            if (row != null) row.BringIntoView();
        }

        private void RenderCue(CueResult result)
        {
            bool showActions = result.Kind != CueKind.Quiet;
            Control<TextBlock>("CueTitle").Text = result.Title;
            Control<TextBlock>("CueDetail").Text = result.Detail;
            Control<WrapPanel>("CueActions").Visibility = VisibilityFor(showActions);
            Control<Button>("CorrectCue").Visibility = VisibilityFor(showActions && engine.CanCorrect);
            Color color = result.Kind == CueKind.Refocus ? Color.FromRgb(250, 237, 213) :
                result.Kind == CueKind.Break ? Color.FromRgb(222, 239, 227) : Color.FromRgb(238, 243, 235);
            Control<Border>("CueCard").Background = new SolidColorBrush(color);
        }

        private void RenderReplay()
        {
            int total = scenario == null ? 0 : scenario.Steps.Length;
            int index = outcomes.Count;
            Control<TextBlock>("ReplayClock").Text = Clock(engine == null ? 0 : engine.ElapsedSeconds);
            Control<TextBlock>("StepCount").Text = total == 0 ? "" : index + " / " + total + " steps";
            Control<ProgressBar>("ReplayProgress").Value = total == 0 ? 0 : 100.0 * index / total;
            var next = Control<Button>("NextStep");
            next.IsEnabled = total > 0;
            next.Content = index == total && total > 0 ? "Replay from start" : "Run next step";
            Control<Button>("ResetReplay").IsEnabled = index > 0 && index < total;
            Control<ItemsControl>("Timeline").ItemsSource = scenario == null ? null : scenario.Steps
                .Select((step, position) => new TimelineRow
                {
                    Activity = step.Activity,
                    Duration = Duration(step.Seconds),
                    Status = position >= index ? "Upcoming" : outcomes[position].Status,
                    Current = position == Math.Max(0, index - 1)
                }).ToArray();
        }

        private void ShowLengthError(string label, TextBox input)
        {
            ShowMessage("TimerMessage", label + " length must be between 1 and " + LocalState.MaximumMinutes + " minutes.");
            input.Focus();
            input.SelectAll();
        }

        private void ShowStorageMessage(string message)
        {
            ShowMessage("StorageMessage", message);
            ShowMessage("FocusStorageMessage", message);
        }

        private static bool TryMinutes(TextBox input, out int minutes)
        {
            return Int32.TryParse(input.Text, out minutes) && LocalState.ValidMinutes(minutes);
        }

        private static bool IsFileError(Exception exception)
        {
            return exception is IOException || exception is UnauthorizedAccessException ||
                exception is SerializationException || exception is XmlException ||
                exception is InvalidDataException;
        }

        private static string Clock(int seconds)
        {
            return String.Format("{0:00}:{1:00}", seconds / 60, seconds % 60);
        }

        private static string Duration(long seconds)
        {
            if (seconds < 60) return seconds + "s";
            if (seconds < 3600)
                return seconds % 60 == 0 ? seconds / 60 + "m" : seconds / 60 + "m " + seconds % 60 + "s";
            return seconds / 3600 + "h " + (seconds / 60) % 60 + "m";
        }

        private void ShowMessage(string name, string message)
        {
            var text = Control<TextBlock>(name);
            text.Text = message ?? "";
            text.Visibility = VisibilityFor(message != null);
        }

        private T Control<T>(string name) where T : FrameworkElement { return (T)window.FindName(name); }
        private void Click(string name, Action action) { Control<Button>(name).Click += delegate { action(); }; }
        private static Visibility VisibilityFor(bool visible) { return visible ? Visibility.Visible : Visibility.Collapsed; }

        private sealed class ScenarioChoice
        {
            public string File { get; private set; }
            public Scenario Data { get; private set; }
            public string Title { get { return Data.Title; } }

            public ScenarioChoice(string file, Scenario data)
            {
                File = file;
                Data = data;
            }
        }

        private sealed class TimelineRow
        {
            public string Activity { get; set; }
            public string Duration { get; set; }
            public string Status { get; set; }
            public bool Current { get; set; }
        }

        private sealed class HistoryRow
        {
            public string Goal { get; set; }
            public string Summary { get; set; }
            public string Date { get; set; }
        }
    }
}
