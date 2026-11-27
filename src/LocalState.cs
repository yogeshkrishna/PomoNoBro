using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace PomoNoBro
{
    [DataContract]
    internal sealed class FocusRecord
    {
        [DataMember(Name = "goal", IsRequired = true)] public string Goal { get; set; }
        [DataMember(Name = "endedAt", IsRequired = true)] public DateTimeOffset EndedAt { get; set; }
        [DataMember(Name = "elapsedSeconds", IsRequired = true)] public int ElapsedSeconds { get; set; }
        [DataMember(Name = "plannedMinutes", IsRequired = true)] public int PlannedMinutes { get; set; }
        [DataMember(Name = "completed", IsRequired = true)] public bool Completed { get; set; }
    }

    [DataContract]
    internal sealed class LocalState
    {
        public const int DefaultFocusMinutes = 25;
        public const int DefaultBreakMinutes = 5;
        public const int MaximumMinutes = 180;

        [DataMember(Name = "focusMinutes")] public int FocusMinutes { get; set; }
        [DataMember(Name = "breakMinutes")] public int BreakMinutes { get; set; }
        [DataMember(Name = "history")] public List<FocusRecord> History { get; set; }

        public LocalState() { SetDefaults(); }

        [OnDeserializing]
        private void BeforeLoading(StreamingContext context) { SetDefaults(); }

        private void SetDefaults()
        {
            FocusMinutes = DefaultFocusMinutes;
            BreakMinutes = DefaultBreakMinutes;
            History = new List<FocusRecord>();
        }

        public static bool ValidMinutes(int minutes)
        {
            return minutes >= 1 && minutes <= MaximumMinutes;
        }
    }

    internal sealed class LocalStateStore
    {
        private readonly string path;
        private readonly DataContractJsonSerializer serializer =
            new DataContractJsonSerializer(typeof(LocalState));

        public LocalStateStore(string directory)
        {
            path = Path.Combine(directory, "state.json");
        }

        public LocalState Load()
        {
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    var state = (LocalState)serializer.ReadObject(stream);
                    if (state == null || state.History == null ||
                        !LocalState.ValidMinutes(state.FocusMinutes) ||
                        !LocalState.ValidMinutes(state.BreakMinutes) ||
                        state.History.Exists(record => record == null ||
                            String.IsNullOrWhiteSpace(record.Goal) ||
                            !LocalState.ValidMinutes(record.PlannedMinutes) ||
                            record.ElapsedSeconds < 0 ||
                            record.ElapsedSeconds > record.PlannedMinutes * 60))
                        throw new SerializationException("The saved focus history has invalid values.");
                    return state;
                }
            }
            catch (FileNotFoundException) { return new LocalState(); }
            catch (DirectoryNotFoundException) { return new LocalState(); }
        }

        public void Save(LocalState state)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write))
                {
                    serializer.WriteObject(stream, state);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                File.Delete(temporary);
            }
        }
    }
}
