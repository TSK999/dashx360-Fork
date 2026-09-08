using System;

namespace XboxMetroLauncher.Models;

public sealed class Profile
{
	public string ProfileId { get; set; } = Guid.NewGuid().ToString("N");

	public string Gamertag { get; set; } = "Player One";

	public string Name { get; set; } = "(No name)";

	public string GamerPicturePath { get; set; } = string.Empty;

	public int Gamerscore { get; set; } = 36000;

	public string OnlineStatus { get; set; } = "Online";

	public string Motto { get; set; } = "(No motto)";

	public string Location { get; set; } = "United States";

	public string Description { get; set; } = "(No bio)";
}
