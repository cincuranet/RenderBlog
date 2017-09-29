using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Syntax.Inlines;

namespace MyTypographyExtension
{
	class MyTypographyInlineParser : InlineParser
	{
		const char SingleQuote = '\'';
		const char DoubleQuote = '"';
		const char Dot = '.';

		public MyTypographyInlineParser()
		{
			OpeningCharacters = new[] { SingleQuote, DoubleQuote, Dot };
		}

		public override bool Match(InlineProcessor processor, ref StringSlice slice)
		{
			var c = slice.CurrentChar;
			var startPosition = slice.Start;
			switch (c)
			{
				case SingleQuote:
					{
						slice.Start += 1;
						processor.Inline = new HtmlInline() { Tag = "&rsquo;" };
						return true;
					}
				case DoubleQuote:
					{
						var nextChar = slice.NextChar();
						if (nextChar.IsWhiteSpaceOrZero())
						{
							processor.Inline = new HtmlInline() { Tag = "&rdquo;" };
						}
						else
						{
							processor.Inline = new HtmlInline() { Tag = "&ldquo;" };
						}
						return true;
					}
				case Dot:
					{
						if (slice.NextChar() == Dot && slice.NextChar() == Dot)
						{
							slice.Start += 1;
							processor.Inline = new HtmlInline() { Tag = "&hellip;" };
							return true;
						}
						break;
					}
			}
			return false;
		}
	}
}
