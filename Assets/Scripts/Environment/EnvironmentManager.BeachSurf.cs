namespace EndlessRollball.Scenery
{
    public sealed partial class EnvironmentManager
    {
        private bool beachSurfPresentation;

        internal void SetBeachSurfPresentation(bool active)
        {
            if (beachSurfPresentation == active)
                return;

            beachSurfPresentation = active;
            // Retain pool ownership and transition visibility while hiding props.
            foreach (SceneryPair pair in activeScenery.Values)
            {
                if (pair.Theme == EnvironmentTheme.Water && pair.Instance != null)
                    pair.Instance.SetActive(!active && !pair.HiddenForTransition);
            }
        }
    }
}
