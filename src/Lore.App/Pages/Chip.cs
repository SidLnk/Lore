using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;
using Path = Microsoft.Maui.Controls.Shapes.Path;

namespace Lore.App.Pages;

/// <summary>
/// Material-style filter chip: a 36dp pill inside a 48dp touch area.
/// Inactive is outlined; active is a tonal green fill, so set filters read at a glance.
/// </summary>
public sealed class Chip : Grid
{
	public static readonly BindableProperty TextProperty =
		BindableProperty.Create(nameof(Text), typeof(string), typeof(Chip), "", propertyChanged: (b, _, n) => ((Chip)b)._label.Text = (string)n);
	public static readonly BindableProperty IsActiveProperty =
		BindableProperty.Create(nameof(IsActive), typeof(bool), typeof(Chip), false, propertyChanged: (b, _, _) => ((Chip)b).ApplyState());
	public static readonly BindableProperty CommandProperty =
		BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(Chip));
	public static readonly BindableProperty TrailingProperty =
		BindableProperty.Create(nameof(Trailing), typeof(ChipTrailing), typeof(Chip), ChipTrailing.None, propertyChanged: (b, _, _) => ((Chip)b).ApplyState());

	public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
	public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
	public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
	public ChipTrailing Trailing { get => (ChipTrailing)GetValue(TrailingProperty); set => SetValue(TrailingProperty, value); }

	readonly Border _pill;
	readonly Label _label = new() { FontSize = 14, VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation, MaximumWidthRequest = 220 };
	readonly Path _icon = new() { StrokeThickness = 1.75, WidthRequest = 10, HeightRequest = 10, VerticalOptions = LayoutOptions.Center, Aspect = Stretch.Uniform };

	public Chip()
	{
		HeightRequest = 48;
		_pill = new Border
		{
			HeightRequest = 36,
			VerticalOptions = LayoutOptions.Center,
			Padding = new Thickness(12, 0),
			StrokeShape = new RoundRectangle { CornerRadius = 8 },
			Content = new HorizontalStackLayout { Spacing = 6, Children = { _label, _icon } },
		};
		Add(_pill);
		GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => { if (Command?.CanExecute(null) == true) Command.Execute(null); }) });
		SemanticProperties.SetHint(this, "Filter");
		ApplyState();
	}

	void ApplyState()
	{
		var primary = Color.FromArgb("#2B4C3F");
		if (IsActive)
		{
			// Dark: a full Archive Green fill, since a deep tint vanishes against the off-black page.
			_pill.SetAppThemeColor(Border.BackgroundColorProperty, Color.FromArgb("#DCEBE3"), primary);
			_pill.SetAppTheme<Brush>(Border.StrokeProperty, Color.FromArgb("#DCEBE3"), primary);
			_label.SetAppThemeColor(Label.TextColorProperty, primary, Color.FromArgb("#DCEBE3"));
			_label.FontFamily = "OpenSansSemibold";
		}
		else
		{
			_pill.BackgroundColor = Colors.Transparent;
			_pill.SetAppTheme<Brush>(Border.StrokeProperty, Color.FromArgb("#ACACAC"), Color.FromArgb("#6E6E6E"));
			_label.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#212121"), Colors.White);
			_label.FontFamily = "OpenSansRegular";
		}
		_pill.StrokeThickness = 1;
		_icon.SetAppTheme<Brush>(Shape.StrokeProperty, IsActive ? primary : Color.FromArgb("#212121"), IsActive ? Color.FromArgb("#DCEBE3") : Colors.White);
		_icon.IsVisible = Trailing != ChipTrailing.None;
		_icon.Data = Trailing == ChipTrailing.Close
			? (Geometry)new PathGeometryConverter().ConvertFromInvariantString("M1,1 L9,9 M9,1 L1,9")!
			: (Geometry)new PathGeometryConverter().ConvertFromInvariantString("M1,3 L5,7 L9,3")!;
		SemanticProperties.SetDescription(this, IsActive ? $"{Text}, active" : Text);
	}
}

public enum ChipTrailing { None, Chevron, Close }
