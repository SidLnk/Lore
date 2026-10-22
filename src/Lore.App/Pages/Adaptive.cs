namespace Lore.App.Pages;

/// <summary>Window-size rules shared by the pages, so tablets, landscape and split screen restructure instead of stretching.</summary>
public static class Adaptive
{
	/// <summary>Material's medium width class starts here: the bottom tabs become a navigation rail.</summary>
	public const double RailWidth = 600;

	/// <summary>Keeps grid tiles near <paramref name="tile"/> dp wide, never with fewer columns than the phone layout.</summary>
	public static void FitColumns(CollectionView view, int phoneSpan, double tile, Action<int>? changed = null)
	{
		view.SizeChanged += (_, _) =>
		{
			if (view.Width <= 0 || view.ItemsLayout is not GridItemsLayout grid) return;
			var span = Math.Max(phoneSpan, (int)(view.Width / tile));
			if (span == grid.Span) return;
			grid.Span = span;
			changed?.Invoke(span);
		};
	}

	/// <summary>
	/// Keeps a list or form page to a readable column of <paramref name="max"/> dp, aligned under the page title
	/// (which stays at the start edge) rather than centred away from it.
	/// </summary>
	public static void CapWidth(ContentPage page, double max = 640) =>
		page.SizeChanged += (_, _) => page.Padding = new Thickness(0, 0, Math.Max(0, Math.Floor(page.Width - max)), 0);
}

/// <summary>A grid as tall as it is wide: photo tiles stay square whatever the column count.</summary>
public sealed class SquareGrid : Grid
{
	protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
	{
		if (double.IsInfinity(widthConstraint)) return base.MeasureOverride(widthConstraint, heightConstraint);
		base.MeasureOverride(widthConstraint, widthConstraint);
		return new Size(widthConstraint, widthConstraint);
	}
}
