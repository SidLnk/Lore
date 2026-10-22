using Android;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Provider;
using System.Globalization;
using System.Security.Cryptography;
using Lore.Contracts;
using Microsoft.Win32.SafeHandles;
using Uri = Android.Net.Uri;
using Path = System.IO.Path;

namespace Lore.App.Droid;

public sealed record DeviceMedia(long Id, string Name, string MimeType, long Size, DateTime? Taken, bool IsVideo, DateTime Added)
{
	/// <summary>When it was taken, or when it arrived on the phone if the file has no date (screenshots, downloads).</summary>
	public DateTime When => Taken ?? Added;

	public Uri ContentUri => ContentUris.WithAppendedId(IsVideo ? MediaStore.Video.Media.ExternalContentUri! : MediaStore.Images.Media.ExternalContentUri!, Id);
}

/// <summary>
/// Reads the phone's photos and videos straight from MediaStore.
/// Unlike the system photo picker, this can ask for the original bytes (MediaStore.setRequireOriginal),
/// so GPS EXIF survives; the picker zeroes it in place.
/// </summary>
public static class DeviceGallery
{
	/// <summary>Read access to photos and videos, plus unredacted location (Android 10+).</summary>
	public sealed class MediaPermission : Permissions.BasePlatformPermission
	{
		public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
		[
			.. OperatingSystem.IsAndroidVersionAtLeast(33)
				? new[] { (Manifest.Permission.ReadMediaImages, true), (Manifest.Permission.ReadMediaVideo, true) }
				: [(Manifest.Permission.ReadExternalStorage, true)],
			// Android 8-10: Save to phone (8-9) and Free up space (8-10) write to shared storage directly.
			.. OperatingSystem.IsAndroidVersionAtLeast(30) ? Array.Empty<(string, bool)>() : [(Manifest.Permission.WriteExternalStorage, true)],
			.. OperatingSystem.IsAndroidVersionAtLeast(29) ? [(Manifest.Permission.AccessMediaLocation, true)] : Array.Empty<(string, bool)>(),
		];
	}

	static ContentResolver Resolver => Platform.AppContext.ContentResolver!;

	/// <summary>Newest first by date taken (date added when there is none). videos: null = both, true/false = only those.</summary>
	public static List<DeviceMedia> Query(int offset, int limit, bool? videos = null)
	{
		string[] projection =
		[
			IBaseColumns.Id, MediaStore.IMediaColumns.DisplayName, MediaStore.IMediaColumns.MimeType,
			MediaStore.IMediaColumns.Size, "datetaken" /* column predates the API 29 constant */, MediaStore.Files.IFileColumns.MediaType,
			MediaStore.IMediaColumns.DateAdded,
		];
		var args = new Bundle();
		var types = videos switch { true => "3", false => "1", null => "1, 3" }; // 1 image, 3 video
		args.PutString(ContentResolver.QueryArgSqlSelection, $"{MediaStore.Files.IFileColumns.MediaType} IN ({types})");
		// MediaProvider ignores QueryArgSortColumns; the SQL sort order is honoured. Taken order keeps month groups whole.
		args.PutString(ContentResolver.QueryArgSqlSortOrder,
			$"COALESCE(datetaken, {MediaStore.IMediaColumns.DateAdded} * 1000) DESC, {IBaseColumns.Id} DESC");
		args.PutInt(ContentResolver.QueryArgLimit, limit);
		args.PutInt(ContentResolver.QueryArgOffset, offset);

		var result = new List<DeviceMedia>();
		using var c = Resolver.Query(MediaStore.Files.GetContentUri("external")!, projection, args, null);
		if (c == null) return result;
		while (c.MoveToNext())
		{
			var taken = c.IsNull(4) ? (DateTime?)null : DateTimeOffset.FromUnixTimeMilliseconds(c.GetLong(4)).LocalDateTime;
			var added = DateTimeOffset.FromUnixTimeSeconds(c.GetLong(6)).LocalDateTime;
			result.Add(new DeviceMedia(c.GetLong(0), c.GetString(1) ?? $"media_{c.GetLong(0)}", c.GetString(2) ?? "", c.GetLong(3), taken, c.GetInt(5) == 3, added));
		}
		return result;
	}

	/// <summary>Current size of a gallery file, or null when it's gone (deleted, or in the phone's trash).</summary>
	public static long? CurrentSize(string contentUri)
	{
		// A query by item URI still returns trashed rows, so ask for is_trashed and treat those as gone.
		var trashColumn = OperatingSystem.IsAndroidVersionAtLeast(30);
		using var c = Resolver.Query(Uri.Parse(contentUri)!,
			trashColumn ? [MediaStore.IMediaColumns.Size, MediaStore.IMediaColumns.IsTrashed] : [MediaStore.IMediaColumns.Size], null, null, null);
		if (c == null || !c.MoveToFirst() || trashColumn && c.GetInt(1) == 1) return null;
		return c.GetLong(0);
	}

	/// <summary>SHA-256 (lower-case hex) of a gallery file's original bytes, or null if it can't be read.</summary>
	public static string? Hash(string contentUri)
	{
		try
		{
			using var s = OpenOriginal(contentUri);
			return Convert.ToHexStringLower(SHA256.HashData(s));
		}
		catch (Exception e) when (e is IOException or Java.Lang.Exception)
		{
			return null;
		}
	}

	const int TrashRequest = 4201;
	static TaskCompletionSource<bool>? _trash;

	/// <summary>
	/// Removes gallery files after the system asks the user. Android 11+: into the phone's trash (restorable for
	/// 30 days). Android 8-10: deleted for good (the caller confirms first).
	/// </summary>
	/// <returns>False when the user said no.</returns>
	public static async Task<bool> RemoveAsync(IReadOnlyList<string> contentUris)
	{
		if (contentUris.Count == 0) return true;
		if (OperatingSystem.IsAndroidVersionAtLeast(30))
		{
			var request = MediaStore.CreateTrashRequest(Resolver, [.. contentUris.Select(u => Uri.Parse(u)!)], true);
			_trash = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			Platform.CurrentActivity!.StartIntentSenderForResult(request.IntentSender, TrashRequest, null, 0, 0, 0);
			return await _trash.Task;
		}
		foreach (var u in contentUris) Resolver.Delete(Uri.Parse(u)!, null, null);
		return true;
	}

	/// <summary>Called from MainActivity.OnActivityResult.</summary>
	public static void OnActivityResult(int requestCode, Android.App.Result result)
	{
		if (requestCode == TrashRequest) _trash?.TrySetResult(result == Android.App.Result.Ok);
	}

	/// <summary>
	/// Content URI of a gallery photo or video with exactly these bytes, or null.
	/// Size narrows the candidates (usually to none); SHA-256 confirms.
	/// </summary>
	public static string? FindOriginal(long size, string sha256)
	{
		string[] projection = [IBaseColumns.Id, MediaStore.Files.IFileColumns.MediaType];
		var args = new Bundle();
		args.PutString(ContentResolver.QueryArgSqlSelection, $"{MediaStore.IMediaColumns.Size} = ? AND {MediaStore.Files.IFileColumns.MediaType} IN (1, 3)");
		args.PutStringArray(ContentResolver.QueryArgSqlSelectionArgs, [size.ToString(CultureInfo.InvariantCulture)]);
		using var c = Resolver.Query(MediaStore.Files.GetContentUri("external")!, projection, args, null);
		if (c == null) return null;
		while (c.MoveToNext())
		{
			var collection = c.GetInt(1) == 3 ? MediaStore.Video.Media.ExternalContentUri! : MediaStore.Images.Media.ExternalContentUri!;
			var uri = ContentUris.WithAppendedId(collection, c.GetLong(0)).ToString()!;
			// Hash is null when unreadable (gone, or another app's file without permission): not a match.
			if (Hash(uri) is { } h && h.Equals(sha256, StringComparison.OrdinalIgnoreCase)) return uri;
		}
		return null;
	}

	/// <summary>
	/// Writes a Lore original into the gallery under Pictures/Lore or Movies/Lore, checking its SHA-256 on the way.
	/// Nothing is left behind if the download fails or doesn't match.
	/// </summary>
	public static async Task SaveAsync(MediaDto m, Stream source, CancellationToken ct)
	{
		var video = m.MediaType == MediaTypes.Video;
		var folder = Path.Combine(video ? Android.OS.Environment.DirectoryMovies! : Android.OS.Environment.DirectoryPictures!, "Lore");
		if (OperatingSystem.IsAndroidVersionAtLeast(29))
		{
			var values = new ContentValues();
			values.Put(MediaStore.IMediaColumns.DisplayName, m.OriginalFilename);
			values.Put(MediaStore.IMediaColumns.MimeType, m.MimeType);
			values.Put(MediaStore.IMediaColumns.RelativePath, folder);
			// DateTaken is wall-clock time where it was taken; the phone's zone is the family's zone.
			values.Put(MediaStore.IMediaColumns.DateTaken, new DateTimeOffset(m.DateTaken, TimeZoneInfo.Local.GetUtcOffset(m.DateTaken)).ToUnixTimeMilliseconds());
			values.Put(MediaStore.IMediaColumns.IsPending, 1);
			var collection = video
				? MediaStore.Video.Media.GetContentUri(MediaStore.VolumeExternalPrimary)!
				: MediaStore.Images.Media.GetContentUri(MediaStore.VolumeExternalPrimary)!;
			var uri = Resolver.Insert(collection, values) ?? throw new IOException("The phone's gallery refused the file");
			try
			{
				await using (var output = Resolver.OpenOutputStream(uri, "w") ?? throw new IOException("Couldn't write to the gallery"))
					await CopyVerifiedAsync(source, output, m.Sha256, ct);
				var done = new ContentValues();
				done.Put(MediaStore.IMediaColumns.IsPending, 0);
				// shortcut: publishing rescans the file and MediaProvider takes DATE_TAKEN from the file's own EXIF/metadata,
				// ignoring ours (re-applying it after publish doesn't stick either). Camera photos and videos carry a date;
				// files without one (screenshots, some edits) sort as "saved today" in the gallery. Fix would be writing EXIF.
				Resolver.Update(uri, done, null, null);
			}
			catch
			{
				Resolver.Delete(uri, null, null);
				throw;
			}
			return;
		}

		// Android 8-9: a plain file in the shared folder, then ask the media scanner to index it.
		var dir = Directory.CreateDirectory(Path.Combine(Android.OS.Environment.ExternalStorageDirectory!.AbsolutePath, folder)).FullName;
		var path = Path.Combine(dir, m.OriginalFilename);
		for (var n = 1; File.Exists(path); n++)
			path = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(m.OriginalFilename)} ({n}){Path.GetExtension(m.OriginalFilename)}");
		try
		{
			await using (var output = File.Create(path))
				await CopyVerifiedAsync(source, output, m.Sha256, ct);
		}
		catch
		{
			File.Delete(path);
			throw;
		}
		File.SetLastWriteTime(path, m.DateTaken);
		Android.Media.MediaScannerConnection.ScanFile(Platform.AppContext, [path], [m.MimeType], null);
	}

	static async Task CopyVerifiedAsync(Stream source, Stream output, string sha256, CancellationToken ct)
	{
		using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		var buffer = new byte[81920];
		int n;
		while ((n = await source.ReadAsync(buffer, ct)) > 0)
		{
			hash.AppendData(buffer, 0, n);
			await output.WriteAsync(buffer.AsMemory(0, n), ct);
		}
		if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(sha256, StringComparison.OrdinalIgnoreCase))
			throw new IOException("The download didn't match the original");
	}

	/// <summary>Seekable stream over the original file (location intact when permission allows), for resumable uploads.</summary>
	public static Stream OpenOriginal(string contentUri)
	{
		var uri = Uri.Parse(contentUri)!;
		if (OperatingSystem.IsAndroidVersionAtLeast(29))
		{
			try
			{
				return Open(MediaStore.SetRequireOriginal(uri)!);
			}
			catch (Java.Lang.SecurityException)
			{
				// No ACCESS_MEDIA_LOCATION: upload anyway, just without GPS.
			}
		}
		return Open(uri);
	}

	static Stream Open(Uri uri)
	{
		var pfd = Resolver.OpenFileDescriptor(uri, "r") ?? throw new FileNotFoundException("Media is gone from the phone", uri.ToString());
		return new FileStream(new SafeFileHandle(pfd.DetachFd(), ownsHandle: true), FileAccess.Read);
	}

	/// <summary>JPEG thumbnail bytes; null when unavailable. shortcut: Android 10+ only; older phones show a blank tile.</summary>
	public static byte[]? Thumbnail(DeviceMedia m, int size)
	{
		if (!OperatingSystem.IsAndroidVersionAtLeast(29)) return null;
		try
		{
			using var bitmap = Resolver.LoadThumbnail(m.ContentUri, new Android.Util.Size(size, size), null);
			using var ms = new MemoryStream();
			bitmap.Compress(Bitmap.CompressFormat.Jpeg!, 80, ms);
			return ms.ToArray();
		}
		catch (Java.IO.IOException)
		{
			return null;
		}
	}
}
