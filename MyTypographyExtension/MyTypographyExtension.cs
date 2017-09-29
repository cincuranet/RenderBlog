using Markdig;
using Markdig.Parsers.Inlines;
using Markdig.Renderers;

namespace MyTypographyExtension
{
	class MyTypographyExtension : IMarkdownExtension
	{
		public void Setup(MarkdownPipelineBuilder pipeline)
		{
			if (!pipeline.InlineParsers.Contains<MyTypographyInlineParser>())
			{
				pipeline.InlineParsers.InsertAfter<CodeInlineParser>(new MyTypographyInlineParser());
			}
		}

		public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
		{ }
	}
}
