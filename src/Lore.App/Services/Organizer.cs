using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using Lore.Client;
using Lore.Contracts;

namespace Lore.App.Services;

/// <summary>
/// The "edit these items" flow shared by bulk selection and the info page:
/// an action sheet, then a picker or prompt, then one bulk API call.
/// </summary>
public sealed class Organizer(Session session, Saves saves)
{
	const string Cancel = "Cancel", NewItem = "+ New…", NoFolder = "(No folder)";

	public static readonly string[] Actions =
		["Save to phone", "Add to album", "Remove from album", "Add tags", "Remove tags", "Move to folder", "Set owner", "Favorite", "Not favorite", "Move to recycle bin"];

	/// <returns>True when something changed on the server.</returns>
	public async Task<bool> EditAsync(Page page, IReadOnlyList<MediaDto> items, string? action = null)
	{
		if (items.Count == 0 || session.Api is not { } api) return false;
		var ids = items.Select(i => i.Id).ToList();
		action ??= await page.DisplayActionSheetAsync($"{ids.Count} selected", Cancel, null, Actions);
		if (action == "Save to phone")
		{
			await SaveToPhoneAsync(items);
			return false; // nothing changed on the server
		}
		try
		{
			BulkEditRequest? request = action switch
			{
				"Add to album" => await PickAlbumAsync(page, api, allowNew: true) is { } a ? new(ids, AddAlbumIds: [a]) : null,
				"Remove from album" => await PickAlbumAsync(page, api, allowNew: false) is { } a ? new(ids, RemoveAlbumIds: [a]) : null,
				"Add tags" => await PromptTagsAsync(page, "Add tags") is { Count: > 0 } t ? new(ids, AddTags: t) : null,
				"Remove tags" => await PickTagAsync(page, api) is { } t ? new(ids, RemoveTags: [t]) : null,
				"Move to folder" => await PickFolderAsync(page, api) is var (ok, f) && ok ? new(ids, SetFolder: true, FolderId: f) : null,
				"Set owner" => await PickOwnerAsync(page) is { } o ? new(ids, OwnerId: o) : null,
				"Favorite" => new(ids, Favorite: true),
				"Not favorite" => new(ids, Favorite: false),
				"Move to recycle bin" => await page.DisplayAlertAsync("Move to recycle bin?", $"{ids.Count} item(s) can be restored from the bin until it is emptied.", "Move", Cancel)
					? new(ids, Delete: true) : null,
				_ => null,
			};
			if (request == null) return false;
			await api.BulkEditAsync(request);
			return true;
		}
		catch (Exception e) when (e is HttpRequestException or TaskCanceledException or LoreApiException)
		{
			await page.DisplayAlertAsync("Lore", e is LoreApiException { Status: System.Net.HttpStatusCode.Conflict or System.Net.HttpStatusCode.BadRequest } le
				? le.Message : "Couldn't reach Lore. Are you on home Wi-Fi?", "OK");
			return false;
		}
	}

	/// <summary>Queues originals for download into the phone's gallery; the background service reports the outcome.</summary>
	public async Task SaveToPhoneAsync(IReadOnlyList<MediaDto> items)
	{
		await saves.EnqueueAsync(items);
		await Toast.Make($"Saving {Format.Plural(items.Count, "item")} to your phone. Find them in Pictures/Lore and Movies/Lore.", ToastDuration.Long).Show();
	}

	public static async Task<List<string>?> PromptTagsAsync(Page page, string title)
	{
		var text = await page.DisplayPromptAsync(title, "Separate tags with commas.", "OK", Cancel, "beach, birthday");
		return text?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
	}

	async Task<int?> PickAlbumAsync(Page page, LoreApiClient api, bool allowNew)
	{
		var albums = await api.GetAlbumsAsync();
		string[] options = [.. allowNew ? [NewItem] : Array.Empty<string>(), .. albums.Select(a => a.Name)];
		if (options.Length == 0)
		{
			await page.DisplayAlertAsync("Albums", "There are no albums yet.", "OK");
			return null;
		}
		var choice = await page.DisplayActionSheetAsync("Album", Cancel, null, options);
		if (choice == NewItem)
		{
			var name = await page.DisplayPromptAsync("New album", null, "Create", Cancel);
			return string.IsNullOrWhiteSpace(name) ? null : (await api.CreateAlbumAsync(name)).Id;
		}
		return albums.FirstOrDefault(a => a.Name == choice)?.Id;
	}

	static async Task<string?> PickTagAsync(Page page, LoreApiClient api)
	{
		var tags = await api.GetTagsAsync();
		if (tags.Count == 0) return null;
		var choice = await page.DisplayActionSheetAsync("Remove tag", Cancel, null, [.. tags.Select(t => t.Name)]);
		return tags.Any(t => t.Name == choice) ? choice : null;
	}

	/// <returns>Ok=false when cancelled; FolderId null means "no folder".</returns>
	async Task<(bool Ok, int? FolderId)> PickFolderAsync(Page page, LoreApiClient api)
	{
		var folders = Flatten(await api.GetFoldersAsync());
		var labels = folders.Select(f => f.Path).ToList();
		var choice = await page.DisplayActionSheetAsync("Move to folder", Cancel, null, [NewItem, NoFolder, .. labels]);
		if (choice == NoFolder) return (true, null);
		if (choice == NewItem)
		{
			var name = await page.DisplayPromptAsync("New folder", "Created at the top level. Nest it later from Organize.", "Create", Cancel);
			return string.IsNullOrWhiteSpace(name) ? (false, null) : (true, (await api.CreateFolderAsync(name, null)).Id);
		}
		var index = labels.IndexOf(choice ?? "");
		return index < 0 ? (false, null) : (true, folders[index].Folder.Id);
	}

	async Task<int?> PickOwnerAsync(Page page)
	{
		var choice = await page.DisplayActionSheetAsync("Belongs to", Cancel, null, [.. session.Family.Select(f => f.Name)]);
		return session.Family.FirstOrDefault(f => f.Name == choice)?.Id;
	}

	/// <summary>Depth-first folder tree, children sorted by name. Paths ("Trips / Bali") are unique, so they double as labels.</summary>
	public static List<(FolderDto Folder, int Depth, string Path)> Flatten(IReadOnlyList<FolderDto> folders)
	{
		var result = new List<(FolderDto, int, string)>();
		var byParent = folders.ToLookup(f => f.ParentId);
		void Walk(int? parent, int depth, string prefix)
		{
			foreach (var f in byParent[parent].OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase))
			{
				var path = prefix + f.Name;
				result.Add((f, depth, path));
				Walk(f.Id, depth + 1, path + " / ");
			}
		}
		Walk(null, 0, "");
		return result;
	}
}
