namespace Lore.App.Services;

public static class Format
{
	/// <summary>"51.506°N, 0.126°W": one format everywhere a location is shown.</summary>
	public static string Coordinates(double lat, double lon) =>
		$"{Math.Abs(lat):0.###}°{(lat >= 0 ? "N" : "S")}, {Math.Abs(lon):0.###}°{(lon >= 0 ? "E" : "W")}";

	public static string Plural(int n, string word) => $"{n:N0} {word}{(n == 1 ? "" : "s")}";

	public static string Bytes(long bytes) => bytes switch
	{
		>= 1L << 40 => $"{bytes / (double)(1L << 40):0.##} TB",
		>= 1L << 30 => $"{bytes / (double)(1L << 30):0.#} GB",
		>= 1L << 20 => $"{bytes / (double)(1L << 20):0.#} MB",
		>= 1L << 10 => $"{bytes / 1024.0:0} KB",
		_ => $"{bytes} B",
	};
}
