namespace XboxMetroLauncher.Models;

public sealed class FirstRunSetupState
{
    public bool Completed { get; set; }

    public string Language { get; set; } = "English";

    public string Locale { get; set; } = "United States";

    public string Gamertag { get; set; } = "Player";

    public bool ImportSteamLibrary { get; set; } = true;
}
