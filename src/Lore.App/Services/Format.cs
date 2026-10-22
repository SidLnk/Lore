namespace Lore.App.Services;

public static class Format
{
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
