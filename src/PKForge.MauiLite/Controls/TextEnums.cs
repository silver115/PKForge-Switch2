namespace Microsoft.Maui
{
    public enum TextAlignment { Start, Center, End, Justify }

    public enum LineBreakMode { NoWrap, WordWrap, CharacterWrap, HeadTruncation, TailTruncation, MiddleTruncation }

    [Flags]
    public enum TextDecorations { None = 0, Underline = 1 << 0, Strikethrough = 1 << 1 }

    public enum TextTransform { None, Default, Lowercase, Uppercase }

    public enum Aspect { AspectFit, AspectFill, Fill, Center }
}

namespace Microsoft.Maui.Controls
{
    [Flags]
    public enum FontAttributes { None = 0, Bold = 1 << 0, Italic = 1 << 1 }

    public enum TextType { Text, Html }
}
