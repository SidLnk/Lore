using Android;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Provider;
using Microsoft.Win32.SafeHandles;
using Uri = Android.Net.Uri;

namespace Lore.App.Droid;

public sealed record DeviceMedia(long Id, string Name, string MimeType, long Size, DateTime? Taken, bool IsVideo)
{
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
			.. OperatingSystem.IsAndroidVersionAtLeast(29) ? [(Manifest.Permission.AccessMediaLocation, true)] : Array.Empty<(string, bool)>(),
		];
	}

	static ContentResolver Resolver => Platform.AppContext.ContentResolver!;

	/// <summary>Newest first, by date added.</summary>
	public static List<DeviceMedia> Query(int offset, int limit)
	{
		string[] projection =
		[
			IBaseColumns.Id, MediaStore.IMediaColumns.DisplayName, MediaStore.IMediaColumns.MimeType,
			MediaStore.IMediaColumns.Size, "datetaken" /* column predates the API 29 constant */, MediaStore.Files.IFileColumns.MediaType,
		];
		var args = new Bundle();
		args.PutString(ContentResolver.QueryArgSqlSelection, $"{MediaStore.Files.IFileColumns.MediaType} IN (1, 3)"); // image, video
		// MediaProvider ignores QueryArgSortColumns; the SQL sort order is honoured.
		args.PutString(ContentResolver.QueryArgSqlSortOrder, $"{MediaStore.IMediaColumns.DateAdded} DESC, {IBaseColumns.Id} DESC");
		args.PutInt(ContentResolver.QueryArgLimit, limit);
		args.PutInt(ContentResolver.QueryArgOffset, offset);

		var result = new List<DeviceMedia>();
		using var c = Resolver.Query(MediaStore.Files.GetContentUri("external")!, projection, args, null);
		if (c == null) return result;
		while (c.MoveToNext())
		{
			var taken = c.IsNull(4) ? (DateTime?)null : DateTimeOffset.FromUnixTimeMilliseconds(c.GetLong(4)).LocalDateTime;
			result.Add(new DeviceMedia(c.GetLong(0), c.GetString(1) ?? $"media_{c.GetLong(0)}", c.GetString(2) ?? "", c.GetLong(3), taken, c.GetInt(5) == 3));
		}
		return result;
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
