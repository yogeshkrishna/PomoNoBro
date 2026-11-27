using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;

namespace PomoNoBro
{
    [DataContract]
    internal sealed class Scenario
    {
        [DataMember(Name = "title", IsRequired = true)] public string Title { get; set; }
        [DataMember(Name = "goal", IsRequired = true)] public string Goal { get; set; }
        [DataMember(Name = "focusTerms")] public string[] FocusTerms { get; set; }
        [DataMember(Name = "rules")] public CueRules Rules { get; set; }
        [DataMember(Name = "steps", IsRequired = true)] public ScenarioStep[] Steps { get; set; }

        public static Scenario Load(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                if (stream.Length > 256 * 1024)
                    throw new InvalidDataException("Choose a scenario file under 256 KB.");

                var serializer = new DataContractJsonSerializer(typeof(Scenario));
                var scenario = (Scenario)serializer.ReadObject(stream);
                if (scenario == null || String.IsNullOrWhiteSpace(scenario.Title) ||
                    String.IsNullOrWhiteSpace(scenario.Goal) || scenario.Steps == null ||
                    scenario.Steps.Length == 0 || scenario.Steps.Length > 100)
                    throw new InvalidDataException("A scenario needs a title, goal and 1 to 100 steps.");

                scenario.Title = scenario.Title.Trim();
                scenario.Goal = scenario.Goal.Trim();
                scenario.Rules = scenario.Rules ?? new CueRules();
                scenario.Rules.Validate();
                for (int index = 0; index < scenario.Steps.Length; index++)
                {
                    var step = scenario.Steps[index];
                    if (step == null || String.IsNullOrWhiteSpace(step.Activity) ||
                        step.Seconds < 1 || step.Seconds > 3600)
                        throw new InvalidDataException(
                            String.Format("Step {0} needs an activity and 1 to 3600 seconds.", index + 1));
                    step.Kind = (step.Kind ?? "").Trim().ToLowerInvariant();
                    if (step.Kind != "activity" && step.Kind != "away" && step.Kind != "tired")
                        throw new InvalidDataException(
                            String.Format("Step {0} needs a kind of activity, away, or tired.", index + 1));
                    step.Activity = step.Activity.Trim();
                }
                return scenario;
            }
        }
    }

    [DataContract]
    internal sealed class ScenarioStep
    {
        [DataMember(Name = "activity", IsRequired = true)] public string Activity { get; set; }
        [DataMember(Name = "kind", IsRequired = true)] public string Kind { get; set; }
        [DataMember(Name = "seconds", IsRequired = true)] public int Seconds { get; set; }
    }

    [DataContract]
    internal sealed class CueRules
    {
        [DataMember(Name = "offTaskSeconds")] public int OffTaskSeconds { get; set; }
        [DataMember(Name = "awaySeconds")] public int AwaySeconds { get; set; }
        [DataMember(Name = "tiredSeconds")] public int TiredSeconds { get; set; }
        [DataMember(Name = "snoozeSeconds")] public int SnoozeSeconds { get; set; }

        public CueRules() { SetDefaults(); }

        [OnDeserializing]
        private void BeforeLoading(StreamingContext context) { SetDefaults(); }

        private void SetDefaults()
        {
            OffTaskSeconds = 40;
            AwaySeconds = 30;
            TiredSeconds = 30;
            SnoozeSeconds = 60;
        }

        public void Validate()
        {
            if (new[] { OffTaskSeconds, AwaySeconds, TiredSeconds, SnoozeSeconds }
                .Any(seconds => seconds < 1 || seconds > 3600))
                throw new InvalidDataException("Rule thresholds must be between 1 and 3600 seconds.");
        }
    }

    internal enum CueKind { Quiet, Refocus, Break }

    internal sealed class CueResult
    {
        public CueKind Kind { get; private set; }
        public string Status { get; private set; }
        public string Title { get; private set; }
        public string Detail { get; private set; }

        public CueResult(CueKind kind, string status, string title, string detail)
        {
            Kind = kind;
            Status = status;
            Title = title;
            Detail = detail;
        }
    }

    internal sealed class CueEngine
    {
        private enum Signal { OnTask, Drift, Away, Tired }

        private static readonly Regex Words = new Regex(@"[\p{L}\p{Nd}]+", RegexOptions.Compiled);
        private static readonly HashSet<string> CommonWords = new HashSet<string>(
            ("a an and at by for from in into is it of on or our the to with your " +
             "work working study finish start make watch write read review complete").Split(' '));

        private readonly CueRules rules;
        private readonly HashSet<string> correctedActivities = new HashSet<string>();
        private Signal signal;
        private int streakSeconds;
        private int snoozedUntil;
        private bool cueShown;
        private string activity;

        public string[] Terms { get; private set; }
        public int ElapsedSeconds { get; private set; }

        public CueEngine(Scenario scenario)
        {
            rules = scenario.Rules;
            var provided = scenario.FocusTerms ?? new string[0];
            Terms = provided.Select(Normalize).Where(term => term.Length > 0).Distinct().ToArray();
            if (Terms.Length == 0)
                Terms = Normalize(scenario.Goal).Split(' ')
                    .Where(term => term.Length > 0 && !CommonWords.Contains(term)).Distinct().ToArray();
        }

        public CueResult Observe(ScenarioStep step)
        {
            ElapsedSeconds += step.Seconds;
            activity = Normalize(step.Activity);
            string term = Terms.FirstOrDefault(item =>
                (" " + activity + " ").Contains(" " + item + " "));
            bool corrected = correctedActivities.Contains(activity);

            if (step.Kind == "away") SetSignal(Signal.Away);
            else if (step.Kind == "tired") SetSignal(Signal.Tired);
            else SetSignal(term == null && !corrected ? Signal.Drift : Signal.OnTask);

            if (signal == Signal.OnTask)
                return Quiet("On track", corrected
                    ? "You marked this activity as on task during this replay."
                    : "The activity matched “" + term + "”. Let the session continue.");

            streakSeconds += step.Seconds;
            if (ElapsedSeconds < snoozedUntil)
                return Quiet("Snoozed", (snoozedUntil - ElapsedSeconds) + "s until cues resume.");
            if (cueShown)
                return Quiet("Waiting quietly", "A cue already appeared during this stretch.");

            int threshold = rules.OffTaskSeconds;
            string context = "without a goal match";
            if (signal == Signal.Away)
            {
                threshold = rules.AwaySeconds;
                context = "away";
            }
            else if (signal == Signal.Tired)
            {
                threshold = rules.TiredSeconds;
                context = "of reported tiredness";
            }
            string reason = String.Format("{0}s {1} · {2}s threshold", streakSeconds, context, threshold);

            if (streakSeconds < threshold) return Quiet("Waiting quietly", reason);
            cueShown = true;
            if (signal == Signal.Tired)
                return new CueResult(CueKind.Break, "Break offered", "Take a short break?", reason);
            if (signal == Signal.Away)
                return new CueResult(CueKind.Refocus, "Nudge", "Ready to get back to it?", reason);
            return new CueResult(CueKind.Refocus, "Nudge", "Back to your goal?", reason);
        }

        public CueResult Snooze()
        {
            snoozedUntil = ElapsedSeconds + rules.SnoozeSeconds;
            cueShown = false;
            return Quiet("Snoozed", "Cues are paused for " + rules.SnoozeSeconds + "s of replay time.");
        }

        public CueResult MarkOnTask()
        {
            if (signal == Signal.Drift) correctedActivities.Add(activity);
            SetSignal(Signal.OnTask);
            return Quiet("Marked on task", "This activity will count as on task for the rest of the replay.");
        }

        public bool CanCorrect { get { return signal == Signal.Drift; } }

        private void SetSignal(Signal next)
        {
            if (signal == next) return;
            signal = next;
            streakSeconds = 0;
            cueShown = false;
        }

        public static CueResult Quiet(string title, string detail)
        {
            return new CueResult(CueKind.Quiet, title, title, detail);
        }

        private static string Normalize(string value)
        {
            return String.Join(" ", Words.Matches((value ?? "").ToLowerInvariant())
                .Cast<Match>().Select(match => match.Value));
        }
    }
}
