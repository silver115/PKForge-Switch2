using System.Windows.Input;
using Microsoft.Maui.Graphics;
using PKForge.MauiLite.Rendering;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace Microsoft.Maui
{
    public enum ReturnType { Default, Done, Go, Next, Search, Send }

    public enum ClearButtonVisibility { Never, WhileEditing }

    [Flags]
    public enum KeyboardFlags { None = 0, CapitalizeSentence = 1, Spellcheck = 2, Suggestions = 4, CapitalizeWord = 8, CapitalizeCharacter = 16, CapitalizeNone = 32, All = -1 }
}

namespace Microsoft.Maui.Controls
{
    /// <summary>MAUI's keyboard kinds. On the Switch 2 they choose the software keyboard's layout (phase 2).</summary>
    public class Keyboard
    {
        Keyboard(string name) => Name = name;

        internal string Name { get; }

        public static Keyboard Default { get; } = new(nameof(Default));
        public static Keyboard Plain { get; } = new(nameof(Plain));
        public static Keyboard Text { get; } = new(nameof(Text));
        public static Keyboard Chat { get; } = new(nameof(Chat));
        public static Keyboard Email { get; } = new(nameof(Email));
        public static Keyboard Numeric { get; } = new(nameof(Numeric));
        public static Keyboard Telephone { get; } = new(nameof(Telephone));
        public static Keyboard Url { get; } = new(nameof(Url));

        public static Keyboard Create(KeyboardFlags flags) => Text;
    }

    public class TextChangedEventArgs(string? oldTextValue, string? newTextValue) : EventArgs
    {
        public string? OldTextValue { get; } = oldTextValue;
        public string? NewTextValue { get; } = newTextValue;
    }

    public class FocusEventArgs(VisualElement visualElement, bool isFocused) : EventArgs
    {
        public VisualElement VisualElement { get; } = visualElement;
        public bool IsFocused { get; } = isFocused;
    }

    /// <summary>
    /// MAUI's text input base. It draws and binds its text. Taking focus (a tap, or the app's own Focus call) opens the
    /// software keyboard when there is one (<see cref="PKForge.MauiLite.SoftKeyboard"/>).
    /// </summary>
    public abstract class InputView : View
    {
        public static readonly BindableProperty TextProperty = BindableProperty.Create(nameof(Text), typeof(string), typeof(InputView), null, BindingMode.TwoWay,
            propertyChanged: (bindable, old, now) => ((InputView)bindable).TextChanged?.Invoke(bindable, new TextChangedEventArgs((string?)old, (string?)now))).Measured();
        public static readonly BindableProperty TextColorProperty = BindableProperty.Create(nameof(TextColor), typeof(Color), typeof(InputView), null);
        public static readonly BindableProperty PlaceholderProperty = BindableProperty.Create(nameof(Placeholder), typeof(string), typeof(InputView), null).Measured();
        public static readonly BindableProperty PlaceholderColorProperty = BindableProperty.Create(nameof(PlaceholderColor), typeof(Color), typeof(InputView), null);
        public static readonly BindableProperty FontFamilyProperty = BindableProperty.Create(nameof(FontFamily), typeof(string), typeof(InputView), null).Measured();
        public static readonly BindableProperty FontSizeProperty = BindableProperty.Create(nameof(FontSize), typeof(double), typeof(InputView), 14d).Measured();
        public static readonly BindableProperty FontAttributesProperty = BindableProperty.Create(nameof(FontAttributes), typeof(FontAttributes), typeof(InputView), FontAttributes.None).Measured();
        public static readonly BindableProperty CharacterSpacingProperty = BindableProperty.Create(nameof(CharacterSpacing), typeof(double), typeof(InputView), 0d).Measured();
        public static readonly BindableProperty MaxLengthProperty = BindableProperty.Create(nameof(MaxLength), typeof(int), typeof(InputView), int.MaxValue);
        public static readonly BindableProperty KeyboardProperty = BindableProperty.Create(nameof(Keyboard), typeof(Keyboard), typeof(InputView), Keyboard.Default);
        public static readonly BindableProperty IsSpellCheckEnabledProperty = BindableProperty.Create(nameof(IsSpellCheckEnabled), typeof(bool), typeof(InputView), true);
        public static readonly BindableProperty IsTextPredictionEnabledProperty = BindableProperty.Create(nameof(IsTextPredictionEnabled), typeof(bool), typeof(InputView), true);
        public static readonly BindableProperty IsReadOnlyProperty = BindableProperty.Create(nameof(IsReadOnly), typeof(bool), typeof(InputView), false);
        public static readonly BindableProperty CursorPositionProperty = BindableProperty.Create(nameof(CursorPosition), typeof(int), typeof(InputView), 0);
        public static readonly BindableProperty SelectionLengthProperty = BindableProperty.Create(nameof(SelectionLength), typeof(int), typeof(InputView), 0);
        public static readonly BindableProperty HorizontalTextAlignmentProperty = BindableProperty.Create(nameof(HorizontalTextAlignment), typeof(TextAlignment), typeof(InputView), TextAlignment.Start);
        public static readonly BindableProperty VerticalTextAlignmentProperty = BindableProperty.Create(nameof(VerticalTextAlignment), typeof(TextAlignment), typeof(InputView), TextAlignment.Center);

        public string? Text { get => (string?)GetValue(TextProperty); set => SetValue(TextProperty, value); }
        public Color? TextColor { get => (Color?)GetValue(TextColorProperty); set => SetValue(TextColorProperty, value); }
        public string? Placeholder { get => (string?)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
        public Color? PlaceholderColor { get => (Color?)GetValue(PlaceholderColorProperty); set => SetValue(PlaceholderColorProperty, value); }
        public string? FontFamily { get => (string?)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
        public double FontSize { get => (double)GetValue(FontSizeProperty)!; set => SetValue(FontSizeProperty, value); }
        public FontAttributes FontAttributes { get => (FontAttributes)GetValue(FontAttributesProperty)!; set => SetValue(FontAttributesProperty, value); }
        public double CharacterSpacing { get => (double)GetValue(CharacterSpacingProperty)!; set => SetValue(CharacterSpacingProperty, value); }
        public int MaxLength { get => (int)GetValue(MaxLengthProperty)!; set => SetValue(MaxLengthProperty, value); }
        public Keyboard Keyboard { get => (Keyboard)GetValue(KeyboardProperty)!; set => SetValue(KeyboardProperty, value); }
        public bool IsSpellCheckEnabled { get => (bool)GetValue(IsSpellCheckEnabledProperty)!; set => SetValue(IsSpellCheckEnabledProperty, value); }
        public bool IsTextPredictionEnabled { get => (bool)GetValue(IsTextPredictionEnabledProperty)!; set => SetValue(IsTextPredictionEnabledProperty, value); }
        public bool IsReadOnly { get => (bool)GetValue(IsReadOnlyProperty)!; set => SetValue(IsReadOnlyProperty, value); }
        public int CursorPosition { get => (int)GetValue(CursorPositionProperty)!; set => SetValue(CursorPositionProperty, value); }
        public int SelectionLength { get => (int)GetValue(SelectionLengthProperty)!; set => SetValue(SelectionLengthProperty, value); }
        public TextAlignment HorizontalTextAlignment { get => (TextAlignment)GetValue(HorizontalTextAlignmentProperty)!; set => SetValue(HorizontalTextAlignmentProperty, value); }
        public TextAlignment VerticalTextAlignment { get => (TextAlignment)GetValue(VerticalTextAlignmentProperty)!; set => SetValue(VerticalTextAlignmentProperty, value); }

        public bool IsFocused { get; private set; }

        public event EventHandler<TextChangedEventArgs>? TextChanged;
        public event EventHandler<FocusEventArgs>? Focused;
        public event EventHandler<FocusEventArgs>? Unfocused;

        public bool Focus()
        {
            if (!IsFocused)
            {
                IsFocused = true;
                Focused?.Invoke(this, new FocusEventArgs(this, true));
            }
            // On every focus, as Android's keyboard comes back when a field is tapped again. It blocks while it shows.
            if (!IsReadOnly && PKForge.MauiLite.SoftKeyboard.Show is { } show
                && show(new PKForge.MauiLite.SoftKeyboardRequest(Text ?? "", Placeholder, MaxLength, Keyboard == Keyboard.Numeric, this is Editor)) is { } text)
            {
                Text = text;
                (this as Entry)?.SendCompleted();
                (this as Editor)?.SendCompleted();
            }
            return true;
        }

        public void Unfocus()
        {
            if (!IsFocused)
                return;
            IsFocused = false;
            Unfocused?.Invoke(this, new FocusEventArgs(this, false));
        }

        /// <summary>Android's text field padding, in units.</summary>
        static readonly Thickness FieldPadding = new(4, 10);

        TextStyle Style(bool placeholder) => new(FontFamily, FontSize, FontAttributes,
            (placeholder ? PlaceholderColor : TextColor)?.ToSKColor() ?? (placeholder ? SKColors.Gray : SKColors.Black), null, CharacterSpacing, TextDecorations.None);

        bool ShowsPlaceholder => string.IsNullOrEmpty(Text) && !string.IsNullOrEmpty(Placeholder);

        private protected virtual LineBreakMode Wrapping => LineBreakMode.NoWrap;

        TextLayout Layout(float widthPixels) => TextLayout.Build([((ShowsPlaceholder ? Placeholder : Text) ?? "", Style(ShowsPlaceholder))],
            widthPixels, Wrapping, -1, -1, Display.Density);

        internal override Size MeasureContent(double availableWidth, double availableHeight)
        {
            var scale = Display.Density;
            var width = double.IsInfinity(availableWidth) ? float.PositiveInfinity : (float)Math.Max(0, (availableWidth - FieldPadding.HorizontalThickness) * scale);
            var layout = Layout(width);
            // A text field takes the width it is offered, as Android's EditText does.
            var contentWidth = double.IsInfinity(availableWidth) ? layout.Width / scale + FieldPadding.HorizontalThickness : availableWidth;
            return new Size(contentWidth, layout.Height / scale + FieldPadding.VerticalThickness);
        }

        // Laid out on the UI thread before drawing, which may run on several threads.
        TextLayout? _drawLayout;

        internal override void Prepare(SKSize size, float scale) =>
            _drawLayout = Layout(Math.Max(0, size.Width - (float)FieldPadding.HorizontalThickness * scale));

        internal override void Draw(SKCanvas canvas, SKSize size, float scale)
        {
            base.Draw(canvas, size, scale);
            var left = (float)FieldPadding.Left * scale;
            var width = Math.Max(0, size.Width - (float)FieldPadding.HorizontalThickness * scale);
            var layout = _drawLayout ?? Layout(width);
            var top = VerticalTextAlignment switch
            {
                TextAlignment.Start => (float)FieldPadding.Top * scale,
                TextAlignment.End => size.Height - (float)FieldPadding.Bottom * scale - layout.Height,
                _ => (size.Height - layout.Height) / 2,
            };
            canvas.Save();
            canvas.ClipRect(SKRect.Create(size));
            using var paint = new SKPaint { IsAntialias = true };
            foreach (var line in layout.Lines)
            {
                var x = left + HorizontalTextAlignment switch
                {
                    TextAlignment.Center => (width - line.Width) / 2,
                    TextAlignment.End => width - line.Width,
                    _ => 0,
                };
                var baseline = top + line.Top + (line.Height - (line.Descent - line.Ascent)) / 2 - line.Ascent;
                foreach (var piece in line.Pieces)
                {
                    paint.Color = piece.Style.Color;
                    canvas.DrawText(piece.Text, x + piece.X, baseline, SKTextAlign.Left, piece.Font, paint);
                }
            }
            canvas.Restore();
        }
    }

    public class Entry : InputView
    {
        public static readonly BindableProperty ReturnTypeProperty = BindableProperty.Create(nameof(ReturnType), typeof(ReturnType), typeof(Entry), ReturnType.Default);
        public static readonly BindableProperty ReturnCommandProperty = BindableProperty.Create(nameof(ReturnCommand), typeof(ICommand), typeof(Entry), null);
        public static readonly BindableProperty ReturnCommandParameterProperty = BindableProperty.Create(nameof(ReturnCommandParameter), typeof(object), typeof(Entry), null);
        public static readonly BindableProperty IsPasswordProperty = BindableProperty.Create(nameof(IsPassword), typeof(bool), typeof(Entry), false);
        public static readonly BindableProperty ClearButtonVisibilityProperty = BindableProperty.Create(nameof(ClearButtonVisibility), typeof(ClearButtonVisibility), typeof(Entry), ClearButtonVisibility.Never);

        public ReturnType ReturnType { get => (ReturnType)GetValue(ReturnTypeProperty)!; set => SetValue(ReturnTypeProperty, value); }
        public ICommand? ReturnCommand { get => (ICommand?)GetValue(ReturnCommandProperty); set => SetValue(ReturnCommandProperty, value); }
        public object? ReturnCommandParameter { get => GetValue(ReturnCommandParameterProperty); set => SetValue(ReturnCommandParameterProperty, value); }
        public bool IsPassword { get => (bool)GetValue(IsPasswordProperty)!; set => SetValue(IsPasswordProperty, value); }
        public ClearButtonVisibility ClearButtonVisibility { get => (ClearButtonVisibility)GetValue(ClearButtonVisibilityProperty)!; set => SetValue(ClearButtonVisibilityProperty, value); }

        public event EventHandler? Completed;

        public void SendCompleted()
        {
            if (ReturnCommand is { } command && command.CanExecute(ReturnCommandParameter))
                command.Execute(ReturnCommandParameter);
            Completed?.Invoke(this, EventArgs.Empty);
        }
    }

    public enum EditorAutoSizeOption { Disabled, TextChanges }

    public class Editor : InputView
    {
        public static readonly BindableProperty AutoSizeProperty = BindableProperty.Create(nameof(AutoSize), typeof(EditorAutoSizeOption), typeof(Editor), EditorAutoSizeOption.Disabled).Measured();

        public Editor() => VerticalTextAlignment = TextAlignment.Start;

        public EditorAutoSizeOption AutoSize { get => (EditorAutoSizeOption)GetValue(AutoSizeProperty)!; set => SetValue(AutoSizeProperty, value); }

        public event EventHandler? Completed;

        public void SendCompleted() => Completed?.Invoke(this, EventArgs.Empty);

        private protected override LineBreakMode Wrapping => LineBreakMode.WordWrap;
    }
}
