using System;
using System.Collections.Generic;
using System.IO;
using EndlessRollball.Track;
using UnityEngine;

namespace EndlessRollball.Avatars
{
    // Single local prototype profile. All mutations commit a complete snapshot before publishing it.
    public sealed class AvatarProgressRepository
    {
        [Serializable] private sealed class Progress
        {
            public int version = 1;
            public string profileId;
            public int balance;
            public string selected = AvatarCatalog.Cow;
            public List<string> ownedItems = new List<string>();
            public List<AvatarAppearance> appearances = new List<AvatarAppearance>();
            public List<SavedSession> sessions = new List<SavedSession>();
        }
        [Serializable] private sealed class SavedSession
        {
            public string sessionId, startedAtUtc, savedAtUtc, avatarId, mode, difficulty, outcome, trackIdentifier, scoreRules;
            public double activeSeconds, plannedSeconds;
            public int steeringRequired, steeringSucceeded, jumpsRequired, jumpsSucceeded, coins;
        }
        private static AvatarProgressRepository instance;
        public static AvatarProgressRepository Instance => instance ?? (instance = new AvatarProgressRepository());
        private Progress state;
        private readonly string path;
        public string Error { get; private set; }
        public bool IsAvailable => state != null;
        public int Balance => state?.balance ?? 0;
        public string Selected => state?.selected ?? AvatarCatalog.Cow;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInstance() => instance = null;

        private AvatarProgressRepository()
        {
            path = Path.Combine(Application.persistentDataPath, "avatar-progress-v1.json");
            try
            {
                if (File.Exists(path))
                {
                    var loaded = JsonUtility.FromJson<Progress>(File.ReadAllText(path));
                    Validate(loaded);
                    state = loaded;
                }
                else
                {
                    // Never silently replace an existing wallet with a new profile after a damaged save.
                    if (File.Exists(path + ".bak")) throw new IOException("The primary avatar save is missing; a backup is available for recovery.");
                    var fresh = new Progress { profileId = Guid.NewGuid().ToString("N") };
                    foreach (string id in AvatarCatalog.AvatarIds) fresh.appearances.Add(new AvatarAppearance { avatarId = id });
                    Write(fresh);
                    state = fresh;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException || ex is OverflowException)
            { Error = "Avatar progress could not be loaded. Your existing save has been kept. " + ex.Message; }
        }

        public bool IsUnlocked(string id) => AvatarCatalog.Exists(id) && (id == AvatarCatalog.Cow ||
            state != null && state.sessions.Exists(s => Qualifies(s, id)));
        public bool Owns(string item) => string.IsNullOrEmpty(item) || state != null && state.ownedItems.Contains(item);
        public AvatarAppearance Appearance(string avatar) => state?.appearances.Find(a => a.avatarId == avatar)?.Copy()
            ?? new AvatarAppearance { avatarId = AvatarCatalog.Exists(avatar) ? avatar : AvatarCatalog.Cow };

        public bool Select(string avatar)
        {
            if (!IsAvailable || !IsUnlocked(avatar)) return Fail("This avatar is not available yet.");
            var next = Clone(); next.selected = avatar; return Commit(next);
        }
        public bool SaveAppearance(AvatarAppearance appearance)
        {
            if (!IsAvailable || appearance == null || !IsUnlocked(appearance.avatarId)) return Fail("This avatar is not available yet.");
            if (!ValidAppearance(appearance, state)) return Fail("Choose owned items for this avatar before saving.");
            var next = Clone();
            next.appearances[next.appearances.FindIndex(a => a.avatarId == appearance.avatarId)] = appearance.Copy();
            return Commit(next);
        }
        public bool Purchase(string itemId)
        {
            var item = AvatarCatalog.Find(itemId);
            if (!IsAvailable || item == null || !IsUnlocked(item.Avatar)) return Fail("This item is not available.");
            if (Owns(itemId)) { Error = null; return true; }
            if (Balance < item.Price) return Fail("Not enough Avatar Coins.");
            var next = Clone(); next.balance -= item.Price; next.ownedItems.Add(itemId); return Commit(next);
        }

        public bool CommitSession(SessionEndInfo info, out int credited)
        {
            credited = 0;
            if (!IsAvailable) return false;
            if (info == null || string.IsNullOrEmpty(info.SessionId) || info.Avatar == null || info.Score == null)
                return Fail("This session has no saved run setup.");
            var existing = state.sessions.Find(s => s.sessionId == info.SessionId);
            if (existing != null) { credited = existing.coins; Error = null; return true; }
            var session = new SavedSession {
                sessionId = info.SessionId, startedAtUtc = info.Avatar.StartedAtUtc, savedAtUtc = DateTime.UtcNow.ToString("o"),
                avatarId = info.Avatar.Appearance.avatarId, mode = info.Avatar.Mode.ToString(), difficulty = info.Avatar.Difficulty.ToString(),
                outcome = info.Outcome.ToString(), trackIdentifier = info.TrackIdentifier, scoreRules = SessionScore.RulesId,
                activeSeconds = info.ActiveDurationSeconds, plannedSeconds = info.PlannedRouteDurationSeconds ?? 0,
                steeringRequired = info.Score.SteeringTaskCount, steeringSucceeded = info.Score.SuccessfulSteeringTaskCount,
                jumpsRequired = info.Score.RequiredJumpOpportunityCount, jumpsSucceeded = info.Score.SuccessfulJumpLandingCount,
                coins = info.Score.PotentialAvatarCoins
            };
            var next = Clone(); next.sessions.Add(session);
            try { next.balance = checked(next.balance + session.coins); }
            catch (OverflowException) { return Fail("The coin balance could not be updated."); }
            if (!Commit(next)) return false;
            credited = session.coins; return true;
        }

        private static bool Qualifies(SavedSession session, string avatar) => session.outcome == SessionOutcome.Completed.ToString()
            && session.plannedSeconds > 0 && session.difficulty == DifficultyMode.Easy.ToString()
            && session.mode == (avatar == AvatarCatalog.Whale ? GameMode.FastPaced : GameMode.Standard).ToString();
        private Progress Clone() => JsonUtility.FromJson<Progress>(JsonUtility.ToJson(state));
        private bool Fail(string message) { Error = message; return false; }
        private bool Commit(Progress next)
        {
            try { Validate(next); Write(next); state = next; Error = null; return true; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException || ex is OverflowException)
            { return Fail("Could not save avatar progress. Please retry. " + ex.Message); }
        }
        private void Write(Progress next)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(next, true));
                stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
            else File.Move(temp, path);
        }
        private static bool ValidAppearance(AvatarAppearance a, Progress p)
        {
            foreach (string slot in new[] { "appearance", "accessory", "effect" })
            {
                string id = a.Get(slot);
                if (string.IsNullOrEmpty(id)) continue;
                var item = AvatarCatalog.Find(id);
                if (item == null || item.Avatar != a.avatarId || item.Slot != slot || !p.ownedItems.Contains(id)) return false;
            }
            return true;
        }
        private static void Validate(Progress p)
        {
            if (p == null || p.version != 1 || !Guid.TryParse(p.profileId, out _) || p.balance < 0 ||
                p.sessions == null || p.ownedItems == null || p.appearances == null || !AvatarCatalog.Exists(p.selected))
                throw new InvalidOperationException("Unsupported or incomplete avatar save.");
            var ids = new HashSet<string>(); long expected = 0;
            foreach (var s in p.sessions)
            {
                if (s == null || string.IsNullOrEmpty(s.sessionId) || !ids.Add(s.sessionId) || !AvatarCatalog.Exists(s.avatarId)
                    || !Enum.TryParse(s.mode, out GameMode mode) || !Enum.IsDefined(typeof(GameMode), mode)
                    || !Enum.TryParse(s.difficulty, out DifficultyMode difficulty) || !Enum.IsDefined(typeof(DifficultyMode), difficulty)
                    || !Enum.TryParse(s.outcome, out SessionOutcome outcome) || !Enum.IsDefined(typeof(SessionOutcome), outcome)
                    || double.IsNaN(s.activeSeconds) || double.IsInfinity(s.activeSeconds) || s.activeSeconds < 0
                    || double.IsNaN(s.plannedSeconds) || double.IsInfinity(s.plannedSeconds) || s.plannedSeconds < 0
                    || outcome == SessionOutcome.Completed && (mode == GameMode.Endless || s.plannedSeconds <= 0))
                    throw new InvalidOperationException("Invalid saved session.");
                var score = new SessionScore(s.steeringRequired, s.steeringSucceeded, s.jumpsRequired, s.jumpsSucceeded);
                if (s.coins != score.PotentialAvatarCoins) throw new InvalidOperationException("Invalid saved reward.");
                expected += s.coins;
            }
            ids.Clear();
            foreach (string id in p.ownedItems)
            {
                var item = AvatarCatalog.Find(id);
                if (item == null || !ids.Add(id)) throw new InvalidOperationException("Invalid item ownership.");
                expected -= item.Price;
            }
            if (expected != p.balance) throw new InvalidOperationException("The saved wallet is inconsistent.");
            ids.Clear();
            foreach (var a in p.appearances)
                if (a == null || !AvatarCatalog.Exists(a.avatarId) || !ids.Add(a.avatarId) || !ValidAppearance(a, p))
                    throw new InvalidOperationException("Invalid saved appearance.");
            if (ids.Count != 3 || p.selected != AvatarCatalog.Cow && !p.sessions.Exists(s => Qualifies(s, p.selected)))
                throw new InvalidOperationException("Invalid avatar selection.");
        }
    }
}
