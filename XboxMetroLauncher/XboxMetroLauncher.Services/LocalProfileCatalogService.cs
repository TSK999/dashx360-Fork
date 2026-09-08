using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XboxMetroLauncher.Models;

namespace XboxMetroLauncher.Services;

public sealed class LocalProfileCatalogService
{
    private const string CatalogFileName = "profiles.json";
    private const string ActiveProfileFileName = "profile.json";
    private readonly IJsonStore _store;

    public LocalProfileCatalogService(IJsonStore store)
    {
        _store = store;
    }

    public async Task<List<Profile>> LoadAsync(Profile activeProfile, CancellationToken cancellationToken = default)
    {
        List<Profile> profiles = await _store.ReadAsync<List<Profile>>(CatalogFileName, cancellationToken).ConfigureAwait(false)
            ?? new List<Profile>();

        EnsureProfileIdentity(activeProfile);
        int activeIndex = profiles.FindIndex(profile => string.Equals(profile.ProfileId, activeProfile.ProfileId, StringComparison.OrdinalIgnoreCase));
        if (activeIndex >= 0)
        {
            profiles[activeIndex] = Clone(activeProfile);
        }
        else
        {
            profiles.Insert(0, Clone(activeProfile));
        }

        Normalize(profiles);
        await SaveAsync(profiles, cancellationToken).ConfigureAwait(false);
        await _store.WriteAsync(ActiveProfileFileName, activeProfile, cancellationToken).ConfigureAwait(false);
        return profiles;
    }

    public Task SaveAsync(IEnumerable<Profile> profiles, CancellationToken cancellationToken = default)
    {
        List<Profile> snapshot = profiles.Select(Clone).ToList();
        Normalize(snapshot);
        return _store.WriteAsync(CatalogFileName, snapshot, cancellationToken);
    }

    public async Task ActivateAsync(Profile profile, IEnumerable<Profile> profiles, CancellationToken cancellationToken = default)
    {
        EnsureProfileIdentity(profile);
        List<Profile> snapshot = profiles.Select(Clone).ToList();
        Normalize(snapshot);
        int index = snapshot.FindIndex(item => string.Equals(item.ProfileId, profile.ProfileId, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            snapshot[index] = Clone(profile);
        }
        else
        {
            snapshot.Add(Clone(profile));
        }

        await _store.WriteAsync(CatalogFileName, snapshot, cancellationToken).ConfigureAwait(false);
        await _store.WriteAsync(ActiveProfileFileName, profile, cancellationToken).ConfigureAwait(false);
    }

    public static Profile Clone(Profile profile)
    {
        EnsureProfileIdentity(profile);
        return new Profile
        {
            ProfileId = profile.ProfileId,
            Gamertag = profile.Gamertag,
            Name = profile.Name,
            GamerPicturePath = profile.GamerPicturePath,
            Gamerscore = profile.Gamerscore,
            OnlineStatus = profile.OnlineStatus,
            Motto = profile.Motto,
            Location = profile.Location,
            Description = profile.Description
        };
    }

    private static void Normalize(List<Profile> profiles)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = profiles.Count - 1; index >= 0; index--)
        {
            Profile profile = profiles[index];
            EnsureProfileIdentity(profile);
            if (!seen.Add(profile.ProfileId))
            {
                profiles.RemoveAt(index);
            }
        }
    }

    private static void EnsureProfileIdentity(Profile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.ProfileId))
        {
            profile.ProfileId = Guid.NewGuid().ToString("N");
        }
    }
}
