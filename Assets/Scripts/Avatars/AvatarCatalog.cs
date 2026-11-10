using System;
using UnityEngine;

namespace EndlessRollball.Avatars
{
    public static class AvatarCatalog
    {
        public const string Cow = "cow", Whale = "whale", Phoenix = "phoenix";
        public static readonly string[] AvatarIds = { Cow, Whale, Phoenix };
        public static readonly Cosmetic[] Items = {
            new Cosmetic("cow.midnight", Cow, "appearance", "Midnight Coat", 40),
            new Cosmetic("cow.hat", Cow, "accessory", "Cowboy Hat", 60),
            new Cosmetic("whale.coral", Whale, "appearance", "Coral Wave Pattern", 40),
            new Cosmetic("whale.hat", Whale, "accessory", "Sailor Hat", 60),
            new Cosmetic("phoenix.azure", Phoenix, "appearance", "Azure Flame Plumage", 80),
            new Cosmetic("phoenix.crown", Phoenix, "accessory", "Sunfire Crown", 120),
            new Cosmetic("phoenix.aura", Phoenix, "effect", "Rebirth Aura", 180)
        };
        public static bool Exists(string id) => id == Cow || id == Whale || id == Phoenix;
        public static string Name(string id) => id == Whale ? "Tidal Whale" : id == Phoenix ? "Ember Phoenix" : "Charging Cow";
        public static string Requirement(string id) => id == Whale ? "Complete Fast-Paced Easy once" : id == Phoenix ? "Complete Standard Easy once" : "Free starter companion";
        public static Cosmetic Find(string id) => Array.Find(Items, item => item.Id == id);
    }

    public sealed class Cosmetic
    {
        public readonly string Id, Avatar, Slot, Name;
        public readonly int Price;
        public Cosmetic(string id, string avatar, string slot, string name, int price)
        { Id = id; Avatar = avatar; Slot = slot; Name = name; Price = price; }
    }

    [Serializable]
    public sealed class AvatarAppearance
    {
        public string avatarId = AvatarCatalog.Cow;
        public string appearance = "", accessory = "", effect = "";
        public AvatarAppearance Copy() => new AvatarAppearance { avatarId = avatarId, appearance = appearance, accessory = accessory, effect = effect };
        public string Get(string slot) => slot == "appearance" ? appearance : slot == "accessory" ? accessory : effect;
        public void Set(string slot, string id)
        { if (slot == "appearance") appearance = id; else if (slot == "accessory") accessory = id; else effect = id; }
    }

    // Captured before gameplay; independent of later menu changes and transient wellbeing data.
    public sealed class AvatarRunSnapshot
    {
        private readonly AvatarAppearance appearance;
        public AvatarAppearance Appearance => appearance.Copy();
        public GameMode Mode { get; }
        public Track.DifficultyMode Difficulty { get; }
        public string StartedAtUtc { get; }
        public AvatarRunSnapshot(AvatarAppearance selection, GameMode mode, Track.DifficultyMode difficulty)
        { appearance = selection.Copy(); Mode = mode; Difficulty = difficulty; StartedAtUtc = DateTime.UtcNow.ToString("o"); }
    }
}
