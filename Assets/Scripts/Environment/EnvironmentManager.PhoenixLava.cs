namespace EndlessRollball.Scenery
{
    public sealed partial class EnvironmentManager
    {
        // Dungeon flight retains its surroundings; only Beach surfing hides scenery.
        private bool IsCarrierSceneryHidden(EnvironmentTheme theme) =>
            beachSurfPresentation && theme == EnvironmentTheme.Water;
    }
}
